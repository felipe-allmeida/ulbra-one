using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Backfills `budgeting.requisitions.project_id` for the legacy requisitions that carry a `projectId`
/// (the requisition importer ignores that field). A post-pass: resolves both the requisition and the project
/// through the id-map, then links them. Run AFTER requisitions and projects. A requisition or project not in
/// the id-map (its stage never ran / it was dead-lettered) is skipped and logged, never dead-lettered.</summary>
public sealed class ProjectLinkWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    // `is distinct from` makes it idempotent: a row already linked to this project is a no-op (0 rows).
    private const string UpdateSql = """
        update budgeting.requisitions set project_id = @projectId
        where id = @requisitionId and project_id is distinct from @projectId
        """;

    public async Task<ImportSummary> ImportAsync(FirestoreSource source, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var reqMap = await store.LoadMapAsync(RequisitionWriter.EntityType, ct);
        var projMap = await store.LoadMapAsync(ProjectWriter.EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var link in source.ReadRequisitionProjectLinksAsync(ct))
        {
            counters.CountRead();

            if (!reqMap.TryGetValue(link.RequisitionId, out var req) || req.TargetId == default
                || !projMap.TryGetValue(link.ProjectId, out var proj) || proj.TargetId == default)
            {
                counters.Count(RowOutcome.Skipped); // requisition or project not imported — link deferred
                continue;
            }

            if (dryRun)
            {
                counters.Count(RowOutcome.Updated);
                continue;
            }

            var n = await conn.ExecuteAsync(new CommandDefinition(
                UpdateSql, new { projectId = proj.TargetId, requisitionId = req.TargetId }, cancellationToken: ct));
            counters.Count(n > 0 ? RowOutcome.Updated : RowOutcome.Skipped);
        }

        return counters.ToSummary("project-links", dryRun);
    }
}
