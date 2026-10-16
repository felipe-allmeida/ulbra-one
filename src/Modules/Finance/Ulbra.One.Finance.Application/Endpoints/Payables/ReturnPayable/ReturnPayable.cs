using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ReturnPayable;

public sealed class ReturnPayable : IEndpoint
{
    public sealed record ReturnRequest(
        [property: SwaggerSchema("Why the title is being returned. Shown to the requester on the requisition.")]
        [property: SwaggerExample("Nota fiscal divergente do pedido.")]
        string? Reason,
        [property: SwaggerSchema("Which role to return the requisition to: Requester or Controllership. "
            + "Defaults to Requester.")]
        PayableReturnTarget? ReturnTo);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/return", Handle).WithName("ReturnPayable").WithTags("Finance")
            .WithSummary("Returns a requisition-sourced payable to the requester or controllership.")
            .WithDescription("Finance bounces a title generated from a requisition back to the requester "
                + "(→ Complemento) or controllership (→ Triagem), carrying the reason; the requisition leaves "
                + "payment for correction and its accounting incurral is reversed. On re-approval the same title "
                + "is refreshed and re-booked. Returns 409 when the payable is not requisition-sourced or is "
                + "already paid/cancelled/returned.")
            .RequirePermission(Permissions.PayablesWrite).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, ReturnRequest req, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);
        if (p.OriginRequisitionId is not { } reqId)
            return ErrorResults.InvalidState("Only a requisition-sourced payable can be returned.", ErrorCodes.Payable.InvalidState);
        if (p.Status is PayableStatus.Rejected)
            return ErrorResults.InvalidState("Payable has already been returned.", ErrorCodes.Payable.InvalidState);
        try { p.Reject(req.Reason, user.UserId, clock.GetUtcNow()); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payable.InvalidState); }
        // Budgeting returns the requisition to the chosen role; Accounting reverses the incurral.
        await publish.Publish(new PayableRejected(p.Id, p.CompanyId, reqId, req.Reason, user.UserId,
            (req.ReturnTo ?? PayableReturnTarget.Requester).ToString()), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
