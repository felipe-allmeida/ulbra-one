using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class PayableReceivedConsumer(
    IUnitOfWork uow,
    IPayableClassifier classifier,
    TimeProvider clock,
    IPublishEndpoint publish,
    IAutoWithholdingService autoWithholding)
    : IConsumer<PayableReceived>
{
    internal const double AutoApplyThreshold = 0.8;

    public async Task Consume(ConsumeContext<PayableReceived> context)
    {
        var giveUp = context.GetRedeliveryCount() >= ClassificationRetry.MaxRedeliveries;
        await ProcessAsync(context.Message.PayableId, giveUp, context.CancellationToken);
    }

    /// <summary>
    /// Core logic extracted for direct testing without a broker: load payable, guard idempotency,
    /// classify, apply decision, persist. Callers that don't pass giveUpIfUnclassified always settle
    /// (equivalent to the final retry attempt), preserving backwards compatibility.
    /// </summary>
    public Task ProcessAsync(Guid payableId, CancellationToken ct) =>
        ProcessAsync(payableId, giveUpIfUnclassified: true, ct);

    /// <summary>
    /// Full 3-arg variant. When <paramref name="giveUpIfUnclassified"/> is <c>false</c> and
    /// classification yields no suggestion, throws <see cref="ClassificationUnavailableException"/>
    /// so MassTransit redelivers with the escalating schedule. When <c>true</c> (final attempt or
    /// direct call) it settles the payable into PendingClassification instead.
    /// </summary>
    public async Task ProcessAsync(Guid payableId, bool giveUpIfUnclassified, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == payableId, ct);

        // Idempotency: only act on a payable still awaiting classification (at-least-once delivery).
        if (payable is null || payable.Status != PayableStatus.Received) return;

        // Withholding doesn't depend on classification — compute + persist it before the (slow) classifier.
        await autoWithholding.RecalculateAndApplyAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        var result = await classifier.ClassifyAsync(payable, ct);

        // Nothing to go on (no AI suggestion, no vendor history). Retry with escalating delays until data
        // or the AI is available — unless this is the final attempt, where we settle for a human below.
        var hasSuggestion = result.AccountingAccountId is not null;
        if (!payable.IsClassified && !hasSuggestion && !giveUpIfUnclassified)
            throw new ClassificationUnavailableException(payableId);

        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);

        Apply(payable, result, cc?.AutoApprovalLimit ?? 0m, clock.GetUtcNow());

        if (payable.Status == PayableStatus.Scheduled)
        {
            var code = payable.AccountingAccountId is { } acc
                ? await uow.ReadSet<ChartAccount>().Where(c => c.Id == acc).Select(c => c.Code).FirstOrDefaultAsync(ct)
                : null;
            var withholdings = payable.Withholdings.Select(w => new ScheduledWithholding(w.TaxType.ToString(), w.Amount)).ToList();
            await publish.Publish(new PayableScheduled(payable.Id, payable.CompanyId, payable.TotalAmount, code, withholdings), ct);
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

        if (r.AccountingAccountId is not null && r.Confidence >= AutoApplyThreshold)
        {
            payable.Classify(r.AccountingAccountId);
            payable.ApplyAutoApproval(autoApprovalLimit, now); // → Scheduled if under limit
            if (payable.Status == PayableStatus.Received) payable.MarkPendingApproval();
        }
        else
        {
            // 0.5–0.8 band (or heuristic): keep the suggestion for the human.
            if (r.AccountingAccountId is not null)
                payable.SetSuggestion(r.AccountingAccountId, r.Confidence, r.Rationale);
            payable.MarkPendingClassification();
        }
    }
}
