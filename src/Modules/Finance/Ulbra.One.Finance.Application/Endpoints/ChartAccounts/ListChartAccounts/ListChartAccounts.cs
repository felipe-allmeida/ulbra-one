using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ChartAccounts.ListChartAccounts;

public sealed class ListChartAccounts : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListChartAccounts").WithTags("Finance")
            .RequireAuthorization().Produces<List<ChartAccountResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [FromKeyedServices("finance")] IUnitOfWork uow,
        ChartType? chartType, AccountKind? kind, bool? activeOnly, CancellationToken ct)
    {
        var q = uow.ReadSet<ChartAccount>();
        if (chartType is { } ct2) q = q.Where(a => a.ChartType == ct2);
        if (kind is { } k) q = q.Where(a => a.Kind == k);
        if (activeOnly == true) q = q.Where(a => a.IsActive);
        var rows = await q.OrderBy(a => a.Code).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ChartAccountResponse.From).ToList());
    }
}
