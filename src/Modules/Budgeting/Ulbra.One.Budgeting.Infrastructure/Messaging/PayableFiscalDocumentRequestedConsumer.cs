using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Budgeting.Infrastructure.Messaging;

/// <summary>Handles a "Solicitar NF" request (Finance can't read Budgeting entities). Two effects, both
/// idempotent under at-least-once delivery: (1) moves the requisition <c>InPayment → AwaitingFiscalDocument</c>
/// so the "NF pendente" pendency lights up even when the requester already attached other documents (AEL-871) —
/// guarded on the current status, so a redelivery (already AwaitingFiscalDocument) or a late request on a
/// Paid/Finalized requisition is a no-op; (2) resolves the requester and re-publishes a notification-ready event.
/// No-ops silently when the requisition is gone or its creator is not a real user ("system"/empty).</summary>
public sealed class PayableFiscalDocumentRequestedConsumer(IUnitOfWork uow, IPublishEndpoint publish)
    : IConsumer<PayableFiscalDocumentRequested>
{
    public Task Consume(ConsumeContext<PayableFiscalDocumentRequested> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(PayableFiscalDocumentRequested m, CancellationToken ct)
    {
        // Tracked load WITH transitions — we may append a transition below (see the AddAsync note).
        var req = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == m.RequisitionId, ct);
        if (req is null) return;

        // Reflect the request on the requisition: only InPayment → AwaitingFiscalDocument is legal, so the
        // guard makes this safe/idempotent (redelivery or a request on an already-advanced requisition skips).
        if (req.Status == RequisitionStatus.InPayment)
        {
            var transitionsBefore = req.Transitions.Count;
            req.MarkAwaitingFiscalDocument(SystemActor, m.OccurredAt);
            // A child appended to a now-Modified parent must be persisted explicitly as Added — EF's
            // store-generated-key heuristic would otherwise mark it Modified (0-row UPDATE →
            // DbUpdateConcurrencyException). Same pattern as RequisitionPaymentConsumer.
            if (req.Transitions.Count > transitionsBefore)
                await uow.AddAsync(req.Transitions[^1], ct);
            await uow.SaveChangesAsync(ct);
        }

        if (!Guid.TryParse(req.CreatedBy, out var requesterId)) return;

        await publish.Publish(
            new RequisitionFiscalDocumentRequested(m.RequestId, m.RequisitionId, m.CompanyId, m.Code ?? req.Code, requesterId, m.OccurredAt),
            ct);
    }

    /// <summary>Matches <c>Requisition.SystemActor</c> — a payment-driven transition with no human behind it.</summary>
    private static readonly Guid SystemActor = Guid.Empty;
}
