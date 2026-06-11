using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Normalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ChartAccounts.UpdateChartAccount;

public sealed class UpdateChartAccount : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string Name { get; set; } = string.Empty;
        public ChartAccountId? ParentId { get; set; }
        public AccountNature? Nature { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateChartAccount").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<ChartAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(ChartAccountId id, Request req, [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        var account = await uow.Set<ChartAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null) return Results.Problem(detail: "Chart account not found.", statusCode: 404, title: "ChartAccount.NotFound");
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.Problem(detail: "Name is required.", statusCode: 400, title: "ChartAccount.Invalid");

        account.Rename(req.Name);

        ChartAccount? parent = null;
        if (req.ParentId is { } parentId)
        {
            parent = await uow.Set<ChartAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return Results.Problem(detail: "Parent account not found.", statusCode: 400, title: "ChartAccount.InvalidParent");
        }
        try { account.Reparent(parent); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 400, title: "ChartAccount.InvalidParent"); }

        account.SetNature(req.Nature);
        if (req.IsActive) account.Activate(); else account.Deactivate();

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ChartAccountResponse.From(account));
    }
}
