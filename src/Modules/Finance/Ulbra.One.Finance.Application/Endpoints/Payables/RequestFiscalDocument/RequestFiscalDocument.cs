using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RequestFiscalDocument;

/// <summary>"Solicitar Nota Fiscal": the Contábil (or the Tributário, during análise tributária) asks the requester
/// to provide the NF of a requisition-sourced payable that is still NF-pending. Publishes
/// <see cref="PayableFiscalDocumentRequested"/> (EF bus outbox); Budgeting resolves the requester and Notifications
/// pings them. Only requisition-sourced payables have a requester to notify.</summary>
public sealed class RequestFiscalDocument : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/request-fiscal-document", Handle).WithName("RequestPayableFiscalDocument").WithTags("Finance")
            .WithSummary("Asks the requester for the fiscal document.")
            .WithDescription("Notifies the person who raised the originating requisition that the nota fiscal is still "
                + "missing. Only works on a payable that came from a requisition - a manually created one has "
                + "no requester to ask, and returns 400.")
            .RequireAnyPermission(Permissions.PayablesWrite, Permissions.PayablesTax).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        PayableId id, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);
        if (p.OriginRequisitionId is not { } requisitionId)
            return ErrorResults.InvalidRequest("Only requisition-sourced payables can request a fiscal document from the requester.", ErrorCodes.Payable.NoRequisitionOrigin);

        // Stamp who requested so the requester's later NF upload notifies this Contábil user back (AEL-865).
        p.MarkFiscalDocumentRequested(Guid.Parse(user.UserId));

        // Publish before SaveChanges so it rides the EF bus outbox. The Budgeting consumer resolves the requester
        // (Requisition.CreatedBy) and re-publishes for Notifications. Idempotency: RequestId dedups at the sink.
        await publish.Publish(
            new PayableFiscalDocumentRequested(Guid.NewGuid(), p.Id, p.CompanyId, requisitionId, p.OriginRequisitionCode, clock.GetUtcNow()),
            ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
