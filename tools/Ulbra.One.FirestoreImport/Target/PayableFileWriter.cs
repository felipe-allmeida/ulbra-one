using System.Text;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Migrates each payable's legacy PDFs (Nota Fiscal, boleto, comprovante) from Firebase Storage into
/// the One's blob container and records them as <c>finance.fiscal_documents</c> (one per payable) +
/// <c>finance.payable_attachments</c>. Runs AFTER payables (looks the payable up by the requisition id-map).
/// Idempotent: a per-payable hash of the source file paths skips unchanged groups without re-downloading;
/// blob keys are deterministic so a re-migrate overwrites in place. Blob I/O happens outside the DB
/// transaction (only the row writes are transactional).</summary>
public sealed class PayableFileWriter(NpgsqlDataSource dataSource, ImportStore store, BlobMigrator blobs)
{
    public const string EntityType = "payable_files";
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-0000000005ed");

    private const string UpsertFiscalSql = """
        insert into finance.fiscal_documents (
            id, payable_id, format, raw_extraction_json, file_name, content_type, storage_path, size_bytes,
            uploaded_by_id, created_at, cfops, ncms)
        values (@id, @payableId, 2, '{}'::jsonb, @fileName, @contentType, @storagePath, @size,
            @actorId, now(), '[]'::jsonb, '[]'::jsonb)
        on conflict (payable_id) do update set
            file_name = excluded.file_name,
            content_type = excluded.content_type,
            storage_path = excluded.storage_path,
            size_bytes = excluded.size_bytes
        """;

    private const string DeleteAttachmentsSql = "delete from finance.payable_attachments where payable_id = @payableId";

    private const string InsertAttachmentSql = """
        insert into finance.payable_attachments (
            id, payable_id, file_name, content_type, size_bytes, storage_path, uploaded_by_id, kind, created_at)
        values (@id, @payableId, @fileName, @contentType, @size, @storagePath, @actorId, @kind, now())
        """;

    public async Task<ImportSummary> ImportAsync(FirestoreSource source, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        var payableByReq = await store.LoadMapAsync(PayableWriter.EntityType, ct);
        if (!dryRun) await blobs.EnsureContainerAsync(ct);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var companyByPayable = (await conn.QueryAsync<(Guid Id, Guid CompanyId)>(new CommandDefinition(
            "select id, company_id from finance.payables", cancellationToken: ct)))
            .ToDictionary(r => r.Id, r => r.CompanyId);

        // Group the workflow payments by requisition and collect their files.
        var groups = new Dictionary<string, List<PaymentDoc>>(StringComparer.Ordinal);
        await foreach (var p in source.ReadPaymentsAsync(ct))
        {
            if (p.IsExternal == true) continue;
            var reqId = (p.RequisitionId ?? string.Empty).Trim();
            if (reqId.Length == 0 || reqId.StartsWith("EXT_REQ_IMP_", StringComparison.Ordinal)) continue;
            (groups.TryGetValue(reqId, out var list) ? list : groups[reqId] = []).Add(p);
        }

        foreach (var reqId in groups.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!payableByReq.TryGetValue(reqId, out var payable) || payable.TargetId == default)
                continue; // payable not imported for this requisition — nothing to attach to

            var files = CollectFiles(groups[reqId]);
            if (files.Count == 0) continue;

            counters.CountRead();
            var hash = HashOf(files);
            priorMap.TryGetValue(reqId, out var prior);
            if (prior.ContentHash == hash)
            {
                counters.Count(RowOutcome.Skipped);
                continue;
            }

            if (dryRun)
            {
                counters.Count(prior.TargetId == default ? RowOutcome.Inserted : RowOutcome.Updated);
                continue;
            }

            var companyId = companyByPayable.TryGetValue(payable.TargetId, out var c) ? c : Guid.Empty;
            counters.Count(await MigrateGroupAsync(conn, reqId, payable.TargetId, companyId, files, hash, ct));
        }

