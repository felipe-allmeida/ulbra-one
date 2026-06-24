using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class PayableReceivedConsumer(
    IUnitOfWork uow,
    IPayableClassifier classifier,
    TimeProvider clock,
    IPublishEndpoint publish)
    : IConsumer<PayableReceived>
{
    internal const double AutoApplyThreshold = 0.8;

    public async Task Consume(ConsumeContext<PayableReceived> context)
    {
        await ProcessAsync(context.Message.PayableId, context.CancellationToken);
    }

    /// <summary>
    /// Core logic extracted for direct testing without a broker: load payable, guard idempotency,
    /// classify, apply decision, persist.
    /// </summary>
    public async Task ProcessAsync(Guid payableId, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == payableId, ct);

        // Idempotency: only act on a payable still awaiting classification (at-least-once delivery).
        if (payable is null || payable.Status != PayableStatus.Received) return;

        var result = await classifier.ClassifyAsync(payable, ct);
        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);
        Apply(payable, result, cc?.AutoApprovalLimit ?? 0m, clock.GetUtcNow());

        if (payable.Status == PayableStatus.Scheduled)
        {
            var code = payable.AccountingAccountId is { } acc
                ? await uow.ReadSet<ChartAccount>().Where(c => c.Id == acc).Select(c => c.Code).FirstOrDefaultAsync(ct)
                : null;
            await publish.Publish(new PayableScheduled(payable.Id, payable.CompanyId, payable.TotalAmount, code), ct);
        }

        // Payable carries an xmin rowversion. If a human mutated the payable between create and async
        // consumption, SaveChanges throws DbUpdateConcurrencyException — safe under at-least-once: MassTransit
        // retries, and on retry the Status==Received guard makes it a no-op. No explicit catch needed.
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Pure decision: classify + advance the state per confidence. Public for unit tests.
    /// </summary>
    public static void Apply(Payable payable, PayableClassificationResult r, decimal autoApprovalLimit, DateTimeOffset now)
    {
        // If the payable was already classified at create-time (manual pre-classification), honour it:
        // skip the AI path, run auto-approval, and advance to PendingApproval if still Received.
        if (payable.IsClassified)
        {
            payable.ApplyAutoApproval(autoApprovalLimit, now);
            if (payable.Status == PayableStatus.Received) payable.MarkPendingApproval();
            return;
        }

        if (r.AccountingAccountId is not null && r.FinancialAccountId is not null && r.Confidence >= AutoApplyThreshold)
        {
            payable.Classify(r.AccountingAccountId, r.FinancialAccountId);
            payable.ApplyAutoApproval(autoApprovalLimit, now); // → Scheduled if under limit
            if (payable.Status == PayableStatus.Received) payable.MarkPendingApproval();
        }
        else
        {
            // 0.5–0.8 band (or heuristic with partial result): keep the suggestion for the human.
            if (r.AccountingAccountId is not null || r.FinancialAccountId is not null)
                payable.SetSuggestion(r.AccountingAccountId, r.FinancialAccountId, r.Confidence, r.Rationale);
            payable.MarkPendingClassification();
        }
    }
}
