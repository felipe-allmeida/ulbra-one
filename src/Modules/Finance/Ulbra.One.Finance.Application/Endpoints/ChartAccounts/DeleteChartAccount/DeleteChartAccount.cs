using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ChartAccounts.DeleteChartAccount;

public sealed class DeleteChartAccount : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}", Handle).WithName("DeleteChartAccount").WithTags("Finance")
            .RequireAuthorization().Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        ChartAccountId id,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var account = await uow.Set<ChartAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null) return TypedResults.Problem(detail: "Chart account not found.", statusCode: 404, title: "ChartAccount.NotFound");

        var hasChildren = await uow.ReadSet<ChartAccount>().AnyAsync(a => a.ParentId == id.Value, ct);
        if (hasChildren) return TypedResults.Problem(detail: "Chart account has children; remove or reparent them first.", statusCode: 409, title: "ChartAccount.HasChildren");

        var inUse = await uow.ReadSet<Payable>().AnyAsync(p => p.AccountingAccountId == id.Value || p.FinancialAccountId == id.Value, ct);
        if (inUse) return TypedResults.Problem(detail: "Chart account is in use by payables; deactivate it instead.", statusCode: 409, title: "ChartAccount.InUse");

        account.MarkDeleted(clock.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
