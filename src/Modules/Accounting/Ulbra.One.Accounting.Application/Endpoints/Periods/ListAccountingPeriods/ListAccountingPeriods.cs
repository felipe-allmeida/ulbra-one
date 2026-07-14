using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Periods.ListAccountingPeriods;

public sealed record ListAccountingPeriodsRequest
{
    public CompanyId? CompanyId { get; set; }
    public int? FiscalYear { get; set; }
}

public sealed class ListAccountingPeriods : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListAccountingPeriods").WithTags("Accounting")
            .RequireAuthorization().Produces<List<AccountingPeriodResponse>>(StatusCodes.Status200OK);

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
