using MassTransit;
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
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ReversePayment;

public sealed class ReversePayment : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the payment is being reversed. Optional, and kept on the reversal.")]
        [property: SwaggerExample("Pagamento em duplicidade.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/payments/{paymentId}/reverse", Handle)
            .WithName("ReversePayment").WithTags("Finance")
            .WithSummary("Reverses a registered payment.")
            .WithDescription("Undoes a payment by recording a reversal — the payment row itself stays, "
                + "because the trail is append-only. The installment balance goes back up and the payable "
                + "returns to its pre-payment state. Accounting is rebooked accordingly. Reversing an "
                + "already-reversed payment returns 409.").RequirePermission(Permissions.PayablesPay)
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, PaymentId paymentId, Request req,
        IUnitOfWork uow, IPublishEndpoint publish, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var payment = payable.FindPayment(paymentId.Value);
        if (payment is null) return ErrorResults.NotFound("Payment not found.", ErrorCodes.Payment.NotFound);

        var installment = payable.FindInstallment(payment.InstallmentId);
        try { installment!.ReversePayment(paymentId.Value, req.Reason); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payment.AlreadyReversed); }
        payable.RecomputeSettlement();

        await publish.Publish(new PaymentReversed(paymentId.Value, payable.Id, payable.CompanyId,
            RequisitionId: payable.OriginRequisitionId), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
