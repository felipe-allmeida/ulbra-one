using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.CompleteAccounting;

/// <summary>Accountant confirms the accounting review of a PendingAccounting payable ("Contabilizar"):
/// verifies retentions/vendor data, then releases it to payment. Hard-gated on a registered Nota Fiscal.
/// Emits <see cref="PayableScheduled"/> (the accounting incurral) for the manual esteira; the requisition
/// auto-schedule path (PayableReceivedConsumer) emits it too.</summary>
public sealed class CompleteAccounting : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/accounting/confirm", Handle).WithName("CompletePayableAccounting").WithTags("Finance")
            .WithSummary("Confirms the accounting review of a payable.")
            .WithDescription("The accountant signs off on a payable awaiting review - checking withholdings and vendor "
                + "data - and releases it to payment, booking the expense incurral in accounting. Hard-gated on "
                + "a registered nota fiscal: without one it returns 400, no exceptions. Confirming a payable in "
                + "any other state returns 409.")
            .RequirePermission(Permissions.PayablesWrite).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // Hard gate: the fiscal document must be registered before accounting completes — a parsed FiscalDocument
        // (XML) or an attachment typed as Nota Fiscal, and for Conta Fixa a Fatura also qualifies (Fatura OU NF).
        // Advance payables (adiantamento) are exempt — paid before the document exists.
        var hasFiscalDocument = await PayableFiscalGate.HasQualifyingDocumentAsync(uow, p, ct);
        if (!hasFiscalDocument && p.Origin != PayableOrigin.Advance)
            return ErrorResults.InvalidRequest("A Nota Fiscal must be registered before the payable can be sent to payment.", ErrorCodes.Payable.FiscalDocumentMissing);

        try { p.CompleteAccounting(user.UserId, clock.GetUtcNow(), hasFiscalDocument); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payable.InvalidState); }

        // Supplier identification for the Livro Razão history (razão social + CNPJ). Cross-module read by the
        // loose Guid VendorId (same pattern as GetPayableAccountingView); falls back to the payable's snapshot name.
        var vendor = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == p.VendorId)
            .Select(v => new { v.Name, v.Document })
            .FirstOrDefaultAsync(ct);
        // Shared payload builder — reused by PayableReceivedConsumer's auto-schedule so they can't drift.
        await publish.Publish(PayableScheduledFactory.Build(p, vendor?.Name, vendor?.Document), ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
