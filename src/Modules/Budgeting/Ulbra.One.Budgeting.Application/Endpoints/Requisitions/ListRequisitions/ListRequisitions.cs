using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListRequisitions;

/// <summary>
/// Query-string filters for the requisition browse list, bound via <c>[AsParameters]</c>.
/// Inherits <c>page</c> / <c>pageSize</c> from <see cref="PaginationQueryParameters"/>, which is what
/// merges filters and paging into a single bound object — <c>[AsParameters]</c> does not nest.
/// </summary>
public sealed record ListRequisitionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the requisitions. Omit for the consolidated view across every company, "
        + "which only a consolidated or scoped role may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Unit (organizational branch) that owns the requisitions.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Cost center charged for the requisitions.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Vendor the requisitions are addressed to.")]
    public VendorId? VendorId { get; set; }

    [SwaggerSchema("Workflow stages to keep, as an OR-set: repeat the key to widen it "
        + "(`?status=Draft&status=Triage`). A single value binds to a one-element set.")]
    public RequisitionStatus[]? Status { get; set; }

    [SwaggerSchema("Requisition type.")]
    public RequisitionType? Type { get; set; }

    [SwaggerSchema("Exact accounting competency (`YYYY-MM`): keeps requisitions whose [start, end] window "
        + "covers that month. Takes precedence over CompetencyFrom/CompetencyTo when both are supplied.")]
    [SwaggerExample("2026-08")]
    public string? Competency { get; set; }

    [SwaggerSchema("Start of a competency range (`YYYY-MM`), inclusive. Keeps requisitions that are still "
        + "open or that end on or after this month. Ignored when Competency is supplied.")]
    [SwaggerExample("2026-01")]
    public string? CompetencyFrom { get; set; }

    [SwaggerSchema("End of a competency range (`YYYY-MM`), inclusive. Keeps requisitions that start on or "
        + "before this month. Ignored when Competency is supplied.")]
    [SwaggerExample("2026-12")]
    public string? CompetencyTo { get; set; }

    [SwaggerSchema("Free-text search over description, requisition code (with or without the leading `#`), "
        + "vendor name and vendor CNPJ/CPF. Case-insensitive.")]
    [SwaggerExample("#1042")]
    public string? Search { get; set; }

    [SwaggerSchema("Lower bound (inclusive) for the estimated value, in BRL.")]
    [SwaggerExample("1500.00")]
    public decimal? MinValue { get; set; }

    [SwaggerSchema("Upper bound (inclusive) for the estimated value, in BRL.")]
    [SwaggerExample("25000.00")]
    public decimal? MaxValue { get; set; }

    [SwaggerSchema("Sort field: `code`, `estimate`, `competency`, `status` (workflow pipeline order) or "
        + "`createdAt`. Prefix with `-` for descending. Defaults to company then code, both descending.")]
    [SwaggerExample("-createdAt")]
    public string? Sort { get; set; }

    [SwaggerSchema("Keep only requisitions created by the authenticated caller (requester dashboard). "
        + "The owner is resolved server-side from the token, never from the query string.")]
    public bool? Mine { get; set; }

    [SwaggerSchema("Keep only post-approval requisitions still missing a fiscal document — either nothing "
        + "attached yet, or Accounting explicitly asked for it (AwaitingFiscalDocument).")]
    public bool? AwaitingNf { get; set; }
}

