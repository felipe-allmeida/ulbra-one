using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ListPayables;

/// <summary>Query-string filters for the Contas a Pagar list. Bound via <c>[AsParameters]</c>.</summary>
// NÃO herda PaginationQueryParameters: estes filtros são compartilhados com
// GetPaymentQueueSummary, que agrega o conjunto inteiro e não pagina — herdar faria o
// summary anunciar page/pageSize que ele ignora.
public sealed record ListPayablesRequest
{
    [SwaggerSchema("Company that owes the payables. Omit for the consolidated view across every company, "
        + "which only a consolidated role may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Lifecycle stage to keep.")]
    public PayableStatus? Status { get; set; }

    [SwaggerSchema("Vendor to be paid.")]
    public VendorId? VendorId { get; set; }

    [SwaggerSchema("Cost center the expense is charged to.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Unit (estabelecimento) tagged on the payable.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Expense category.")]
    public ExpenseCategoryId? CategoryId { get; set; }

    [SwaggerSchema("Keep installments due on or after this day. Rows are per INSTALLMENT here, so these "
        + "bounds filter due dates, not payables.")]
    [SwaggerExample("2026-09-01")]
    public DateOnly? DueFrom { get; set; }

    [SwaggerSchema("Keep installments due on or before this day.")]
    [SwaggerExample("2026-09-30")]
    public DateOnly? DueTo { get; set; }

    [SwaggerSchema("Keep only settled or only outstanding installments.")]
    public bool? Paid { get; set; }

    [SwaggerSchema("Keep only payables that already have an expense ledger account, or only those still "
        + "missing one — the unclassified queue.")]
    public bool? Classified { get; set; }

    /// <summary>Accounting competency ("YYYY-MM"): keeps payables whose <c>IssueDate</c> falls in that month.
    /// Payables without an IssueDate (no accounting competency yet) are excluded.</summary>
    [SwaggerSchema("Accounting competency (`YYYY-MM`), matched against the issue date. A payable with no "
        + "issue date has no competency yet and is EXCLUDED, not kept.")]
    [SwaggerExample("2026-08")]
    public string? Competency { get; set; }

    /// <summary>Matches counterparty name / document number (case-insensitive) and vendor CNPJ/CPF (digits).</summary>
    [SwaggerSchema("Free-text search over counterparty name, document number and vendor CNPJ/CPF.")]
    [SwaggerExample("NF 12345")]
    public string? Search { get; set; }

    /// <summary>Filters by EFFECTIVE method: <c>Payable.PaymentMethod ?? default paying account's method</c>.</summary>
    [SwaggerSchema("Filters by the EFFECTIVE payment method — the payable\u0027s own method when set, "
        + "otherwise the paying account\u0027s default. A payable with no method of its own still matches "
        + "through its account.")]
    public PaymentMethod? PaymentMethod { get; set; }
}

