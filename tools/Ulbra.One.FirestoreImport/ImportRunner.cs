using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;
using Ulbra.One.FirestoreImport.Target;

namespace Ulbra.One.FirestoreImport;

/// <summary>Orchestrates a run: ensures the bookkeeping schema, (re)loads the target org snapshot, and
/// drives preflight or one collection's import. The org snapshot is loaded per call so it always
/// reflects the target environment the job is pointed at.</summary>
public sealed class ImportRunner(
    FirestoreSource source,
    ImportStore store,
    NpgsqlDataSource dataSource,
    VendorWriter vendors,
    UserWriter users,
    BudgetWriter budgets,
    RequisitionWriter requisitions,
    BankAccountWriter bankAccounts,
    PayableWriter payables,
    PayableFileWriter payableFiles,
    ProjectWriter projects,
    AdjustmentWriter adjustments,
    ProjectLinkWriter projectLinks)
{
    public async Task<PreflightReport> PreflightAsync(CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await new PreflightService(source, org).RunAsync(ct);
    }

    /// <summary>Fast synchronous preconditions for the import endpoint: Postgres reachable (+ bookkeeping
    /// schema) and Firestore reachable/authed. Throws on failure so the endpoint returns a real error instead
    /// of a 202 that would only fail later in the background.</summary>
    public async Task ValidatePreconditionsAsync(CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        await source.PingAsync(ct);
    }

    /// <summary>The dead-letters (all, or one entity type) for CSV export/diagnosis.</summary>
    public async Task<IReadOnlyList<DeadLetterRow>> GetDeadLettersAsync(string? entityType, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        return await store.GetDeadLettersAsync(entityType, ct);
    }

    /// <summary>Dead-letter counts grouped by entity type.</summary>
    public async Task<IReadOnlyList<DeadLetterCount>> GetDeadLetterCountsAsync(CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        return await store.GetDeadLetterCountsAsync(ct);
    }

    public async Task<ImportSummary> ImportVendorsAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        return await vendors.ImportAsync(source, dryRun, ct);
    }

    public async Task<ImportSummary> ImportUsersAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await users.ImportAsync(source, org, dryRun, ct);
    }

    public async Task<ImportSummary> ImportBudgetsAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await budgets.ImportAsync(source, org, dryRun, ct);
    }

    /// <summary>Imports legacy requisitions (the Firestore `requisicoes` collection) into
    /// `budgeting.requisitions` (+ installments, + realized payments) so the budget consumption
    /// (realizado + empenhado) is derived by the app. <paramref name="stages"/> selects which One statuses
    /// to bring — paid, committed or queued — so they can be migrated on demand and re-run idempotently.
    /// Run AFTER the org import (it references the vendors/units/naturezas it brought in). No Payables are
    /// created (direct write, no outbox).</summary>
    public async Task<ImportSummary> ImportRequisitionsAsync(
        IReadOnlySet<string> stages, string label, bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await requisitions.ImportAsync(source, org, stages, label, dryRun, ct);
    }

    /// <summary>Imports legacy `projetos` (projects with their own spend ceiling) into `budgeting.projects`.
    /// Metadata only (no cost center, no attachments). Run AFTER the org import (resolves the unit).</summary>
    public async Task<ImportSummary> ImportProjectsAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await projects.ImportAsync(source, org, dryRun, ct);
    }

    /// <summary>Imports legacy `adequacoes` (budget-ceiling adjustments) into `budgeting.budget_adjustments`
    /// (+ the transition timeline) as HISTORY — never mutating the ceiling. Run AFTER users (the requester
    /// e-mail resolves to the One user for created_by/updated_by and the transition actor).</summary>
    public async Task<ImportSummary> ImportAdjustmentsAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await adjustments.ImportAsync(source, org, dryRun, ct);
    }

    /// <summary>Backfills `budgeting.requisitions.project_id` for the requisitions that carry a legacy
    /// `projectId`. Run LAST — it resolves both the requisition and the project through the id-map.</summary>
    public async Task<ImportSummary> ImportProjectLinksAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        return await projectLinks.ImportAsync(source, dryRun, ct);
    }

    /// <summary>Imports payer bank accounts (`santander_config` → `finance.bank_accounts`). Run before
    /// payables (their baixas resolve the payer account through the remittance chain).</summary>
    public async Task<ImportSummary> ImportBankAccountsAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await bankAccounts.ImportAsync(source, org, dryRun, ct);
    }

    /// <summary>Imports the legacy `pagamentos` (workflow only) into Finance Payables + installments + baixas,
    /// and mirrors the realized amount into `budgeting.requisition_payment`. Run AFTER requisitions (needs the
    /// requisition id-map) and bank-accounts (needs the payer accounts).</summary>
    public async Task<ImportSummary> ImportPayablesAsync(bool full, bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        var org = await OrgResolver.LoadAsync(dataSource, ct);
        return await payables.ImportAsync(source, org, full, dryRun, ct);
    }

    /// <summary>Migrates each payable's legacy PDFs (NF/boleto/comprovante) from Firebase Storage into the
    /// One's blob + `finance.fiscal_documents`/`payable_attachments`. Run AFTER payables. Slow (downloads +
    /// uploads each file); idempotent by a per-payable file hash.</summary>
    public async Task<ImportSummary> ImportPayableFilesAsync(bool dryRun, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        return await payableFiles.ImportAsync(source, dryRun, ct);
    }

    /// <summary>Runs exactly the items flagged in <paramref name="selection"/>, SEQUENTIALLY and in
    /// dependency order regardless of flag order: vendors → budgets (provisiona naturezas) → users
    /// (referencia as naturezas) → projects → adjustments (referencia users) → bank accounts →
    /// requisitions (paid → committed → queued) → payables → payable files → project links (backfill, needs
    /// os id-maps de requisition e project). Each step reloads the org snapshot, so later steps see the
    /// naturezas an earlier step just created. Migração sob-demanda: re-running is idempotent — a doc that is
    /// unchanged is skipped, a new one is inserted, and one that moved on (e.g. Em Pagamento → PAGO) is
    /// updated in place when its stage flag runs.</summary>
    public async Task<ImportResult> ImportAsync(ImportSelection selection, bool dryRun, bool full, CancellationToken ct)
    {
        // Reset the dead-letters of the entities about to run, so the table reflects THIS run (they accumulate
        // otherwise — a doc fixed since the last run would keep its stale dead-letter and look like a failure).
        if (!dryRun) await ResetDeadLettersAsync(selection, ct);

        var vendorsResult = selection.Vendors ? await ImportVendorsAsync(dryRun, ct) : null;
        var budgetsResult = selection.Budgets ? await ImportBudgetsAsync(dryRun, ct) : null;
        var usersResult = selection.Users ? await ImportUsersAsync(dryRun, ct) : null;
        var projectsResult = selection.Projects ? await ImportProjectsAsync(dryRun, ct) : null;
        var adjustmentsResult = selection.Adjustments ? await ImportAdjustmentsAsync(dryRun, ct) : null;
        var bankResult = selection.BankAccounts ? await ImportBankAccountsAsync(dryRun, ct) : null;

        var paid = selection.RequisitionsPaid
            ? await ImportRequisitionsAsync(RequisitionMapping.StagePaid, "requisitions-paid", dryRun, ct) : null;
        var inPayment = selection.RequisitionsInPayment
            ? await ImportRequisitionsAsync(RequisitionMapping.StageInPayment, "requisitions-in-payment", dryRun, ct) : null;
        var inApproval = selection.RequisitionsInApproval
            ? await ImportRequisitionsAsync(RequisitionMapping.StageApproval, "requisitions-in-approval", dryRun, ct) : null;
        var cancelled = selection.RequisitionsCancelled
            ? await ImportRequisitionsAsync(RequisitionMapping.StageCancelled, "requisitions-cancelled", dryRun, ct) : null;

        var payablesResult = selection.Payables ? await ImportPayablesAsync(full, dryRun, ct) : null;
        var filesResult = selection.PayableFiles ? await ImportPayableFilesAsync(dryRun, ct) : null;
        var projectLinksResult = selection.ProjectLinks ? await ImportProjectLinksAsync(dryRun, ct) : null;

        return new ImportResult(dryRun, vendorsResult, budgetsResult, usersResult, projectsResult,
            adjustmentsResult, bankResult, paid, inPayment, inApproval, cancelled, payablesResult, filesResult,
            projectLinksResult);
    }

    // Clears the dead-letters of exactly the entities this run will (re)import, once, before it starts.
    private async Task ResetDeadLettersAsync(ImportSelection s, CancellationToken ct)
    {
        await store.EnsureSchemaAsync(ct);
        if (s.Vendors) await store.ClearDeadLettersAsync(VendorWriter.EntityType, ct);
        if (s.Users)
        {
            await store.ClearDeadLettersAsync(UserWriter.EntityType, ct);
            await store.ClearDeadLettersAsync("user_grant", ct);
        }
        if (s.Budgets) await store.ClearDeadLettersAsync(BudgetWriter.EntityType, ct);
        if (s.BankAccounts) await store.ClearDeadLettersAsync(BankAccountWriter.EntityType, ct);
        if (s.Projects) await store.ClearDeadLettersAsync(ProjectWriter.EntityType, ct);
        if (s.Adjustments) await store.ClearDeadLettersAsync(AdjustmentWriter.EntityType, ct);
        if (s.RequisitionsPaid || s.RequisitionsInPayment || s.RequisitionsInApproval || s.RequisitionsCancelled)
            await store.ClearDeadLettersAsync(RequisitionWriter.EntityType, ct);
        if (s.Payables) await store.ClearDeadLettersAsync(PayableWriter.EntityType, ct);
        if (s.PayableFiles) await store.ClearDeadLettersAsync(PayableFileWriter.EntityType, ct);
    }
}

