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

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.CreateExpenseCategory;

public sealed class CreateExpenseCategory : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Category name. Must be unique.")]
        [SwaggerExample("Manutencao predial")]
        [NormalizeText] public string Name { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateExpenseCategory").WithTags("Finance")
            .WithSummary("Creates an expense category.")
            .WithDescription("Adds a management grouping for payables. Names are unique.")
            .RequirePermission(Permissions.PayablesWrite).WithRequestNormalizer<Request>()
            .Produces<ExpenseCategoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.ExpenseCategory.NameRequired);
        var exists = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Name == req.Name, ct);
        if (exists) return ErrorResults.InvalidRequest("A category with this name already exists.", ErrorCodes.ExpenseCategory.Duplicate);
        var cat = new ExpenseCategory(req.Name);
        await uow.AddAsync(cat, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/expense-categories/{new ExpenseCategoryId(cat.Id)}", ExpenseCategoryResponse.From(cat));
    }
}
