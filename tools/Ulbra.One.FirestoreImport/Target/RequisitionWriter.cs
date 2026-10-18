using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes legacy `requisicoes` into `budgeting.requisitions` (+ `requisition_installment` and, for
/// realized ones, `requisition_payment`) DIRECTLY (raw SQL, no domain/outbox → no Payables created), so the
/// existing BudgetControlService derives budget consumption from them. Status/type/frequency are mapped so
/// the per-competency contribution matches the app; the installment plan (parcelas) is reproduced from the
/// legacy count/frequency/first-due so the record is faithful (Σ installment amounts == estimate_value, the
/// domain invariant). Callers pass the set of One statuses to import (a "stage"), so paid/committed/queued
/// solicitações can be brought on demand and re-run idempotently. The natureza is provisioned in the unit's
/// company if missing (same rule as budgets); a missing unit/vendor is dead-lettered.</summary>
public sealed class RequisitionWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "requisition";
    private const string Actor = "firestore-migration";
    private const string InstallmentMethod = "Other"; // legacy carries no per-installment method

    // Legacy seqNum is NOT unique (duplicates exist), and `requisitions.code` is unique per company. Assign a
    // deterministic synthetic code (offset + stable GLOBAL rank by legacy id) so it is unique AND stable
    // across runs and across stage selections, and never collides with the small seed codes.
    private const int CodeBase = 1_000_000;

    private const string UpsertSql = """
        insert into budgeting.requisitions (
            id, company_id, cost_center_id, unit_id, vendor_id, code, type, description, observation,
            estimate_value, estimate_type, start_competency, end_competency, status, status_changed_at,
            payment_frequency, payment_installments_count, first_due_date, issue_date,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @companyId, @costCenterId, @unitId, @vendorId, @code, @type, @description, @observation,
            @estimateValue, @estimateType, @startCompetency, @endCompetency, @status, now(),
            @paymentFrequency, @paymentInstallmentsCount, cast(@firstDueDate as date), cast(@issueDate as date),
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (company_id, code) do update set
            cost_center_id = excluded.cost_center_id,
            unit_id = excluded.unit_id,
            vendor_id = excluded.vendor_id,
            type = excluded.type,
            description = excluded.description,
            observation = excluded.observation,
            estimate_value = excluded.estimate_value,
            estimate_type = excluded.estimate_type,
            start_competency = excluded.start_competency,
            end_competency = excluded.end_competency,
            status = excluded.status,
            status_changed_at = now(),
            payment_frequency = excluded.payment_frequency,
            payment_installments_count = excluded.payment_installments_count,
            first_due_date = excluded.first_due_date,
            issue_date = excluded.issue_date,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = @actor
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    private const string DeleteInstallmentsSql =
        "delete from budgeting.requisition_installment where requisition_id = @reqId";

    private const string InsertInstallmentSql = """
        insert into budgeting.requisition_installment (
            id, requisition_id, sequence, due_date, amount, payment_method, boleto_digitable_line, pix_key_override)
        values (@id, @reqId, @sequence, cast(@dueDate as date), @amount, @paymentMethod, null, null)
        """;

    /// <summary>Imports the solicitações whose mapped status is in <paramref name="stages"/> (paid,
    /// committed or queued). All docs are read and globally ranked first (so the synthetic code is stable
    /// regardless of which stage runs), then filtered to the stage.</summary>
    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, IReadOnlySet<string> stages, string entityLabel,
        bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Buffer + stable sort by legacy id so the synthetic code is a deterministic GLOBAL rank.
        var docs = new List<RequisitionDoc>();
        await foreach (var d in source.ReadRequisitionsAsync(ct)) docs.Add(d);
        docs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        for (var index = 0; index < docs.Count; index++)
        {
            var d = docs[index];
            var status = RequisitionMapping.MapStatus(d.Status);
            if (!stages.Contains(status)) continue; // not this stage's job (keeps the global rank intact)

            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);

            var row = Map(d, status, CodeBase + index, org, out var error);
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

            counters.Count(await UpsertRowAsync(conn, org, d.Id, row, prior, payload, ct));
        }

        return counters.ToSummary(entityLabel, dryRun);
    }

    private static RequisitionRow? Map(RequisitionDoc d, string status, int code, OrgResolver org, out string? error)
    {
        error = null;
        if (!org.TryResolveUnit(d.UnitCode, out var unit))
        {
            error = $"unidade '{d.UnitCode}' não encontrada no org-alvo";
            return null;
        }

        var vendorDoc = VendorMapping.ResolveDocument(d.ProviderCnpj ?? string.Empty).Document;
        if (vendorDoc.Length == 0 || !org.TryResolveVendor(vendorDoc, out var vendorId))
        {
            error = $"fornecedor '{d.ProviderCnpj}' não encontrado (import fornecedores primeiro)";
            return null;
        }

        if (!CompetencyRange.IsValid(d.StartCompetency))
        {
            error = $"startCompetency inválida: '{d.StartCompetency}'";
            return null;
        }

        var plan = RequisitionMapping.ResolvePlan(d);
        var description = Text.Trunc(string.IsNullOrWhiteSpace(d.Description) ? $"Migrado {d.Id}" : d.Description, 500);
        var observation = string.IsNullOrWhiteSpace(d.Observation) ? null : Text.Trunc(d.Observation, 1000);

        return new RequisitionRow(
            unit.CompanyId, unit.UnitId, vendorId, d.NatureName ?? string.Empty, code,
            plan.Type, description, observation, plan.EstimateValue, plan.EstimateType,
            plan.StartCompetency, plan.EndCompetency, status,
            plan.PaymentFrequency, plan.PaymentInstallmentsCount, plan.Installments,
            d.CreatedAt, d.UpdatedAt);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, OrgResolver org, string legacyId, RequisitionRow row, IdMapEntry prior, string payload, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var costCenterId = await org.EnsureCostCenterAsync(row.CompanyId, row.NatureName, ct);

            var id = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            var result = await conn.QuerySingleAsync<UpsertResult>(
                new CommandDefinition(UpsertSql, row.ToParams(id, costCenterId, Actor), tx, cancellationToken: ct));

            // Installments mirror the source each run: clear and re-insert. The realized payments
            // (budgeting.requisition_payment) are owned by the Payable import — derived from the real
            // `pagamentos` — so this writer never touches them (every paid requisition has pagamentos).
            await conn.ExecuteAsync(new CommandDefinition(DeleteInstallmentsSql, new { reqId = result.Id }, tx, cancellationToken: ct));
            foreach (var i in row.Installments)
                await conn.ExecuteAsync(new CommandDefinition(InsertInstallmentSql,
                    new { id = Guid.NewGuid(), reqId = result.Id, sequence = i.Sequence,
                          dueDate = i.DueDate.ToString("yyyy-MM-dd"), amount = i.Amount, paymentMethod = InstallmentMethod },
                    tx, cancellationToken: ct));

            await ImportStore.UpsertMapAsync(conn, tx, EntityType, legacyId, result.Id, row.Hash, ct);
            await tx.CommitAsync(ct);
            return result.Inserted ? RowOutcome.Inserted : RowOutcome.Updated;
        }
        catch (PostgresException ex)
            when (ex.SqlState == "23505" && ex.ConstraintName == "ix_requisitions_fixed_expense_unique" && row.Type == "FixedExpense")
        {
            await tx.RollbackAsync(ct);
            // The One allows a single active Conta Fixa per (vendor, unit, CC) (AEL-881), but the AELBRA has
            // legitimately MULTIPLE meters per unit from the same vendor (e.g. gas: refeitório vs laboratório,
            // same value). Bring the extra one in as ServiceProvision (same withholdings / Service payable) so it
            // still enters the One and its payments consume budget — it just isn't the primary Conta Fixa of that
            // trio. Product follow-up: the One's rule could distinguish meters instead of forbidding the second.
            return await UpsertRowAsync(conn, org, legacyId, row with { Type = "ServiceProvision" }, prior, payload, ct);
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

/// <summary>Mapped target values for one requisition; the cost center is resolved/created at write time, so
/// the hash keys on the natureza, not the resolved id. The first due date and issue date are the first
/// installment's due date (issue_date ≤ earliest due, per the domain).</summary>
public sealed record RequisitionRow(
    Guid CompanyId, Guid UnitId, Guid VendorId, string NatureName, int Code,
    string Type, string Description, string? Observation, decimal EstimateValue, string EstimateType,
    string StartCompetency, string? EndCompetency, string Status,
    string? PaymentFrequency, int? PaymentInstallmentsCount,
    IReadOnlyList<Installment> Installments,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt)
{
    private DateOnly FirstDueDate => Installments[0].DueDate;

    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        CompanyId, UnitId, VendorId, NatureKey.Of(NatureName), Code, Type, Description, Observation ?? "",
        EstimateValue.ToString(CultureInfo.InvariantCulture), EstimateType, StartCompetency, EndCompetency ?? "",
        Status, PaymentFrequency ?? "", PaymentInstallmentsCount?.ToString() ?? "",
        string.Join(',', Installments.Select(i => $"{i.Sequence}:{i.DueDate:yyyy-MM-dd}:{i.Amount.ToString(CultureInfo.InvariantCulture)}")),
        CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? ""));

    public object ToParams(Guid id, Guid costCenterId, string actor) => new
    {
        id,
        companyId = CompanyId,
        costCenterId,
        unitId = UnitId,
        vendorId = VendorId,
        code = Code,
        type = Type,
        description = Description,
        observation = Observation,
        estimateValue = EstimateValue,
        estimateType = EstimateType,
        startCompetency = StartCompetency,
        endCompetency = EndCompetency,
        status = Status,
        paymentFrequency = PaymentFrequency,
        paymentInstallmentsCount = PaymentInstallmentsCount,
        firstDueDate = FirstDueDate.ToString("yyyy-MM-dd"),
        issueDate = FirstDueDate.ToString("yyyy-MM-dd"),
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