/// <summary>Which items a single import call should migrate. Every flag defaults to false (nothing runs),
/// so the caller opts in per item; the requisition flags select budget-consumption stages. Dependency order
/// is enforced internally: vendors → budgets → users → bank accounts → requisitions → payables.</summary>
public sealed record ImportSelection(
    bool Vendors = false,
    bool Users = false,
    bool Budgets = false,
    bool BankAccounts = false,
    bool RequisitionsPaid = false,
    bool RequisitionsInPayment = false,
    bool RequisitionsInApproval = false,
    bool RequisitionsCancelled = false,
    bool Payables = false,
    bool PayableFiles = false,
    bool Projects = false,
    bool Adjustments = false,
    bool ProjectLinks = false);

/// <summary>Result of a single import call — one summary per requested item (null when its flag was off).</summary>
public sealed record ImportResult(
    bool DryRun,
    ImportSummary? Vendors,
    ImportSummary? Budgets,
    ImportSummary? Users,
    ImportSummary? Projects,
    ImportSummary? Adjustments,
    ImportSummary? BankAccounts,
    ImportSummary? RequisitionsPaid,
    ImportSummary? RequisitionsInPayment,
    ImportSummary? RequisitionsInApproval,
    ImportSummary? RequisitionsCancelled,
    ImportSummary? Payables,
    ImportSummary? PayableFiles,
    ImportSummary? ProjectLinks)
{
    /// <summary>The per-item summaries that actually ran (non-null), in dependency order — for rendering.</summary>
    public IEnumerable<ImportSummary> Items()
    {
        foreach (var s in new[]
        {
            Vendors, Budgets, Users, Projects, Adjustments, BankAccounts,
            RequisitionsPaid, RequisitionsInPayment, RequisitionsInApproval, RequisitionsCancelled,
            Payables, PayableFiles, ProjectLinks,
        })
            if (s is not null) yield return s;
    }
}
