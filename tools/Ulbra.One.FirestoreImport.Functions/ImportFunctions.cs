using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Ulbra.One.FirestoreImport;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Functions;

/// <summary>The migration endpoints. A full run reads tens of thousands of Firestore docs and takes minutes —
/// beyond any synchronous HTTP/ingress timeout — so <c>POST /api/import</c> starts the import in the
/// BACKGROUND and returns <c>202</c> + a status URL immediately; the caller polls
/// <c>GET /api/import/status/{runId}</c>. Single-writer (one run at a time). The read-only <c>preflight</c>
/// stays synchronous (it is quick). Auth level is Function (a function key in Azure; anonymous locally).</summary>
public sealed class ImportFunctions(ImportRunner runner, ImportJobRunner jobs)
{
    /// <summary>Measures org coverage in the TARGET environment (missing units, naturezas to provision). Never writes.</summary>
    [Function("preflight")]
    public async Task<IResult> Preflight(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "import/preflight")] HttpRequest req,
        CancellationToken ct)
        => Results.Ok(await runner.PreflightAsync(ct));

    /// <summary>Starter: reads the flags, kicks off the background import, returns 202 + the status URL. If a
    /// run is already in flight, returns 409 with that run's status URL (single-writer).</summary>
    [Function("import")]
    public async Task<IResult> Import(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "import")] HttpRequest req,
        CancellationToken ct)
    {
        var body = req.HasJsonContentType()
            ? await req.ReadFromJsonAsync<ImportRequestBody>(ct) ?? new ImportRequestBody()
            : new ImportRequestBody();

        // Fail FAST (before the 202) on obvious setup errors — Postgres/Firestore unreachable or bad credential —
        // instead of returning Accepted for a job that would only fail later in the background.
        try
        {
            await runner.ValidatePreconditionsAsync(ct);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Import não iniciado — pré-condições não atendidas",
                detail: $"Postgres/Firestore inacessível ou credencial inválida: {ex.Message}",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var (started, job) = jobs.Start(body.ToSelection(), body.DryRun ?? false, body.Full ?? false);
        var statusUrl = $"{req.Scheme}://{req.Host}{req.PathBase}/api/import/status/{job.RunId}";

        return started
            ? Results.Accepted(statusUrl, new { runId = job.RunId, status = "Running", statusUrl })
            : Results.Conflict(new { error = "já existe uma importação em andamento", runId = job.RunId, statusUrl });
    }

    /// <summary>Downloads the dead-letters as a CSV file (a document that could not be resolved/written, or a
    /// <c>user_grant</c> warning). Optional <c>?entityType=payable</c> filter. Columns: entity_type, legacy_id,
    /// at, error, payload — the <c>error</c> column says WHY each one failed.</summary>
    [Function("dead-letters")]
    public async Task<IResult> DeadLetters(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "import/dead-letters")] HttpRequest req,
        CancellationToken ct)
    {
        var entityType = req.Query["entityType"].ToString() is { Length: > 0 } et ? et : null;
        var rows = await runner.GetDeadLettersAsync(entityType, ct);
        var name = entityType is null ? "dead-letters.csv" : $"dead-letters-{entityType}.csv";
        // Prepend a UTF-8 BOM (EF BB BF) so Excel renders the pt-BR accents in error/payload correctly.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(DeadLetterCsv.Build(rows))).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", name);
    }

    /// <summary>Dead-letter counts grouped by entity type — the quick "why did 90 fail" breakdown (JSON).</summary>
    [Function("dead-letters-summary")]
    public async Task<IResult> DeadLettersSummary(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "import/dead-letters/summary")] HttpRequest req,
        CancellationToken ct)
        => Results.Ok(await runner.GetDeadLetterCountsAsync(ct));

    /// <summary>Poll the background import: Running/Completed/Failed + the per-item summary once done. Content
    /// negotiates — a browser (Accept: text/html) gets a formatted page that auto-refreshes while running;
    /// curl (or any non-HTML client) gets the JSON, unchanged.</summary>
    [Function("import-status")]
    public IResult Status(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "import/status/{runId}")] HttpRequest req,
        string runId)
    {
        var wantsHtml = req.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        var job = jobs.Get(runId);
        if (job is null)
            return wantsHtml
                ? Results.Content($"<!doctype html><meta charset=\"utf-8\"><p style=\"font:15px system-ui;margin:40px\">Run <code>{WebUtility.HtmlEncode(runId)}</code> não encontrado.</p>", "text/html; charset=utf-8", null, 404)
                : Results.NotFound(new { runId, error = "run não encontrado" });

        if (wantsHtml)
            return Results.Content(StatusPage.Render(job), "text/html; charset=utf-8");

        return Results.Ok(new
        {
            runId = job.RunId,
            status = job.Status.ToString(),
            dryRun = job.DryRun,
            startedAt = job.StartedAt,
            finishedAt = job.FinishedAt,
            result = job.Result,
            error = job.Error,
        });
    }
}

/// <summary>The import request body: one nullable bool per item (absent = false) plus a global <c>dryRun</c>
/// and <c>full</c> (force a complete Firestore read of `payables`, ignoring the incremental cursor — use it to
/// re-sync from scratch or catch legacy deletions; the default is incremental after the first run).
/// Requisition flags select budget-consumption stages — paid (realizado), in payment (empenhado), in approval
/// (previsto). <c>payables</c> brings the Finance Contas a Pagar (from `pagamentos`) and the real realizado;
/// <c>payableFiles</c> migrates the PDFs. Run them after the requisition flags and <c>bankAccounts</c>.
/// <c>projects</c> and <c>adjustments</c> bring the budget projects and the ceiling-adjustment history;
/// <c>projectLinks</c> backfills requisitions' project_id (run it after both requisitions and projects).</summary>
public sealed record ImportRequestBody(
    bool? DryRun = null,
    bool? Full = null,
    bool? Vendors = null,
    bool? Users = null,
    bool? Budgets = null,
    bool? BankAccounts = null,
    bool? Requisitions = null,
    bool? RequisitionsPaid = null,
    bool? RequisitionsInPayment = null,
    bool? RequisitionsInApproval = null,
    bool? RequisitionsCancelled = null,
    bool? Payables = null,
    bool? PayableFiles = null,
    bool? Projects = null,
    bool? Adjustments = null,
    bool? ProjectLinks = null)
{
    // `requisitions: true` is the fool-proof convenience: it brings ALL requisition statuses (the 4 stages),
    // so a payable never fails "requisição não importada" for a status the caller forgot to flag.
    public ImportSelection ToSelection()
    {
        var allReqs = Requisitions ?? false;
        return new(
        Vendors: Vendors ?? false,
        Users: Users ?? false,
        Budgets: Budgets ?? false,
        BankAccounts: BankAccounts ?? false,
        RequisitionsPaid: allReqs || (RequisitionsPaid ?? false),
        RequisitionsInPayment: allReqs || (RequisitionsInPayment ?? false),
        RequisitionsInApproval: allReqs || (RequisitionsInApproval ?? false),
        RequisitionsCancelled: allReqs || (RequisitionsCancelled ?? false),
        Payables: Payables ?? false,
        PayableFiles: PayableFiles ?? false,
        Projects: Projects ?? false,
        Adjustments: Adjustments ?? false,
        ProjectLinks: ProjectLinks ?? false);
    }
}
