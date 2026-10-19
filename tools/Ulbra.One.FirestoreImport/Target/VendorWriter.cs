using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes `fornecedores` into `registry.vendors`. Replicates the EF write contract by hand:
/// audit columns (no DB default), <c>person_type</c> as int vs <c>tax_regime</c>/<c>payment_schedule_mode</c>
/// as enum-name text, the flattened NOT NULL owned columns (empty string, never null), the jsonb defaults,
/// and no <c>company_id</c> (dropped). Upsert is by the natural key <c>document</c>; each row runs in its
/// own transaction so one bad row is dead-lettered without losing the batch.</summary>
public sealed class VendorWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "vendor";
    private const string Actor = "firestore-migration";

    private const string UpsertSql = """
        insert into registry.vendors (
            id, person_type, name, document, trade_name, email, phone, payment_terms,
            tax_regime, simples_anexo, cprb_foreseen, primary_cnae_code, primary_cnae_description,
            accepted_payment_methods, withholding_rates, associated_ledger_account_ids, active,
            address_cep, address_street, address_number, address_complement,
            address_neighborhood, address_city, address_state,
            bank_info_bank_code, bank_info_branch, bank_info_account, bank_info_account_digit, bank_info_pix_key,
            payment_schedule_mode, payment_schedule_day_of_month,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @personType, @name, @document, null, null, null, null,
            @taxRegime, null, null, null, null,
            '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, true,
            @cep, @street, @number, @complement,
            @neighborhood, @city, @state,
            @bankCode, @branch, @account, @accountDigit, @pixKey,
            @scheduleMode, null,
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (document) where deleted_at is null do update set
            person_type = excluded.person_type,
            name = excluded.name,
            tax_regime = excluded.tax_regime,
            address_cep = excluded.address_cep,
            address_street = excluded.address_street,
            address_number = excluded.address_number,
            address_complement = excluded.address_complement,
            address_neighborhood = excluded.address_neighborhood,
            address_city = excluded.address_city,
            address_state = excluded.address_state,
            bank_info_bank_code = excluded.bank_info_bank_code,
            bank_info_branch = excluded.bank_info_branch,
            bank_info_account = excluded.bank_info_account,
            bank_info_account_digit = excluded.bank_info_account_digit,
            bank_info_pix_key = excluded.bank_info_pix_key,
            payment_schedule_mode = excluded.payment_schedule_mode,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = @actor
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    public async Task<ImportSummary> ImportAsync(FirestoreSource source, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var d in source.ReadVendorsAsync(ct))
        {
            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);
            var row = Map(d);

            if (row is null)
            {
                if (!dryRun)
                    await store.DeadLetterAsync(EntityType, d.Id, "documento (cnpjCpf) vazio", payload, ct);
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

        return counters.ToSummary("vendors", dryRun);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, string legacyId, VendorRow row, IdMapEntry prior, string payload, CancellationToken ct)
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

    private static VendorRow? Map(VendorDoc d)
    {
        var doc = VendorMapping.ResolveDocument(d.Id);
        if (doc.Document.Length == 0) return null;

        var name = string.IsNullOrWhiteSpace(d.LegalName) ? doc.Document : d.LegalName;
        var complement = string.IsNullOrWhiteSpace(d.Complement) ? null : Text.Trunc(d.Complement, 100);

        return new VendorRow(
            PersonType: doc.PersonType,
            Name: Text.Trunc(name, 200),
            Document: Text.Trunc(doc.Document, 14),
            TaxRegime: VendorMapping.ResolveTaxRegime(d.TaxRegime),
            Cep: Text.Trunc(DocumentNormalizer.Normalize(d.PostalCode), 8),
            Street: Text.Trunc(d.Street, 200),
            Number: Text.Trunc(d.Number, 20),
            Complement: complement,
            Neighborhood: Text.Trunc(d.Neighborhood, 100),
            City: Text.Trunc(d.City, 100),
            State: Text.TruncUpper(d.State, 2),
            BankCode: Text.Trunc(d.BankCode, 10),
            Branch: Text.Trunc(d.Branch, 10),
            Account: Text.Trunc(d.Account, 20),
            AccountDigit: Text.Trunc(d.AccountDigit, 2),
            PixKey: Text.Trunc(PixKeyNormalizer.Normalize(d.PixKey), 100),
            ScheduleMode: VendorMapping.PaymentScheduleModeDefault,
            CreatedAt: d.CreatedAt,
            UpdatedAt: d.UpdatedAt);
    }

    private readonly record struct UpsertResult(Guid Id, bool Inserted);
}

/// <summary>The mapped target values for one vendor row, plus a content hash for idempotency.</summary>
public sealed record VendorRow(
    int PersonType, string Name, string Document, string TaxRegime,
    string Cep, string Street, string Number, string? Complement,
    string Neighborhood, string City, string State,
    string BankCode, string Branch, string Account, string AccountDigit, string PixKey,
    string ScheduleMode, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt)
{
    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        PersonType, Name, Document, TaxRegime, Cep, Street, Number, Complement ?? "",
        Neighborhood, City, State, BankCode, Branch, Account, AccountDigit, PixKey, ScheduleMode,
        CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? ""));

    public object ToParams(Guid id, string actor) => new
    {
        id,
        personType = PersonType,
        name = Name,
        document = Document,
        taxRegime = TaxRegime,
        cep = Cep,
        street = Street,
        number = Number,
        complement = Complement,
        neighborhood = Neighborhood,
        city = City,
        state = State,
        bankCode = BankCode,
        branch = Branch,
        account = Account,
        accountDigit = AccountDigit,
        pixKey = PixKey,
        scheduleMode = ScheduleMode,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
