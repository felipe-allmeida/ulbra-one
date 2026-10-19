using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Periods.ListAccountingPeriods;

/// <summary>Query-string filters for the accounting period list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListAccountingPeriodsRequest
{
    [SwaggerSchema("Company that owns the periods. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Fiscal year to list. Omit for every year on record.")]
    [SwaggerExample("2026")]
    public int? FiscalYear { get; set; }
}

public sealed class ListAccountingPeriods : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListAccountingPeriods").WithTags("Accounting")
            .WithSummary("Lists accounting periods.")
            .WithDescription("Which competences are open and which are closed, per company. Only periods "
                + "that were explicitly closed or reopened have a row — a month never touched is open by "
                + "absence and will not appear here.")
            .RequirePermission(Permissions.AccountingRead).Produces<List<AccountingPeriodResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] ListAccountingPeriodsRequest req, CancellationToken ct)
    {
        var q = uow.ReadSet<AccountingPeriod>();
        if (req.CompanyId is { } cid) q = q.Where(p => p.CompanyId == cid.Value);
        if (req.FiscalYear is { } fy) q = q.Where(p => p.FiscalYear == fy);
        var rows = await q
            .OrderBy(p => p.CompanyId).ThenBy(p => p.FiscalYear).ThenBy(p => p.Month)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(AccountingPeriodResponse.From).ToList());
    }
}
