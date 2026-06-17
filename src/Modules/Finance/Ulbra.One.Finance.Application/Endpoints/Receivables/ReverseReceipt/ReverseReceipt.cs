using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.ReverseReceipt;

public sealed class ReverseReceipt : IEndpoint
{
    public sealed record ReverseReceiptRequest(string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/receipts/{receiptId}/reverse", Handle)
            .WithName("ReverseReceipt").WithTags("Finance").RequireAuthorization()
            .Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, ReceiptId receiptId, ReverseReceiptRequest req,
        UserContext user, [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider timeProvider, CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound("Receivable.NotFound", "Receivable not found.")).ToOk();

        var receipt = receivable.FindReceipt(receiptId.Value);
        if (receipt is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound("Receipt.NotFound", "Receipt not found.")).ToOk();

        var installment = receivable.FindInstallment(receipt.InstallmentId);
        try
        {
            installment!.ReverseReceipt(receiptId.Value, Guid.Parse(user.UserId), timeProvider.GetUtcNow(), req.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receipt.AlreadyReversed");
        }
        receivable.RecomputeSettlement();

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ReceivableResponse.From(receivable, ReceivableClock.Today(timeProvider)));
    }
}
