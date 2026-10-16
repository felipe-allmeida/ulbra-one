using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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

namespace Ulbra.One.Finance.Application.Endpoints.Payables.TaxAnalysis.ConfirmTaxAnalysis;

/// <summary>The Tributário confirms the análise tributária of a PendingTaxAnalysis payable ("Liberar para pagamento"):
/// after validating the retenções, releases it to payment and books the accounting incurral. This is the blocking
/// gate for Serviço/Conta Fixa titles — until it runs, the payable can be neither paid (payment requires Scheduled)
/// nor posted (PayableScheduled isn't published). Hard-gated on a registered Nota Fiscal. Emits
/// <see cref="PayableScheduled"/> exactly like <see cref="CompleteAccounting.CompleteAccounting"/> (shared factory).</summary>
public sealed class ConfirmTaxAnalysis : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/tax-analysis/confirm", Handle).WithName("ConfirmPayableTaxAnalysis").WithTags("Finance")
            .WithSummary("Confirms the tax analysis of a payable.")
            .WithDescription("The Tributário signs off on a payable's withholdings and releases it to payment, booking "
                + "the expense incurral in accounting. Hard-gated on a registered nota fiscal: without one it returns "
                + "400, no exceptions. Confirming a payable that is not pending tax analysis returns 409.")
            .RequirePermission(Permissions.PayablesTax).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // Hard gate: the fiscal document must be registered before the title is released — a parsed FiscalDocument
        // (XML) or an attachment typed as Nota Fiscal, and for Conta Fixa a Fatura also qualifies (Fatura OU NF).
        // Advance payables (adiantamento) are exempt — paid before the document exists.
        var hasFiscalDocument = await PayableFiscalGate.HasQualifyingDocumentAsync(uow, p, ct);
        if (!hasFiscalDocument && p.Origin != PayableOrigin.Advance)
            return ErrorResults.InvalidRequest("A Nota Fiscal must be registered before the payable can be released for payment.", ErrorCodes.Payable.FiscalDocumentMissing);

        try { p.CompleteTaxAnalysis(user.UserId, clock.GetUtcNow(), hasFiscalDocument); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payable.InvalidState); }

        // Supplier identification for the Livro Razão history (razão social + CNPJ). Cross-module read by the loose
        // Guid VendorId; falls back to the payable's snapshot name.
        var vendor = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == p.VendorId)
            .Select(v => new { v.Name, v.Document })
            .FirstOrDefaultAsync(ct);
        // Shared payload builder — same event CompleteAccounting + the auto-schedule publish, so they can't drift.
        await publish.Publish(PayableScheduledFactory.Build(p, vendor?.Name, vendor?.Document), ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
