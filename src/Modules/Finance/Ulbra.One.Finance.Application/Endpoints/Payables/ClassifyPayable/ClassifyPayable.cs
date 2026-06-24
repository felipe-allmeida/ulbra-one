using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayable;

public sealed class ClassifyPayable : IEndpoint
{
    public sealed record Request(ChartAccountId? AccountingAccountId, ChartAccountId? FinancialAccountId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/classify", Handle).WithName("ClassifyPayable").WithTags("Finance")
            .RequireAuthorization().Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, req.FinancialAccountId?.Value, ct);
        if (err is not null) return TypedResults.Problem(detail: "Invalid chart account.", statusCode: 400, title: err);

        payable.Classify(req.AccountingAccountId?.Value, req.FinancialAccountId?.Value);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
