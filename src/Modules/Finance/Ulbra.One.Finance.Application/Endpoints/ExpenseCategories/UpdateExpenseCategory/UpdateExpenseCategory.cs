using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.UpdateExpenseCategory;

public sealed class UpdateExpenseCategory : IEndpoint
{
    public sealed record Request { [NormalizeText] public string Name { get; set; } = string.Empty; public bool Active { get; set; } = true; }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateExpenseCategory").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<ExpenseCategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(ExpenseCategoryId id, Request req, [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        var cat = await uow.Set<ExpenseCategory>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (cat is null) return TypedResults.Problem(detail: "Category not found.", statusCode: 404, title: "ExpenseCategory.NotFound");
        if (string.IsNullOrWhiteSpace(req.Name))
            return TypedResults.Problem(detail: "Name is required.", statusCode: 400, title: "ExpenseCategory.NameRequired");
        var duplicate = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Name == req.Name && c.Id != id.Value, ct);
        if (duplicate)
            return TypedResults.Problem(detail: "A category with this name already exists.", statusCode: 400, title: "ExpenseCategory.Duplicate");
        cat.Rename(req.Name);
        if (req.Active) cat.Activate(); else cat.Deactivate();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ExpenseCategoryResponse.From(cat));
    }
}
