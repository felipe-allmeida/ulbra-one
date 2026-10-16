using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes legacy `projetos` into `budgeting.projects` (metadata only — the One's Project has no cost
/// center and no attachments). The unit is resolved (missing → dead-letter); there is no requester, so the
/// audit columns are the migration actor. Upsert by the natural key <c>(company_id, name)</c>.</summary>
public sealed class ProjectWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "project";
    private const string Actor = "firestore-migration";

    private const string UpsertSql = """
        insert into budgeting.projects (
            id, company_id, unit_id, name, description, budget_limit, initial_consumed_value, status,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @companyId, @unitId, @name, @description, @budgetLimit, @initialConsumedValue, @status,
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (company_id, name) do update set
            unit_id = excluded.unit_id,
            description = excluded.description,
            budget_limit = excluded.budget_limit,
            initial_consumed_value = excluded.initial_consumed_value,
            status = excluded.status,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = excluded.updated_by
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var d in source.ReadProjectsAsync(ct))
        {
            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);

            if (!org.TryResolveUnit(d.UnitCode, out var unit))
            {
                if (!dryRun)
                    await store.DeadLetterAsync(EntityType, d.Id, $"unidade '{d.UnitCode}' não encontrada no org-alvo", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var name = Text.Trunc(d.Name, 200);
            if (name.Length == 0)
            {
                if (!dryRun) await store.DeadLetterAsync(EntityType, d.Id, "projeto sem nome", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var row = new ProjectRow(
                unit.CompanyId, unit.UnitId, name,
                string.IsNullOrWhiteSpace(d.Description) ? null : Text.Trunc(d.Description, 2000),
                d.BudgetLimit ?? 0m, d.InitialConsumedValue ?? 0m,
                ProjectMapping.MapStatus(d.Status), d.CreatedAt, d.UpdatedAt);

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

        return counters.ToSummary("projects", dryRun);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, string legacyId, ProjectRow row, IdMapEntry prior, string payload, CancellationToken ct)
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

/// <summary>Mapped target values for one project.</summary>
public sealed record ProjectRow(
    Guid CompanyId, Guid UnitId, string Name, string? Description,
    decimal BudgetLimit, decimal InitialConsumedValue, string Status,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt)
{
    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        CompanyId, UnitId, Name, Description ?? "",
        BudgetLimit.ToString(CultureInfo.InvariantCulture), InitialConsumedValue.ToString(CultureInfo.InvariantCulture),
        Status, CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? ""));

    public object ToParams(Guid id, string actor) => new
    {
        id,
        companyId = CompanyId,
        unitId = UnitId,
        name = Name,
        description = Description,
        budgetLimit = BudgetLimit,
        initialConsumedValue = InitialConsumedValue,
        status = Status,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
