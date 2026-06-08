using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.ListExpenseCategories;

public sealed class ListExpenseCategories : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListExpenseCategories").WithTags("Finance")
            .RequireAuthorization().Produces<List<ExpenseCategoryResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [FromKeyedServices("finance")] IUnitOfWork uow, bool? includeInactive, CancellationToken ct)
    {
        var q = uow.ReadSet<ExpenseCategory>();
        if (includeInactive != true) q = q.Where(c => c.Active);
        var rows = await q.OrderBy(c => c.Name).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ExpenseCategoryResponse.From).ToList());
    }
}
