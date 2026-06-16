using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayableBatch;

public sealed class ClassifyPayableBatch : IEndpoint
{
    public sealed record Request(
        IReadOnlyList<PayableId> PayableIds, ChartAccountId? AccountingAccountId, ChartAccountId? FinancialAccountId);

    public sealed record Response(int ClassifiedCount);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/classify-batch", Handle).WithName("ClassifyPayableBatch").WithTags("Finance")
            .RequireAuthorization().Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req,
        [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, req.FinancialAccountId?.Value, ct);
        if (err is not null) return TypedResults.Problem(detail: "Invalid chart account.", statusCode: 400, title: err);

        var ids = req.PayableIds.Select(p => p.Value).ToList();
        var payables = await uow.Set<Payable>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var payable in payables)
            payable.Classify(req.AccountingAccountId?.Value, req.FinancialAccountId?.Value);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(new Response(payables.Count));
    }
}
