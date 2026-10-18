using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Budgeting.Infrastructure.Messaging;

/// <summary>Feeds real payments (baixas) from Finance back into the requisition's "realizado" (AEL-793, the
/// "volta"). Only acts on requisition-sourced payables (<c>RequisitionId</c> set on the event). Idempotent:
/// <see cref="Requisition.RegisterPayment"/> dedups on the Finance PaymentId, and reversal backs the amount
/// out — both safe under at-least-once delivery.</summary>
public sealed class RequisitionPaymentConsumer(IUnitOfWork uow, TimeProvider clock, ILogger<RequisitionPaymentConsumer> logger)
    : IConsumer<PaymentRegistered>, IConsumer<PaymentReversed>
{
    public Task Consume(ConsumeContext<PaymentRegistered> context) =>
        ApplyPaymentAsync(context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<PaymentReversed> context) =>
        ApplyReversalAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ApplyPaymentAsync(PaymentRegistered m, CancellationToken ct)
    {
        if (m.RequisitionId is not { } requisitionId) return; // not tied to a requisition

        var requisition = await LoadAsync(requisitionId, ct);
        if (requisition is null) return;

        // A payment against an already-cancelled requisition (a late baixa on a Payable paid before the cancel
        // propagated) is still recorded — the money is real and must stay traceable/reversible. RegisterPayment's
        // MarkPaid is status-guarded, so no invalid transition happens; Cancelled contributes zero to the budget.
        if (requisition.Status == RequisitionStatus.Cancelled)
            logger.LogWarning("Pagamento {PaymentId} registrado contra requisição {RequisitionId} já cancelada — mantido apenas para auditoria.",
                m.PaymentId, requisitionId);

        var paymentsBefore = requisition.Payments.Count;
        var transitionsBefore = requisition.Transitions.Count;

        // Realizado tracks the GROSS-equivalent (m.GrossAmount) so it closes against the gross estimate even when
        // the payable settles net of withholdings. Fallback is null-only (never zero): a legit interest-only baixa
        // has GrossAmount 0, and falling back to m.Amount there would re-introduce the gross/net mismatch. Old
        // in-flight messages (pre-GrossAmount) deserialize the field as null and use the cash Amount.
        requisition.RegisterPayment(m.PaymentId, m.PayableId, m.GrossAmount ?? m.Amount,
            m.Competency ?? $"{m.PaymentDate:yyyy-MM}",
            new DateTimeOffset(m.PaymentDate.Year, m.PaymentDate.Month, m.PaymentDate.Day, 0, 0, 0, TimeSpan.Zero));

        // New children appended to a now-Modified parent must be persisted explicitly as Added — EF's
        // store-generated-key graph heuristic would otherwise mark them Modified (0-row UPDATE →
        // DbUpdateConcurrencyException). Same reason the esteira endpoints AddAsync their transition row.
        if (requisition.Payments.Count > paymentsBefore)
            await uow.AddAsync(requisition.Payments[^1], ct);
        if (requisition.Transitions.Count > transitionsBefore) // RegisterPayment → MarkPaid appended a transition
            await uow.AddAsync(requisition.Transitions[^1], ct);

        await uow.SaveChangesAsync(ct);
    }

    public async Task ApplyReversalAsync(PaymentReversed m, CancellationToken ct)
    {
        if (m.RequisitionId is not { } requisitionId) return;

        var requisition = await LoadAsync(requisitionId, ct);
        if (requisition is null) return;

        var transitionsBefore = requisition.Transitions.Count;
        requisition.ReversePayment(m.PaymentId, clock.GetUtcNow());
        if (requisition.Transitions.Count > transitionsBefore) // ReversePayment → ReopenForPayment appended one
            await uow.AddAsync(requisition.Transitions[^1], ct);

        await uow.SaveChangesAsync(ct);
    }

    private Task<Requisition?> LoadAsync(Guid id, CancellationToken ct) =>
        uow.Set<Requisition>()
            .Include(r => r.Payments)
            .Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
}
