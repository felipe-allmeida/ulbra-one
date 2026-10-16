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
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RejectPayable;

public sealed class RejectPayable : IEndpoint
{
    public sealed record RejectRequest(
        [property: SwaggerSchema("Why the payable is being rejected. Optional, but it is what the "
            + "requester sees on the payable.")]
        [property: SwaggerExample("Nota fiscal divergente do pedido.")]
        string? Reason,
        [property: SwaggerSchema("For a requisition-sourced payable, which role to return it to. "
            + "Defaults to Requester.")]
        PayableReturnTarget? ReturnTo);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reject", Handle).WithName("RejectPayable").WithTags("Finance")
            .WithSummary("Rejects a payable.")
            .WithDescription("Sends a payable awaiting approval back instead of releasing it for payment. "
                + "A requisition-sourced payable returns its requisition to the requester or controllership; "
                + "a Finance-only payable is simply rejected. Rejecting from any other state returns 409.")
            .RequirePermission(Permissions.PayablesApprove).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, RejectRequest req, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);
        try { p.Reject(req.Reason, user.UserId, clock.GetUtcNow()); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payable.InvalidState); }
        // Requisition-sourced: bounce the requisition back to the chosen role (Finance-only payables stay terminal).
        if (p.OriginRequisitionId is { } reqId)
            await publish.Publish(new PayableRejected(p.Id, p.CompanyId, reqId, req.Reason, user.UserId,
                (req.ReturnTo ?? PayableReturnTarget.Requester).ToString()), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
