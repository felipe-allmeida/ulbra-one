using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.CompleteAccounting;

/// <summary>Accountant confirms the accounting review of a PendingAccounting payable ("Contabilizar"):
/// verifies retentions/vendor data, then releases it to payment. Hard-gated on a registered Nota Fiscal.
/// This is the sole emission point of <see cref="PayableScheduled"/> — the accounting incurral is booked
/// here, when the payable actually becomes Scheduled.</summary>
public sealed class CompleteAccounting : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/accounting/confirm", Handle).WithName("CompletePayableAccounting").WithTags("Finance")
            .RequireAuthorization().Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        // Hard gate: the Nota Fiscal must be registered before accounting completes — either a parsed
        // FiscalDocument (XML) or an attached NF document (PayableAttachment, the Pendências attach flow).
        // Both are separate from the aggregate, so the check lives here (cf. ClassificationGate).
        // Advance payables (adiantamento) are exempt — they are paid before the NF exists.
        var hasFiscalDocument = await uow.ReadSet<FiscalDocument>().AnyAsync(d => d.PayableId == p.Id, ct)
            || await uow.ReadSet<PayableAttachment>().AnyAsync(a => a.PayableId == p.Id, ct);
        if (!hasFiscalDocument && p.Origin != PayableOrigin.Advance)
            return TypedResults.Problem(
                detail: "A Nota Fiscal must be registered before the payable can be sent to payment.",
                statusCode: 400, title: "Payable.FiscalDocumentMissing");

        try { p.CompleteAccounting(user.UserId, clock.GetUtcNow(), hasFiscalDocument); }
        catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 409, title: "Payable.InvalidState"); }

        var withholdings = p.Withholdings.Select(w => new ScheduledWithholding(w.TaxType.ToString(), w.Amount)).ToList();
        await publish.Publish(new PayableScheduled(p.Id, p.CompanyId, p.TotalAmount, p.AccountingAccountId, withholdings), ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