public sealed class ListRequisitions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListRequisitions")
            .WithTags("Budgeting")
            .WithSummary("Lists requisitions.")
            .WithDescription("Paginated browse list for the requisition workflow. Every filter is optional "
                + "and combines with AND, except Status, which is an OR-set. Omitting CompanyId asks for the "
                + "consolidated view across all companies (403 unless the caller holds a consolidated role); "
                + "a scoped role always reads within its Company x Unit x Cost Center scope, fail-closed.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<PaginatedResponse<RequisitionResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListRequisitionsRequest req,
        UserContext userContext,
        IUserScopeDirectory userScope,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, but only for a
        // consolidated role. Scoped roles are exempt (the scope filter below already restricts them); a plain
        // non-scoped/non-consolidated token must NOT sweep every company via an omitted companyId.
        if (RequisitionScope.GuardConsolidated(req.CompanyId, userContext) is { } forbidden)
            return forbidden;

        // Company / "mine" ownership / role-scope filtering is shared with the stage-counts endpoint so the
        // counts never diverge from the rows this list returns for the same caller.
        var query = await RequisitionScope.ApplyAsync(
            uow.ReadSet<Requisition>(), req.CompanyId, req.Mine == true, userContext, userScope, ct);

        // Load the attachments so RequisitionResponse.From computes FiscalDocumentPending (the "NF pendente"
        // badge) from the real files — without this every post-approval row reads as pending even with an NF.
        query = query.Include(r => r.Attachments);

        if (req.UnitId is { } uid) query = query.Where(r => r.UnitId == uid.Value);
        if (req.CostCenterId is { } ccid) query = query.Where(r => r.CostCenterId == ccid.Value);
        if (req.VendorId is { } vid) query = query.Where(r => r.VendorId == vid.Value);
        if (req.Status is { Length: > 0 } sts) query = query.Where(r => sts.Contains(r.Status));
        if (req.Type is { } tp) query = query.Where(r => r.Type == tp);
        if (req.MinValue is { } lo) query = query.Where(r => r.EstimateValue >= lo);
        if (req.MaxValue is { } hi) query = query.Where(r => r.EstimateValue <= hi);

        // "pending fiscal document" (requester dashboard): post-approval requisitions (a Payable exists) still
        // without a Nota Fiscal. Type-aware (AEL-959), mirrors RequisitionResponse.FiscalDocumentPending — a
        // boleto/fatura/generic attachment does NOT clear it, only an NF (Kind=FiscalDocument) or an .xml.
        // EXISTS subquery on the attachments, translated to SQL.
        if (req.AwaitingNf == true)
            query = query.Where(r =>
                (r.Status == RequisitionStatus.InPayment || r.Status == RequisitionStatus.AwaitingFiscalDocument
                 || r.Status == RequisitionStatus.Paid || r.Status == RequisitionStatus.Finalized)
                && !r.Attachments.Any(a =>
                    a.Kind == RequisitionAttachmentKind.FiscalDocument || a.FileName.ToLower().EndsWith(".xml")));

        // A requisition touches the [start, end] window; treat a competency filter as "overlaps that month".
        // Exact competency wins over a range when both are supplied. "YYYY-MM" is zero-padded, so string.Compare
        // (EF-translatable) is a correct chronological comparison; an open-ended requisition has EndCompetency null.
        if (!string.IsNullOrWhiteSpace(req.Competency))
        {
            var competency = req.Competency;
            query = query.Where(r =>
                string.Compare(r.StartCompetency, competency) <= 0 &&
                (r.EndCompetency == null || string.Compare(r.EndCompetency, competency) >= 0));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(req.CompetencyFrom))
            {
                var competencyFrom = req.CompetencyFrom;
                query = query.Where(r => r.EndCompetency == null || string.Compare(r.EndCompetency, competencyFrom) >= 0);
            }
            if (!string.IsNullOrWhiteSpace(req.CompetencyTo))
            {
                var competencyTo = req.CompetencyTo;
                query = query.Where(r => string.Compare(r.StartCompetency, competencyTo) <= 0);
            }
        }

        // Free-text: description / #code / vendor name / vendor document (alphanumeric CNPJ / digits CPF) —
        // correlated read over the single context by loose Id, same pattern as PayableQueueFilters (AEL-865).
        // IgnoreQueryFilters: a live requisition must stay findable by the vendor name it displays,
        // even after that vendor is soft-deleted (mirrors the display batch below).
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim();
            var lowered = term.ToLower();
            var docTerm = DocumentValidator.NormalizeCnpj(term);
            var codeMatch = int.TryParse(term.TrimStart('#'), out var code) ? code : (int?)null;
            var vendors = uow.ReadSet<VendorEntity>().IgnoreQueryFilters();
            query = query.Where(r =>
                r.Description.ToLower().Contains(lowered)
                || (codeMatch != null && r.Code == codeMatch)
                || vendors.Any(v => v.Id == r.VendorId && v.Name.ToLower().Contains(lowered))
                || (docTerm != "" && vendors.Any(v => v.Id == r.VendorId && v.Document.Contains(docTerm))));
        }

        var page = await ApplySort(query, req.Sort).ToPaginatedAsync(req, ct);

        // Vendor names resolved in one batch (read-only cross-module lookup, same pattern as ListMyDecisions).
        // IgnoreQueryFilters so a row whose vendor was later soft-deleted still shows the name.
        var vendorIds = page.Items.Select(r => r.VendorId).Distinct().ToList();
        var vendorNames = await uow.ReadSet<VendorEntity>()
            .IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name, ct);

        return TypedResults.Ok(page.Map(r =>
            RequisitionResponse.From(r, vendorNames.GetValueOrDefault(r.VendorId, string.Empty))));
    }

    // Status persists as string (HasConversion<string>), so a plain OrderBy(Status) would sort by the
    // English enum name; CASE-map to the pipeline order the user actually sees (workflow → terminals).
    private static readonly System.Linq.Expressions.Expression<Func<Requisition, int>> StatusPipelineRank = r =>
        r.Status == RequisitionStatus.Draft ? 0
        : r.Status == RequisitionStatus.Triage ? 1
        : r.Status == RequisitionStatus.FinancialComplement ? 2
        : r.Status == RequisitionStatus.Approval ? 3
        : r.Status == RequisitionStatus.Complement ? 4
        : r.Status == RequisitionStatus.InPayment ? 5
        : r.Status == RequisitionStatus.AwaitingFiscalDocument ? 6
        : r.Status == RequisitionStatus.Paid ? 7
        : r.Status == RequisitionStatus.Finalized ? 8
        : 9;

    // Every branch ends with a unique tie-break (Id) so Skip/Take pagination stays deterministic
    // (a row must not shuffle between adjacent pages — same discipline as ListMyDecisions.cs:50-52).
    private static IQueryable<Requisition> ApplySort(IQueryable<Requisition> query, string? sort)
    {
        IOrderedQueryable<Requisition> ordered = sort switch
        {
            "code" => query.OrderBy(r => r.Code),
            "-code" => query.OrderByDescending(r => r.Code),
            "estimate" => query.OrderBy(r => r.EstimateValue),
            "-estimate" => query.OrderByDescending(r => r.EstimateValue),
            "competency" => query.OrderBy(r => r.StartCompetency),
            "-competency" => query.OrderByDescending(r => r.StartCompetency),
            "status" => query.OrderBy(StatusPipelineRank),
            "-status" => query.OrderByDescending(StatusPipelineRank),
            "createdAt" => query.OrderBy(r => r.CreatedAt),
            "-createdAt" => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.CompanyId).ThenByDescending(r => r.Code),
        };
        return ordered.ThenBy(r => r.Id);
    }
}
