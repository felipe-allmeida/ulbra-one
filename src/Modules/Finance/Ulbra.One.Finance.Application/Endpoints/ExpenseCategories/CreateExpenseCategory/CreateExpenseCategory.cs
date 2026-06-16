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

namespace Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.CreateExpenseCategory;

public sealed class CreateExpenseCategory : IEndpoint
{
    public sealed record Request { [NormalizeText] public string Name { get; set; } = string.Empty; }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateExpenseCategory").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<ExpenseCategoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.Problem(detail: "Name is required.", statusCode: 400, title: "ExpenseCategory.NameRequired");
        var exists = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Name == req.Name, ct);
        if (exists) return Results.Problem(detail: "A category with this name already exists.", statusCode: 400, title: "ExpenseCategory.Duplicate");
        var cat = new ExpenseCategory(req.Name);
        await uow.AddAsync(cat, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/expense-categories/{new ExpenseCategoryId(cat.Id)}", ExpenseCategoryResponse.From(cat));
    }
}
