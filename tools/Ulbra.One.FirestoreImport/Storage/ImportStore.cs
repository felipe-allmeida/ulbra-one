using Dapper;
using Npgsql;

namespace Ulbra.One.FirestoreImport.Storage;

/// <summary>The importer's own bookkeeping schema, kept separate from the app's EF schemas.
/// <c>id_map</c> makes every run idempotent (a legacy document always maps to the same target GUID,
/// and <c>content_hash</c> decides insert/update/skip); <c>dead_letter</c> records rows that could not
/// be resolved (e.g. a unit/natureza absent from the target org) without failing the batch.</summary>
public sealed class ImportStore(NpgsqlDataSource dataSource)
{
    public const string Schema = "orcamento_import";

    private const string UpsertMapSql = """
        insert into orcamento_import.id_map (entity_type, legacy_id, target_id, content_hash, imported_at)
        values (@entityType, @legacyId, @targetId, @contentHash, now())
        on conflict (entity_type, legacy_id)
        do update set target_id = excluded.target_id,
                      content_hash = excluded.content_hash,
                      imported_at = now()
        """;

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        const string sql = """
            create schema if not exists orcamento_import;

            create table if not exists orcamento_import.id_map (
                entity_type  text        not null,
                legacy_id    text        not null,
                target_id    uuid        not null,
                content_hash text        not null,
                imported_at  timestamptz not null default now(),
                primary key (entity_type, legacy_id)
            );

            create table if not exists orcamento_import.dead_letter (
                id          bigint generated always as identity primary key,
                entity_type text        not null,
                legacy_id   text        not null,
                error       text        not null,
                payload     jsonb,
                at          timestamptz not null default now()
            );

            create table if not exists orcamento_import.sync_state (
                collection  text        not null primary key,
                last_run_at timestamptz not null,
                updated_at  timestamptz not null default now()
            );
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    /// <summary>Pre-loads every mapping for an entity type into memory so per-row skip decisions
    /// (unchanged content_hash) need no round-trip during a run.</summary>
    public async Task<Dictionary<string, IdMapEntry>> LoadMapAsync(string entityType, CancellationToken ct)
    {
        const string sql = """
            select legacy_id as LegacyId, target_id as TargetId, content_hash as ContentHash
            from orcamento_import.id_map
            where entity_type = @entityType
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MapRow>(new CommandDefinition(sql, new { entityType }, cancellationToken: ct));
        return rows.ToDictionary(r => r.LegacyId, r => new IdMapEntry(r.TargetId, r.ContentHash));
    }

    /// <summary>Upserts a mapping inside the caller's transaction, so the id_map row commits atomically
    /// with the business row it points at.</summary>
    public static Task UpsertMapAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string entityType, string legacyId, Guid targetId, string contentHash, CancellationToken ct)
        => conn.ExecuteAsync(new CommandDefinition(
            UpsertMapSql, new { entityType, legacyId, targetId, contentHash }, tx, cancellationToken: ct));

    private const string UpsertMapBatchSql = """
        insert into orcamento_import.id_map (entity_type, legacy_id, target_id, content_hash, imported_at)
        select @entityType, u.legacy_id, u.target_id, u.content_hash, now()
        from unnest(@legacyIds, @targetIds, @contentHashes) as u(legacy_id, target_id, content_hash)
        on conflict (entity_type, legacy_id)
        do update set target_id = excluded.target_id, content_hash = excluded.content_hash, imported_at = now()
        """;

    /// <summary>Bulk variant of <see cref="UpsertMapAsync"/> — one statement for a whole chunk (parallel arrays).
    /// Uses a raw command so the arrays are passed as real Postgres arrays (Dapper would expand them).</summary>
    public static async Task UpsertMapBatchAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string entityType,
        string[] legacyIds, Guid[] targetIds, string[] contentHashes, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(UpsertMapBatchSql, conn, tx);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddWithValue("legacyIds", legacyIds);
        cmd.Parameters.AddWithValue("targetIds", targetIds);
        cmd.Parameters.AddWithValue("contentHashes", contentHashes);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The high-water mark of the last successful run for a collection (incremental read). Null when
    /// a collection has never been synced — the caller then reads it in full.</summary>
    public async Task<DateTimeOffset?> GetSyncCursorAsync(string collection, CancellationToken ct)
    {
        // Read as DateTime (Npgsql's native CLR type for timestamptz) and wrap as a UTC DateTimeOffset —
        // asking Dapper for DateTimeOffset directly throws ("Error parsing column 0 ... - DateTime").
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var value = await conn.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "select last_run_at from orcamento_import.sync_state where collection = @collection",
            new { collection }, cancellationToken: ct));
        return value is { } dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero) : null;
    }

    /// <summary>Advances a collection's incremental-read cursor. Pass the run's START time so docs updated
    /// during the run are re-read next time (idempotent), never missed.</summary>
    public async Task SetSyncCursorAsync(string collection, DateTimeOffset lastRunAt, CancellationToken ct)
    {
        const string sql = """
            insert into orcamento_import.sync_state (collection, last_run_at, updated_at)
            values (@collection, @lastRunAt, now())
            on conflict (collection) do update set last_run_at = excluded.last_run_at, updated_at = now()
            """;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { collection, lastRunAt }, cancellationToken: ct));
    }

    /// <summary>Reads dead-letter rows — all, or one <paramref name="entityType"/> — for CSV export/diagnosis.
    /// Note: <c>user_grant</c> rows are WARNINGS (the user WAS imported, but a unit/natureza scope could not be
    /// attributed), not failed inserts.</summary>
    public async Task<IReadOnlyList<DeadLetterRow>> GetDeadLettersAsync(string? entityType, CancellationToken ct)
    {
        const string sql = """
            select entity_type as EntityType, legacy_id as LegacyId, error as Error, at as At, payload::text as Payload
            from orcamento_import.dead_letter
            where (@entityType is null or entity_type = @entityType)
            order by entity_type, at desc
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DeadLetterRow>(new CommandDefinition(sql, new { entityType }, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>Dead-letter counts grouped by entity type — a quick "why 90 failed" breakdown.</summary>
    public async Task<IReadOnlyList<DeadLetterCount>> GetDeadLetterCountsAsync(CancellationToken ct)
    {
        const string sql = """
            select entity_type as EntityType, count(*) as Count
            from orcamento_import.dead_letter
            group by entity_type
            order by count(*) desc
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DeadLetterCount>(new CommandDefinition(sql, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>Clears the dead-letters of one entity type — called at the START of a (re)import so the table
    /// reflects the LATEST run instead of accumulating stale rows across runs (a fixed doc's old dead-letter
    /// would otherwise linger and look like a current failure).</summary>
    public async Task ClearDeadLettersAsync(string entityType, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from orcamento_import.dead_letter where entity_type = @entityType",
            new { entityType }, cancellationToken: ct));
    }

    /// <summary>Parks an unresolved/failed document for later inspection; never throws the batch.</summary>
    public async Task DeadLetterAsync(
        string entityType, string legacyId, string error, string? payloadJson, CancellationToken ct)
    {
        const string sql = """
            insert into orcamento_import.dead_letter (entity_type, legacy_id, error, payload)
            values (@entityType, @legacyId, @error, cast(@payloadJson as jsonb))
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { entityType, legacyId, error, payloadJson }, cancellationToken: ct));
    }

    private sealed record MapRow(string LegacyId, Guid TargetId, string ContentHash);
}

/// <summary>A row of <c>orcamento_import.id_map</c>.</summary>
public readonly record struct IdMapEntry(Guid TargetId, string ContentHash);

/// <summary>A dead-letter row (a document that could not be resolved/written, or a warning). <c>At</c> is a
/// DateTime — the timestamptz's native Npgsql CLR type; asking Dapper for DateTimeOffset here throws.</summary>
public sealed record DeadLetterRow(string EntityType, string LegacyId, string Error, DateTime At, string? Payload);

/// <summary>Dead-letter count for one entity type.</summary>
public sealed record DeadLetterCount(string EntityType, long Count);