        return counters.ToSummary("payable-files", dryRun);
    }

    private async Task<RowOutcome> MigrateGroupAsync(
        NpgsqlConnection conn, string reqId, Guid payableId, Guid companyId, List<FileRef> files, string hash, CancellationToken ct)
    {
        try
        {
            // 1) Migrate blobs (network) OUTSIDE the transaction.
            var fiscalPrefix = $"fiscal/{companyId:N}/";
            StoredBlob? fiscal = null;
            string? fiscalName = null;
            var attachments = new List<(FileRef File, StoredBlob Blob)>();
            foreach (var f in files)
            {
                var prefix = f.Kind == "FiscalDocument" ? fiscalPrefix : string.Empty;
                var stored = await blobs.MigrateAsync(f.Url, f.Name, prefix, ct);
                if (stored is null) continue; // legacy object missing — skip that file
                attachments.Add((f, stored.Value));
                if (f.Kind == "FiscalDocument" && fiscal is null) { fiscal = stored; fiscalName = f.Name; }
            }

            if (attachments.Count == 0)
            {
                await store.DeadLetterAsync(EntityType, reqId, "nenhum arquivo legado encontrado no storage", "{}", ct);
                return RowOutcome.DeadLettered;
            }

            // 2) Write rows in a short transaction.
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (fiscal is { } fd)
                await conn.ExecuteAsync(new CommandDefinition(UpsertFiscalSql, new
                {
                    id = Guid.NewGuid(), payableId, fileName = Trunc(fiscalName!, 260),
                    contentType = ContentTypeOf(fiscalName!), storagePath = fd.Key, size = fd.Size, actorId = SystemActorId,
                }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(DeleteAttachmentsSql, new { payableId }, tx, cancellationToken: ct));
            foreach (var (file, blob) in attachments)
                await conn.ExecuteAsync(new CommandDefinition(InsertAttachmentSql, new
                {
                    id = Guid.NewGuid(), payableId, fileName = Trunc(file.Name, 260), contentType = ContentTypeOf(file.Name),
                    size = blob.Size, storagePath = blob.Key, actorId = SystemActorId, kind = file.Kind,
                }, tx, cancellationToken: ct));

            await ImportStore.UpsertMapAsync(conn, tx, EntityType, reqId, payableId, hash, ct);
            await tx.CommitAsync(ct);
            return RowOutcome.Inserted; // "inserted" == migrated (insert/update is not distinguished for files)
        }
        catch (Exception ex) when (ex is PostgresException or Azure.RequestFailedException or HttpRequestException)
        {
            await store.DeadLetterAsync(EntityType, reqId, ex.Message, "{}", ct);
            return RowOutcome.DeadLettered;
        }
    }

    // One fiscal document + the distinct attachments (fiscal/boleto/receipt) of a requisition's payments.
    private static List<FileRef> CollectFiles(List<PaymentDoc> payments)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<FileRef>();
        void Add(string? url, string? name, string kind)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url)) return;
            files.Add(new FileRef(url.Trim(), string.IsNullOrWhiteSpace(name) ? DefaultName(url, kind) : name!, kind));
        }
        foreach (var p in payments)
        {
            Add(p.FiscalDocumentUrl, p.FiscalDocumentName, "FiscalDocument");
            Add(p.BoletoDocumentUrl, p.BoletoDocumentName, "BankSlip");
            Add(p.ReceiptUrl, p.ReceiptName, "Other");
        }
        return files;
    }

    private static string HashOf(List<FileRef> files)
        => Hashing.Sha256Hex(string.Join('|', files.OrderBy(f => f.Url, StringComparer.Ordinal).Select(f => $"{f.Kind}:{f.Url}")));

    private static string DefaultName(string url, string kind) => $"{kind}_{Path.GetFileName(url)}";

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".xml" => "application/xml",
        _ => "application/octet-stream",
    };

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>A legacy file to migrate: its storage object path, display name, and the One attachment kind.</summary>
public readonly record struct FileRef(string Url, string Name, string Kind);
