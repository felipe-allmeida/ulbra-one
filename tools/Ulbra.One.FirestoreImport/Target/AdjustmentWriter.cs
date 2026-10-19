using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes legacy `adequacoes` into `budgeting.budget_adjustments` (+ the `budget_adjustment_transition`
/// timeline) as HISTORY — it never mutates the budget ceiling (in the One the ceiling is changed by the
/// approve/reverse endpoints, not the DB; the imported `orcamentos` already carry the final value, and
/// BudgetControlService derives consumption from requisitions, not from these). A "Revisão Orçamentária" fans
/// out to one row per competency; the other types are a single row. The unit is resolved (missing →
/// dead-letter) and the natureza provisioned like budgets; the requester e-mail resolves to the One user for
/// created_by/updated_by and the transition actor (fallback: the migration/system actor).</summary>
public sealed class AdjustmentWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "budget_adjustment";
    private const string Actor = "firestore-migration";

    // Same tactic as RequisitionWriter: legacy seqNum is not unique and `code` is unique per company, so a
    // deterministic synthetic code (offset + stable GLOBAL rank over the fanned-out rows) keeps it unique AND
    // stable across runs, and never collides with the small seed codes.
    private const int CodeBase = 1_000_000;

    // The transition actor is uuid NOT NULL; when the requester e-mail does not resolve to a One user the
    // synthetic trail is attributed to the system actor (a cross-module ref, so any uuid is structurally valid).
    private static readonly Guid SystemActorId = Guid.Empty;

    private const string UpsertSql = """
        insert into budgeting.budget_adjustments (
            id, code, company_id, unit_id, cost_center_id, type, competency, source_competency,
            value, justification, counterparts, status, status_changed_at,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @code, @companyId, @unitId, @costCenterId, @type, @competency, @sourceCompetency,
            @value, @justification, @counterparts, @status, coalesce(@statusChangedAt, now()),
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (company_id, code) do update set
            unit_id = excluded.unit_id,
            cost_center_id = excluded.cost_center_id,
            type = excluded.type,
            competency = excluded.competency,
            source_competency = excluded.source_competency,
            value = excluded.value,
            justification = excluded.justification,
            counterparts = excluded.counterparts,
            status = excluded.status,
            status_changed_at = excluded.status_changed_at,
            created_at = excluded.created_at,
            created_by = excluded.created_by,
            updated_at = excluded.updated_at,
            updated_by = excluded.updated_by
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    private const string DeleteTransitionsSql =
        "delete from budgeting.budget_adjustment_transition where budget_adjustment_id = @adjId";

    private const string InsertTransitionSql = """
        insert into budgeting.budget_adjustment_transition (
            id, budget_adjustment_id, action, from_status, to_status, actor_id, occurred_at, reason)
        values (@id, @adjId, @action, @fromStatus, @toStatus, @actorId, coalesce(@occurredAt, now()), @reason)
        """;

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Buffer + fan out + stable GLOBAL rank so each row's synthetic code is deterministic across runs.
        var flat = new List<(AdjustmentDoc Doc, AdjustmentEntry Entry)>();
        await foreach (var d in source.ReadAdjustmentsAsync(ct))
            foreach (var entry in AdjustmentMapping.ResolveEntries(d))
                flat.Add((d, entry));
        flat.Sort(CompareForRank);

        for (var index = 0; index < flat.Count; index++)
        {
            var (d, entry) = flat[index];
            counters.CountRead();
            var payload = JsonSerializer.Serialize(d);
            var legacyId = $"{d.Id}#{entry.Competency}";

            if (!org.TryResolveUnit(d.UnitCode, out var unit))
            {
                if (!dryRun)
                    await store.DeadLetterAsync(EntityType, legacyId, $"unidade '{d.UnitCode}' não encontrada no org-alvo", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var row = BuildRow(d, entry, unit, CodeBase + index);
            priorMap.TryGetValue(legacyId, out var prior);
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

            counters.Count(await UpsertRowAsync(conn, org, legacyId, row, prior, payload, ct));
        }

        return counters.ToSummary("adjustments", dryRun);
    }

    // Deterministic order for the global code rank: legacy seqNum, then competency, then doc id as tiebreaker.
    private static int CompareForRank(
        (AdjustmentDoc Doc, AdjustmentEntry Entry) a, (AdjustmentDoc Doc, AdjustmentEntry Entry) b)
    {
        var bySeq = (a.Doc.SeqNum ?? long.MaxValue).CompareTo(b.Doc.SeqNum ?? long.MaxValue);
        if (bySeq != 0) return bySeq;
        var byComp = string.CompareOrdinal(a.Entry.Competency, b.Entry.Competency);
        return byComp != 0 ? byComp : string.CompareOrdinal(a.Doc.Id, b.Doc.Id);
    }

    private static AdjustmentRow BuildRow(AdjustmentDoc d, AdjustmentEntry entry, UnitRef unit, int code)
    {
        var status = AdjustmentMapping.MapStatus(d.Status);
        var statusChangedAt = status == "Cancelled" ? d.CancelledAt ?? d.UpdatedAt : d.UpdatedAt;
        var cancelReason = Text.Trunc(d.CancelReason, 1000) is { Length: > 0 } r ? r : null;
        var transitions = AdjustmentMapping.BuildTransitions(status, d.CreatedAt, d.UpdatedAt, d.CancelledAt, cancelReason);
        var requester = (d.RequesterEmail ?? string.Empty).Trim().ToLowerInvariant() is { Length: > 0 } e ? e : null;

        return new AdjustmentRow(
            unit.CompanyId, unit.UnitId, d.NatureName ?? string.Empty, code,
            entry.Type, Text.Trunc(entry.Competency, 7), entry.SourceCompetency, entry.Value,
            Text.Trunc(string.IsNullOrWhiteSpace(d.Justification) ? "Migrado" : d.Justification, 1000),
            string.IsNullOrWhiteSpace(d.Counterparts) ? null : Text.Trunc(d.Counterparts, 1000),
            status, requester, d.CreatedAt, d.UpdatedAt, statusChangedAt, transitions);
    }

    private async Task<RowOutcome> UpsertRowAsync(
        NpgsqlConnection conn, OrgResolver org, string legacyId, AdjustmentRow row, IdMapEntry prior, string payload, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var costCenterId = await org.EnsureCostCenterAsync(row.CompanyId, row.NatureName, ct);
            var resolved = org.TryResolveUserByEmail(row.RequesterEmail, out var userId);
            var actor = resolved ? userId.ToString() : Actor;
            var actorId = resolved ? userId : SystemActorId;

            var id = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            var result = await conn.QuerySingleAsync<UpsertResult>(
                new CommandDefinition(UpsertSql, row.ToParams(id, costCenterId, actor), tx, cancellationToken: ct));

            // The synthetic timeline mirrors the source each run: clear and re-insert.
            await conn.ExecuteAsync(new CommandDefinition(DeleteTransitionsSql, new { adjId = result.Id }, tx, cancellationToken: ct));
            foreach (var t in row.Transitions)
                await conn.ExecuteAsync(new CommandDefinition(InsertTransitionSql, new
                {
                    id = Guid.NewGuid(),
                    adjId = result.Id,
                    action = t.Action,
                    fromStatus = t.FromStatus,
                    toStatus = t.ToStatus,
                    actorId,
                    occurredAt = t.OccurredAt,
                    reason = t.Reason,
                }, tx, cancellationToken: ct));

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

/// <summary>Mapped target values for one fanned-out adequação row. The cost center and the requester (actor)
/// are resolved at write time, so the idempotency hash keys on the natureza (normalized) and the requester
/// e-mail — not the resolved GUIDs — and stays stable across environments.</summary>
public sealed record AdjustmentRow(
    Guid CompanyId, Guid UnitId, string NatureName, int Code,
    string Type, string Competency, string? SourceCompetency, decimal Value,
    string Justification, string? Counterparts, string Status, string? RequesterEmail,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? StatusChangedAt,
    IReadOnlyList<TransitionSpec> Transitions)
{
    public string Hash => Storage.Hashing.Sha256Hex(string.Join('|',
        CompanyId, UnitId, NatureKey.Of(NatureName), Code, Type, Competency, SourceCompetency ?? "",
        Value.ToString(CultureInfo.InvariantCulture), Status, Justification, Counterparts ?? "",
        RequesterEmail ?? "", CreatedAt?.ToString("O") ?? "", UpdatedAt?.ToString("O") ?? "",
        StatusChangedAt?.ToString("O") ?? "",
        string.Join(';', Transitions.Select(t =>
            $"{t.Action}:{t.FromStatus}->{t.ToStatus}@{t.OccurredAt?.ToString("O") ?? ""}:{t.Reason ?? ""}"))));

    public object ToParams(Guid id, Guid costCenterId, string actor) => new
    {
        id,
        code = Code,
        companyId = CompanyId,
        unitId = UnitId,
        costCenterId,
        type = Type,
        competency = Competency,
        sourceCompetency = SourceCompetency,
        value = Value,
        justification = Justification,
        counterparts = Counterparts,
        status = Status,
        statusChangedAt = StatusChangedAt,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        actor,
    };
}
