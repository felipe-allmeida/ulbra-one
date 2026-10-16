using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ulbra.One.FirestoreImport.Target;

namespace Ulbra.One.FirestoreImport;

/// <summary>Runs an import as a background job so the HTTP endpoint can return immediately (202 + a runId) and
/// the caller polls for the result — a full run reads tens of thousands of Firestore docs and takes minutes,
/// beyond any synchronous HTTP/ingress timeout. Single-writer: at most one job runs at a time (mirrors the
/// Container App's max_replicas = 1). State is in-memory: on the always-on migration replica (min_replicas = 1)
/// the job runs to completion; a rare replica restart loses the in-memory job history, but the import itself
/// is idempotent (id_map/content_hash) so it simply resumes on the next run — there is no cross-restart
/// checkpoint by design.</summary>
public sealed class ImportJobRunner(ImportRunner runner, ILogger<ImportJobRunner> logger)
{
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new();
    private int _running; // 0 = idle, 1 = a job is running (single-writer guard)

    /// <summary>Starts a job unless one is already running. Returns (started, job): when a job is already in
    /// flight, started is false and the running job is returned so the caller can point at it.</summary>
    public (bool Started, ImportJob Job) Start(ImportSelection selection, bool dryRun, bool full)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return (false, _jobs.Values.First(j => j.Status == ImportJobStatus.Running));

        var job = new ImportJob(Guid.NewGuid().ToString("N"), dryRun);
        _jobs[job.RunId] = job;
        _ = Task.Run(() => RunAsync(job, selection, dryRun, full));
        return (true, job);
    }

    public ImportJob? Get(string runId) => _jobs.TryGetValue(runId, out var job) ? job : null;

    private async Task RunAsync(ImportJob job, ImportSelection selection, bool dryRun, bool full)
    {
        try
        {
            logger.LogInformation("Import job {RunId} started (dryRun={DryRun}, full={Full})", job.RunId, dryRun, full);
            job.Result = await runner.ImportAsync(selection, dryRun, full, CancellationToken.None);
            job.Status = ImportJobStatus.Completed;
            logger.LogInformation("Import job {RunId} completed", job.RunId);
        }
        catch (Exception ex)
        {
            job.Status = ImportJobStatus.Failed;
            job.Error = ex.Message;
            logger.LogError(ex, "Import job {RunId} failed", job.RunId);
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _running, 0);
        }
    }
}

public enum ImportJobStatus { Running, Completed, Failed }

/// <summary>A tracked background import: status + result/error, polled via the status endpoint.</summary>
public sealed class ImportJob(string runId, bool dryRun)
{
    public string RunId { get; } = runId;
    public bool DryRun { get; } = dryRun;
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Running;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public ImportResult? Result { get; set; }
    public string? Error { get; set; }
}
