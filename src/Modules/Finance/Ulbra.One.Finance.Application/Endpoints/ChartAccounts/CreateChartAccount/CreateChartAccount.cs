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
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ChartAccounts.CreateChartAccount;

public sealed class CreateChartAccount : IEndpoint
{
    public sealed record Request
    {
        public ChartType ChartType { get; set; }
        [NormalizeText] public string Code { get; set; } = string.Empty;
        [NormalizeText] public string Name { get; set; } = string.Empty;
        public AccountKind Kind { get; set; }
        public ChartAccountId? ParentId { get; set; }
        public AccountNature? Nature { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateChartAccount").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<ChartAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(Request req, [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return Results.Problem(detail: "Code is required.", statusCode: 400, title: "ChartAccount.Invalid");
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.Problem(detail: "Name is required.", statusCode: 400, title: "ChartAccount.Invalid");

        var account = new ChartAccount(req.ChartType, req.Code, req.Name, req.Kind, parentId: null, req.Nature);

        if (req.ParentId is { } parentId)
        {
            var parent = await uow.Set<ChartAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return Results.Problem(detail: "Parent account not found.", statusCode: 400, title: "ChartAccount.InvalidParent");
            try { account.Reparent(parent); }
            catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 400, title: "ChartAccount.InvalidParent"); }
        }

        var duplicate = await uow.ReadSet<ChartAccount>()
            .AnyAsync(a => a.ChartType == req.ChartType && a.Code == account.Code, ct);
        if (duplicate)
            return Results.Problem(detail: "A chart account with this code already exists.", statusCode: 409, title: "ChartAccount.DuplicateCode");

        await uow.AddAsync(account, ct);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Results.Problem(detail: "A chart account with this code already exists.", statusCode: 409, title: "ChartAccount.DuplicateCode");
        }

        return Results.Created($"/api/chart-accounts/{new ChartAccountId(account.Id)}", ChartAccountResponse.From(account));
    }
}
