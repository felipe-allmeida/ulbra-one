using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetGeneralLedger;

public sealed record GetGeneralLedgerRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company to report on. Omit to consolidate across every company.")]
    public CompanyId? CompanyId { get; set; }
    [SwaggerSchema("Restrict to a single ledger account. Takes precedence over Root when both are sent.")]
    public LedgerAccountId? LedgerAccountId { get; set; }

    /// <summary>Optional account-root filter: the first code digit "1".."5" (Asset/Liability/Revenue/Expense/Cost).</summary>
    [SwaggerSchema("Restrict to an account root — the first digit of the code: 1 asset, 2 liability, 3 revenue, "
        + "4 expense, 5 cost. Ignored when LedgerAccountId is set.")]
    [SwaggerExample("4")]
    public string? Root { get; set; }

    [SwaggerSchema("Start of the competence range, inclusive. Filters on the entry\u0027s competence date, not "
        + "on when it was posted.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }
    [SwaggerSchema("End of the competence range, inclusive.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }
    [SwaggerSchema("Restrict to entry lines charged to this cost center. Lines without one are excluded.")]
    public CostCenterId? CostCenterId { get; set; }
    [SwaggerSchema("Restrict to entry lines charged to this unit. Lines without one are excluded.")]
    public UnitId? UnitId { get; set; }
}

/// <summary>Livro Razão (read-only): per ledger account, its Posted movements in a competence range with a
/// running accumulated balance from an opening balance. Paginated by account (ordered by code); optionally
/// scoped by a single account, an account root (code prefix) and/or cost center. Consolidated when CompanyId is
/// null. Reads never touch the closed-period guard.</summary>
public sealed class GetGeneralLedger : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/ledger", Handle).WithName("GetGeneralLedger").WithTags("Accounting")
            .WithSummary("Gets the general ledger.")
            .WithDescription("Razao: the individual posted entry lines behind an account, oldest first, with running "
                + "balances. Paginated, unlike the other reports, because a single account over a year can be "
                + "very long. Narrow it with LedgerAccountId or Root - asking for the whole chart at once is "
                + "rarely what you want.")
            .RequirePermission(Permissions.AccountingRead)
            .Produces<PaginatedResponse<GeneralLedgerAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] GetGeneralLedgerRequest req, CancellationToken ct)
    {
        if (req.From is { } f && req.To is { } t && t < f)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.GeneralLedger.InvalidRange);

        var accountsPage = await GeneralLedgerAccountQuery
            .Qualifying(uow, req.CompanyId?.Value, req.LedgerAccountId?.Value, req.Root, req.CostCenterId?.Value, req.To, req.UnitId?.Value)
            .ToPaginatedAsync(req, ct);

        var ids = accountsPage.Items.Select(a => a.Id).ToList();
        var rows = await GeneralLedgerLoader.LoadAsync(
            uow, req.CompanyId?.Value, ids, req.CostCenterId?.Value, req.To, ct, req.UnitId?.Value);
        var sections = GeneralLedgerBuilder.Build(rows, req.From, req.To).ToDictionary(s => s.LedgerAccountId);

        var response = accountsPage.Map(a => GeneralLedgerAccountResponse.From(
            sections.TryGetValue(a.Id, out var section)
                ? section
                : new GeneralLedgerAccount(a.Id, a.Code, a.ReducedCode, a.Name, a.NormalBalance, 0m, [], 0m, 0m, 0m)));

        return TypedResults.Ok(response);
    }
}
