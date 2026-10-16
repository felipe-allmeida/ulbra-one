using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.UpdateExpenseCategory;

public sealed class UpdateExpenseCategory : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Category name. Must stay unique.")]
        [SwaggerExample("Manutencao predial")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Whether the category can still be assigned. Deactivating keeps it on the payables "
            + "that already use it.")]
        public bool Active { get; set; } = true;
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateExpenseCategory").WithTags("Finance")
            .WithSummary("Updates an expense category.")
            .WithDescription("Renames a category or deactivates it. A full replace, not a patch.")
            .RequirePermission(Permissions.PayablesWrite).WithRequestNormalizer<Request>()
            .Produces<ExpenseCategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(ExpenseCategoryId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var cat = await uow.Set<ExpenseCategory>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (cat is null) return ErrorResults.NotFound("Category not found.", ErrorCodes.ExpenseCategory.NotFound);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.ExpenseCategory.NameRequired);
        var duplicate = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Name == req.Name && c.Id != id.Value, ct);
        if (duplicate)
            return ErrorResults.InvalidRequest("A category with this name already exists.", ErrorCodes.ExpenseCategory.Duplicate);
        cat.Rename(req.Name);
        if (req.Active) cat.Activate(); else cat.Deactivate();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ExpenseCategoryResponse.From(cat));
    }
}
