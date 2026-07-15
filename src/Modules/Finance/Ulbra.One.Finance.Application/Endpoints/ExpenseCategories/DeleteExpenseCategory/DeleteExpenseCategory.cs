using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.DeleteExpenseCategory;

public sealed class DeleteExpenseCategory : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}", Handle).WithName("DeleteExpenseCategory").WithTags("Finance")
            .RequirePermission(Permissions.PayablesWrite).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        ExpenseCategoryId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var cat = await uow.Set<ExpenseCategory>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (cat is null) return TypedResults.Problem(detail: "Category not found.", statusCode: 404, title: "ExpenseCategory.NotFound");
        var inUse = await uow.ReadSet<Payable>().AnyAsync(p => p.CategoryId == id.Value, ct);
        if (inUse) return TypedResults.Problem(detail: "Category is in use; deactivate it instead.", statusCode: 409, title: "ExpenseCategory.InUse");
        cat.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
