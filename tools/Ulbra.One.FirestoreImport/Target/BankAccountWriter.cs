using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes legacy `santander_config` (payer bank accounts) into `finance.bank_accounts`. Natural key
/// is <c>(company_id, bank_code, agency, account_number, account_digit)</c> (partial unique on not-deleted);
/// company is resolved by the doc's CNPJ, falling back to Ulbra S.A. The legacy→One id map (`bank_account`
/// entity) lets the Payable import resolve each payment's payer account via the remittance chain.</summary>
public sealed class BankAccountWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "bank_account";
    private const string Actor = "firestore-migration";
    private const string SantanderBankCode = "033";

    private const string UpsertSql = """
        insert into finance.bank_accounts (
            id, company_id, bank_code, bank_name, agency, agency_digit, account_number, account_digit,
            account_type, alias, cnab_agreement_code, payer_cnpj, payer_legal_name,
            is_default_for_payments, active,
            address_cep, address_street, address_number, address_complement, address_city, address_state,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @companyId, @bankCode, @bankName, @agency, @agencyDigit, @accountNumber, @accountDigit,
            0, @alias, @agreement, @payerCnpj, @payerLegalName,
            false, true,
            @cep, @street, @number, @complement, @city, @state,
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (company_id, bank_code, agency, account_number, account_digit) where deleted_at is null
        do update set
            bank_name = excluded.bank_name,
            account_type = excluded.account_type,
            alias = excluded.alias,
            cnab_agreement_code = excluded.cnab_agreement_code,
            payer_cnpj = excluded.payer_cnpj,
            payer_legal_name = excluded.payer_legal_name,
            active = true,
            address_cep = excluded.address_cep,
            address_street = excluded.address_street,
            address_number = excluded.address_number,
            address_complement = excluded.address_complement,
            address_city = excluded.address_city,
            address_state = excluded.address_state,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = @actor
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var d in source.ReadBankAccountsAsync(ct))
        {
            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);

            var row = Map(d, org, out var error);
            if (row is null)
            {
                if (!dryRun) await store.DeadLetterAsync(EntityType, d.Id, error!, payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            priorMap.TryGetValue(d.Id, out var prior);
            if (prior.ContentHash == row.Hash)
            {
                counters.Count(RowOutcome.Skipped);
                continue;
            }

            if (dryRun)
            {
                counters.Count(prior.TargetId == default ? RowOutcome.Inserted : RowOutcome.Updated);
                continue;
            }

            counters.Count(await UpsertRowAsync(conn, d.Id, row, prior, payload, ct));
        }

        return counters.ToSummary("bank-accounts", dryRun);
    }

    private static BankAccountRow? Map(BankAccountDoc d, OrgResolver org, out string? error)
    {
        error = null;
        var cnpj = DocumentNormalizer.NormalizeCnpj(d.Cnpj);
        var companyId = org.TryResolveCompanyByCnpj(cnpj, out var byCnpj) ? byCnpj : org.PrimaryCompanyId;
        if (companyId is null)
        {
            error = $"empresa não resolvida para o CNPJ pagador '{d.Cnpj}' (nem empresa primária)";
            return null;
        }

        var account = Text.Trunc(DocumentNormalizer.Normalize(d.Account), 20);
        if (account.Length == 0)
        {
            error = "conta bancária vazia";
            return null;
        }

        return new BankAccountRow(
            CompanyId: companyId.Value,
            BankCode: SantanderBankCode,
            BankName: "Santander",
            Agency: Text.Trunc(DocumentNormalizer.Normalize(d.Branch), 10),
            AgencyDigit: Text.Trunc(DocumentNormalizer.Normalize(d.BranchDigit), 2),
            AccountNumber: account,
            AccountDigit: Text.Trunc(DocumentNormalizer.Normalize(d.AccountDigit), 2),
            Alias: Text.Trunc(string.IsNullOrWhiteSpace(d.LegalName) ? $"Conta {account}" : d.LegalName!, 60),
            Agreement: Text.Trunc(d.Agreement, 30),
            PayerCnpj: Text.Trunc(cnpj, 14),
            PayerLegalName: Text.Trunc(string.IsNullOrWhiteSpace(d.LegalName) ? "Migração" : d.LegalName!, 100),
            Cep: Text.Trunc(DocumentNormalizer.Normalize(d.PostalCode), 8),
            Street: Text.Trunc(d.Street, 200),
            Number: Text.Trunc(d.Number, 20),
            Complement: Text.Trunc(d.Complement, 100),
            City: Text.Trunc(d.City, 100),
            State: Text.TruncUpper(d.State, 2),
            CreatedAt: d.CreatedAt,
            UpdatedAt: d.UpdatedAt);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, string legacyId, BankAccountRow row, IdMapEntry prior, string payload, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var id = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            var result = await conn.QuerySingleAsync<UpsertResult>(
                new CommandDefinition(UpsertSql, row.ToParams(id, Actor), tx, cancellationToken: ct));
            await ImportStore.UpsertMapAsync(conn, tx, EntityType, legacyId, result.Id, row.Hash, ct);
            await tx.CommitAsync(ct);
            return result.Inserted ? RowOutcome.Inserted : RowOutcome.Updated;
        }
        catch (PostgresException ex)
        {
            await tx.RollbackAsync(ct);
            await store.DeadLetterAsync(EntityType, legacyId, ex.MessageText, payload, ct);
            return RowOutcome.DeadLettered;
        }
    }

    private readonly record struct UpsertResult(Guid Id, bool Inserted);
}

/// <summary>Mapped values for one bank account (owned address flattened to NOT NULL columns).</summary>
public sealed record BankAccountRow(
    Guid CompanyId, string BankCode, string BankName, string Agency, string AgencyDigit,
    string AccountNumber, string AccountDigit, string Alias, string Agreement, string PayerCnpj,
    string PayerLegalName, string Cep, string Street, string Number, string Complement, string City, string State,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt)
{
    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        CompanyId, BankCode, Agency, AgencyDigit, AccountNumber, AccountDigit, Alias, Agreement,
        PayerCnpj, PayerLegalName, Cep, Street, Number, Complement, City, State,
        CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? ""));

    public object ToParams(Guid id, string actor) => new
    {
        id,
        companyId = CompanyId,
        bankCode = BankCode,
        bankName = BankName,
        agency = Agency,
        agencyDigit = AgencyDigit,
        accountNumber = AccountNumber,
        accountDigit = AccountDigit,
        alias = Alias,
        agreement = Agreement,
        payerCnpj = PayerCnpj,
        payerLegalName = PayerLegalName,
        cep = Cep,
        street = Street,
        number = Number,
        complement = Complement,
        city = City,
        state = State,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
