using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes `orcamentos` into `core.cost_center_unit_budgets`. Resolves the unit by code (units
/// are never auto-created — a missing unit is dead-lettered), then PROVISIONS the natureza under the
/// unit's company if it doesn't exist there yet (legacy naturezas are flat; the One's are company-scoped),
/// and ensures the `core.cost_center_units` coverage row for the pair — all so the budget resolves and
/// requisition approval on that (cost center, unit) pair works.</summary>
public sealed class BudgetWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "budget";
    private const string Actor = "firestore-migration";

    private const string UpsertSql = """
        insert into core.cost_center_unit_budgets (
            id, active, company_id, competency, cost_center_id, monthly_limit, unit_id,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, true, @companyId, @competency, @costCenterId, @monthlyLimit, @unitId,
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (cost_center_id, unit_id, competency) do update set
            monthly_limit = excluded.monthly_limit,
            active = true,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = @actor
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    private const string CoverageSql = """
        insert into core.cost_center_units (cost_center_id, unit_id)
        values (@costCenterId, @unitId)
        on conflict do nothing
        """;

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var d in source.ReadBudgetsAsync(ct))
        {
            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);

            // Units are org structure — never auto-created. A missing unit is a real blocker.
            if (!org.TryResolveUnit(d.UnitCode, out var unit))
            {
                await DeadLetterAsync(dryRun, d.Id, $"unidade '{d.UnitCode}' não encontrada no org-alvo", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var competency = Text.Trunc(d.Competency, 7);
            if (competency.Length != 7)
            {
                await DeadLetterAsync(dryRun, d.Id, $"competency inválida: '{d.Competency}'", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var row = new BudgetRow(unit.CompanyId, unit.UnitId, d.NatureName ?? "", competency, d.LimitValue ?? 0m, d.CreatedAt, d.UpdatedAt);
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

        return counters.ToSummary("budgets", dryRun);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, OrgResolver org, string legacyId, BudgetRow row, IdMapEntry prior, string payload, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Provision the natureza under the unit's company if absent (self-committed, independent of
            // the budget row's transaction), then write the budget + coverage against that cost center.
            // Inside the try so a provisioning failure dead-letters this row instead of aborting the batch.
            var costCenterId = await org.EnsureCostCenterAsync(row.CompanyId, row.NatureName, ct);

            var id = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            var result = await conn.QuerySingleAsync<UpsertResult>(
                new CommandDefinition(UpsertSql, row.ToParams(id, costCenterId, Actor), tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                CoverageSql, new { costCenterId, unitId = row.UnitId }, tx, cancellationToken: ct));
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

    private async Task DeadLetterAsync(bool dryRun, string legacyId, string error, string payload, CancellationToken ct)
    {
        if (!dryRun)
            await store.DeadLetterAsync(EntityType, legacyId, error, payload, ct);
    }

    private readonly record struct UpsertResult(Guid Id, bool Inserted);
}

/// <summary>Mapped target values for one budget cell. The cost center is resolved/created at write time,
/// so the idempotency hash keys on the natureza (normalized) rather than the resolved id.</summary>
public sealed record BudgetRow(
    Guid CompanyId, Guid UnitId, string NatureName, string Competency, decimal MonthlyLimit,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt)
{
    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        CompanyId, UnitId, NatureKey.Of(NatureName), Competency,
        MonthlyLimit.ToString(CultureInfo.InvariantCulture),
        CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? ""));

    public object ToParams(Guid id, Guid costCenterId, string actor) => new
    {
        id,
        companyId = CompanyId,
        costCenterId,
        unitId = UnitId,
        competency = Competency,
        monthlyLimit = MonthlyLimit,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