public sealed class ListPayables : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListPayables").WithTags("Finance")
            .WithSummary("Lists payables by installment.")
            .WithDescription("The operational Contas a Pagar view: one row per INSTALLMENT, not per payable, because "
                + "paying happens per parcela. A payable in three installments appears three times. For one row "
                + "per title use stage-items instead. Omitting companyId asks for the consolidated view and "
                + "needs a consolidated role.")
            .RequirePermission(Permissions.PayablesRead).Produces<PaginatedResponse<PayableListItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        UserContext user,
        [AsParameters] ListPayablesRequest req,
        [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, consolidated roles only.
        if (req.CompanyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);

        // Payable-level filters shared with GetPaymentQueueSummary so group headers match rows.
        var payables = await PayableQueueFilters.ApplyAsync(uow.ReadSet<Payable>(), uow, req, ct);

        // Accounting-competency filter: keep payables whose IssueDate falls in the given month. IssueDate is
        // the payable-level competency anchor, so it filters BEFORE the per-installment fan-out below. Kept out
        // of PayableQueueFilters on purpose — that helper also feeds the cash/due-date payment queue.
        var competency = Competency.Normalize(req.Competency);
        if (Competency.IsValid(competency))
        {
            var first = Competency.ToDateOnly(competency);
            var firstOfNext = Competency.ToDateOnly(Competency.AddMonths(competency, 1));
            payables = payables.Where(p => p.IssueDate != null && p.IssueDate >= first && p.IssueDate < firstOfNext);
        }

        // One row per installment (the operational Contas a Pagar list). Project to primitive fields
        // FIRST so the query and pagination translate to SQL, then wrap Guids into typed IDs in memory.
        // LEFT JOIN on RemittanceLine to surface remittance id/sequence for InRemittance installments.
        // CompanyId, VendorId, and PaymentMethod are projected so we can compute Rail in memory after
        // pagination (exactly 2 extra queries per page — no N+1 per row).
        var remittanceLines = uow.ReadSet<RemittanceLine>();
        var fiscalDocuments = uow.ReadSet<FiscalDocument>();
        var attachments = uow.ReadSet<PayableAttachment>();
        var rows = payables
            .SelectMany(p => p.Installments, (p, i) => new
            {
                PayableId = p.Id,
                InstallmentId = i.Id,
                i.Sequence,
                InstallmentCount = p.Installments.Count,
                p.CounterpartyName,
                p.Description,
                p.CompanyId,
                p.VendorId,
                p.CostCenterId,
                p.CategoryId,
                PaymentMethod = p.PaymentMethod,
                InstallmentPaymentMethod = i.PaymentMethod,
                i.DueDate,
                i.Amount,
                i.NetAmount,
                InstallmentStatus = i.Status,
                PayableStatus = p.Status,
                i.ScheduledPaymentDate,
                i.IsHeld,
                i.HoldReason,
                i.BoletoDigitableLine,
                i.PixKeyOverride,
                p.DocumentNumber,
                p.IssueDate,
                p.OriginUnitId,
                HasFiscalDocument = fiscalDocuments.Any(f => f.PayableId == p.Id)
                    || attachments.Any(a => a.PayableId == p.Id && a.Kind == PayableAttachmentKind.FiscalDocument)
                    || (p.RequiresFiscalDocument && attachments.Any(a => a.PayableId == p.Id && a.Kind == PayableAttachmentKind.Invoice)),
            })
            .GroupJoin(
                remittanceLines,
                x => x.InstallmentId,
                rl => rl.InstallmentId,
                (x, rls) => new { Row = x, Lines = rls })
            .SelectMany(
                g => g.Lines.DefaultIfEmpty(),
                (g, rl) => new
                {
                    g.Row.PayableId,
                    g.Row.InstallmentId,
                    g.Row.Sequence,
                    g.Row.InstallmentCount,
                    g.Row.CounterpartyName,
                    g.Row.Description,
                    g.Row.CompanyId,
                    g.Row.VendorId,
                    g.Row.CostCenterId,
                    g.Row.CategoryId,
                    g.Row.PaymentMethod,
                    g.Row.InstallmentPaymentMethod,
                    g.Row.DueDate,
                    g.Row.Amount,
                    g.Row.NetAmount,
                    g.Row.InstallmentStatus,
                    g.Row.PayableStatus,
                    g.Row.ScheduledPaymentDate,
                    g.Row.IsHeld,
                    g.Row.HoldReason,
                    g.Row.BoletoDigitableLine,
                    g.Row.PixKeyOverride,
                    g.Row.DocumentNumber,
                    g.Row.IssueDate,
                    g.Row.OriginUnitId,
                    g.Row.HasFiscalDocument,
                    RemittanceId = rl == null ? (Guid?)null : rl.RemittanceId,
                    RemittanceSequence = rl == null ? (int?)null : rl.Sequence,
                });
        if (req.DueFrom is { } from) rows = rows.Where(x => x.DueDate >= from);
        if (req.DueTo is { } to) rows = rows.Where(x => x.DueDate <= to);
        if (req.Paid is true) rows = rows.Where(x => x.InstallmentStatus == InstallmentStatus.Paid);
        if (req.Paid is false) rows = rows.Where(x => x.InstallmentStatus != InstallmentStatus.Paid);

        // Tie-break equal due dates so rows never shuffle between pages.
        var page = await rows
            .OrderBy(x => x.DueDate).ThenBy(x => x.PayableId).ThenBy(x => x.Sequence)
            .ToPaginatedAsync(pagination, ct);

        // ── Batch-resolve rail (exactly 2 extra queries for the whole page) ───────────────────────

        // 1. Distinct CompanyIds on the page → load default paying accounts in ONE query.
        var companyIds = page.Items.Select(x => x.CompanyId).Distinct().ToList();
        var defaultAccountByCompany = await uow.ReadSet<BankAccount>()
            .Where(b => companyIds.Contains(b.CompanyId) && b.IsDefaultForPayments && b.DeletedAt == null)
            .ToListAsync(ct);
        var accountByCompany = defaultAccountByCompany
            .GroupBy(b => b.CompanyId)
            .ToDictionary(g => g.Key, g => g.First());

        // 2. Distinct VendorIds on the page → load BankInfo + Document in ONE query.
        var vendorIds = page.Items.Select(x => x.VendorId).Distinct().ToList();
        var vendorsById = (await uow.ReadSet<VendorEntity>()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.BankInfo, v.Document })
            .ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v);

        return TypedResults.Ok(page.Map(x =>
        {
            accountByCompany.TryGetValue(x.CompanyId, out var acct);
            vendorsById.TryGetValue(x.VendorId, out var vendor);
            // Effective method: the installment's own method wins, else the payable's (AEL-933).
            var effectiveMethod = x.InstallmentPaymentMethod ?? x.PaymentMethod;
            var reason = acct is null
                ? RemittanceManualReason.BankNotSupported
                : RemittanceEligibility.Evaluate(acct, effectiveMethod, vendor?.BankInfo ?? BankInfo.Empty, x.BoletoDigitableLine, x.PixKeyOverride);
            var rail = reason == RemittanceManualReason.None ? "Remittance" : "Manual";
            var manualReason = reason == RemittanceManualReason.None ? null : reason.ToString();

            return new PayableListItemResponse(
                new PayableId(x.PayableId), new InstallmentId(x.InstallmentId), x.Sequence, x.InstallmentCount,
                x.CounterpartyName, x.Description, new CostCenterId(x.CostCenterId),
                x.CategoryId == null ? null : new ExpenseCategoryId(x.CategoryId.Value),
                x.DueDate, x.Amount, x.NetAmount, x.InstallmentStatus, x.PayableStatus,
                x.ScheduledPaymentDate, x.IsHeld, x.HoldReason,
                x.RemittanceId == null ? null : new RemittanceId(x.RemittanceId.Value),
                x.RemittanceSequence,
                rail, manualReason,
                vendor?.Document, new VendorId(x.VendorId),
                effectiveMethod,
                x.BoletoDigitableLine, x.DocumentNumber, x.IssueDate,
                x.OriginUnitId == null ? null : new UnitId(x.OriginUnitId.Value),
                x.HasFiscalDocument);
        }));
    }
}
