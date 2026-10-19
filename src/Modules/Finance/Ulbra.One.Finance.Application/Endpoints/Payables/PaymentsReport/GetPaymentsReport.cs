using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.PaymentsReport;

/// <summary>
/// The "Relação de Pagamentos" report list (one row per installment), the data behind the screen table.
/// Same filters feed the summary ("Copiar Resumo") and the PDF export, so the three always agree.
/// </summary>
public sealed class GetPaymentsReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetPaymentsReport").WithTags("Finance")
            .WithSummary("Lists the payments report by installment.")
            .WithDescription("The Relação de Pagamentos: one row per INSTALLMENT (parcela) whose due date — and "
                + "optionally payment date — falls in the filter window, across every status by default. "
                + "Resolves the requester, approver, unit and cost-center names for display. Omitting companyId "
                + "asks for the consolidated view and needs a consolidated role.")
            .RequirePermission(Permissions.PayablesRead)
            .Produces<PaginatedResponse<PaymentReportItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        UserContext user,
        IUserDirectory users,
        [AsParameters] PaymentsReportRequest req,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, consolidated roles only.
        if (req.CompanyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);

        var page = await PaymentsReportQuery.Build(uow, req)
            .OrderBy(r => r.DueDate).ThenBy(r => r.PayableId).ThenBy(r => r.Sequence)
            .ToPaginatedAsync(pagination, ct);
        var items = await PaymentsReportQuery.EnrichAsync(uow, users, page.Items, ct);
        return TypedResults.Ok(new PaginatedResponse<PaymentReportItemResponse>(
            items, page.Page, page.PageSize, page.Total, page.TotalPages));
    }
}
