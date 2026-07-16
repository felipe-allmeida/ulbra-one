using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetGeneralLedger;

public sealed record GetGeneralLedgerRequest
{
    public CompanyId? CompanyId { get; set; }
    public LedgerAccountId? LedgerAccountId { get; set; }

    /// <summary>Optional account-root filter: the first code digit "1".."5" (Asset/Liability/Revenue/Expense/Cost).</summary>
    public string? Root { get; set; }

    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public CostCenterId? CostCenterId { get; set; }
}

/// <summary>Livro Razão (read-only): per ledger account, its Posted movements in a competence range with a
/// running accumulated balance from an opening balance. Paginated by account (ordered by code); optionally
/// scoped by a single account, an account root (code prefix) and/or cost center. Consolidated when CompanyId is
/// null. Reads never touch the closed-period guard.</summary>
public sealed class GetGeneralLedger : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/ledger", Handle).WithName("GetGeneralLedger").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingRead)
            .Produces<PaginatedResponse<GeneralLedgerAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] GetGeneralLedgerRequest req,
        [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        if (req.From is { } f && req.To is { } t && t < f)
            return TypedResults.Problem(detail: "'to' must not precede 'from'.",
                statusCode: StatusCodes.Status400BadRequest, title: "GeneralLedger.InvalidRange");

        var accountsPage = await GeneralLedgerAccountQuery
            .Qualifying(uow, req.CompanyId?.Value, req.LedgerAccountId?.Value, req.Root, req.CostCenterId?.Value, req.To)
            .ToPaginatedAsync(pagination, ct);

        var ids = accountsPage.Items.Select(a => a.Id).ToList();
        var rows = await GeneralLedgerLoader.LoadAsync(
            uow, req.CompanyId?.Value, ids, req.CostCenterId?.Value, req.To, ct);
        var sections = GeneralLedgerBuilder.Build(rows, req.From, req.To).ToDictionary(s => s.LedgerAccountId);

        var response = accountsPage.Map(a => GeneralLedgerAccountResponse.From(
            sections.TryGetValue(a.Id, out var section)
                ? section
                : new GeneralLedgerAccount(a.Id, a.Code, a.ReducedCode, a.Name, a.NormalBalance, 0m, [], 0m, 0m, 0m)));

        return TypedResults.Ok(response);
    }
}
