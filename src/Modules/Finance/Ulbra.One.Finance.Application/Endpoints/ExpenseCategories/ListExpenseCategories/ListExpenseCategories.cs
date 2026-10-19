using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.ListExpenseCategories;

public sealed class ListExpenseCategories : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListExpenseCategories").WithTags("Finance")
            .WithSummary("Lists expense categories.")
            .WithDescription("The categories used to group payables. A reference list, no paging. Distinct from the ledger "
                + "account: the category is a management grouping, the ledger account is what accounting posts "
                + "to.")
            .RequirePermission(Permissions.PayablesRead).Produces<List<ExpenseCategoryResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [SwaggerSchema("Include deactivated categories. Omitted returns only active ones.")]
        bool? includeInactive,
        CancellationToken ct)
    {
        var q = uow.ReadSet<ExpenseCategory>();
        if (includeInactive != true) q = q.Where(c => c.Active);
        var rows = await q.OrderBy(c => c.Name).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ExpenseCategoryResponse.From).ToList());
    }
}
