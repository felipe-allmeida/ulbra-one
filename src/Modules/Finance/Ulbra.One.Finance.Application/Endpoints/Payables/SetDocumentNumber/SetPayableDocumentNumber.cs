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
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetDocumentNumber;

/// <summary>Contábil sets/corrects a payable's número do documento (número da NF) from the Revisão de
/// Contabilidade drawer — the single-field edit for the número that the requisition-sourced payable often
/// arrives without. The número is fiscal metadata, editable in ANY state (including a paid payable), so there is
/// no terminal-state conflict here.</summary>
public sealed class SetPayableDocumentNumber : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Number of the vendor's fiscal document (número da NF). Send null to clear it.")]
        [SwaggerExample("NF 12345")]
        [NormalizeText] public string? DocumentNumber { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/document-number", Handle).WithName("SetPayableDocumentNumber").WithTags("Finance")
            .WithSummary("Sets a payable's document number.")
            .WithDescription("Single-field edit for the número do documento, from the accounting review "
                + "drawer — a payable that came from a requisition often arrives without one. The number is "
                + "fiscal metadata, not state, so it is editable in ANY status, including an already-paid "
                + "payable: there is no terminal-state conflict here. The change propagates to the "
                + "settlement entries already booked in accounting.")
            .RequirePermission(Permissions.PayablesWrite).WithRequestNormalizer<Request>()
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, IPublishEndpoint publish, CancellationToken ct)
    {
        // Load the installments/payments so we can carry the payment ids on the event — the settlement journal
        // entries are keyed by payment id, so Accounting can only find them via these ids.
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        payable.SetDocumentNumber(req.DocumentNumber);

        // Refresh the número da NF on any already-posted journal entries (incurral + baixas) for this payable —
        // the Accounting consumer amends their SourceReference (número no Livro Razão) in place. Published via the
        // bus outbox so it commits atomically with the edit; a no-op when nothing was posted yet.
        var paymentIds = payable.Installments.SelectMany(i => i.Payments).Select(p => p.Id).ToList();
        await publish.Publish(new PayableDocumentNumberChanged(payable.Id, payable.CompanyId, payable.DocumentNumber, paymentIds), ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
