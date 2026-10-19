using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class PayableReceivedConsumer(
    IUnitOfWork uow,
    IPayableClassifier classifier,
    TimeProvider clock,
    IAutoWithholdingService autoWithholding,
    IPublishEndpoint publish,
    FinanceMessagingOptions options)
    : IConsumer<PayableReceived>
{
    internal const double AutoApplyThreshold = 0.8;

    public Task Consume(ConsumeContext<PayableReceived> context) =>
        ProcessAsync(context.Message.PayableId, context.CancellationToken);

    /// <summary>
    /// Load → idempotency guard → withholding → classify → apply → persist. A payable is GUARANTEED to leave
    /// Received: <see cref="Apply"/> either auto-classifies (→ PendingApproval/PendingAccounting) or, when the
    /// classifier can't determine an account (no AI suggestion, provider down, or a rate-limit), settles it into
    /// PendingClassification for a human. There is deliberately NO throw-to-retry here: relying on delayed
    /// redelivery could leave a payable stuck in intake on a broker that can't schedule it (and retrying a
    /// down/rate-limited AI only burns cost). Genuine transient faults (e.g. an xmin concurrency race with a
    /// human edit) are covered by the consumer's immediate message-retry (see the definition), where the
    /// Status==Received guard makes a retry a no-op.
    /// </summary>
    public async Task ProcessAsync(Guid payableId, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == payableId, ct);

        // Idempotency: only act on a payable still awaiting classification (at-least-once delivery).
        if (payable is null || payable.Status != PayableStatus.Received) return;

        // Withholding doesn't depend on classification — compute + persist it before the (slow) classifier.
        await autoWithholding.RecalculateAndApplyAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        var result = await classifier.ClassifyAsync(payable, ct);
        var now = clock.GetUtcNow();

        // Requisition-sourced (AEL-846): already approved upstream (Approver) → skip the Finance gates
        // and schedule directly. Type is always set here; if null, fall through to the manual Apply.
        if (options.AutoScheduleRequisitionPayables
            && payable.Origin == PayableOrigin.Purchase
            && payable.Type is not null)
        {
            await AutoScheduleAsync(payable, result, now, ct);
        }
        else
        {
            var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);
            // Always advances the payable out of Received — never leaves it stuck in intake.
            Apply(payable, result, cc?.AutoApprovalLimit ?? 0m, now);
        }

        // xmin rowversion: a concurrent mutation throws DbUpdateConcurrencyException — safe under at-least-once
        // (retry is a no-op via the Status==Received guard). Any PayableScheduled above commits with this save.
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Auto-advances a requisition payable Received → Scheduled and publishes PayableScheduled.
    /// ApprovedBy/AccountedBy = "system:req:{requisition}" (traceable to the Approver approval).</summary>
    private async Task AutoScheduleAsync(Payable payable, PayableClassificationResult result, DateTimeOffset now, CancellationToken ct)
    {
        var actor = $"system:req:{payable.OriginRequisitionId}";

        // Never auto-apply the ledger account: the AI candidate is only a SUGGESTION for Accounting's review. The
        // payable schedules with AccountingAccountId = null, so the incurral parks a PendingAccountingPosting that
        // is resolved when the accountant classifies it (→ PayableReclassified → the parked incurral is booked).
        if (result.AccountingAccountId is { } accountId)
        {
            var rationale = string.IsNullOrEmpty(result.Source) ? result.Rationale : $"[{result.Source}] {result.Rationale}";
            payable.SetSuggestion(accountId, result.Confidence, rationale);
        }

        payable.Approve(actor, now);                                                  // → PendingAccounting (or PendingTaxAnalysis)

        // Tax analysis (Serviço/Conta Fixa): Approve routed it to PendingTaxAnalysis. PARK here — the Tax role
        // validates the withholdings and releases it (POST /payables/{id}/tax-analysis/confirm), which schedules it
        // and publishes PayableScheduled. Do NOT auto-complete/schedule: this is the blocking gate before payment + posting.
        // Subsumes the Conta Fixa NF-park below (every Conta Fixa requires tax analysis); the fiscal document is now
        // required within the tax stage.
        if (payable.Status == PayableStatus.PendingTaxAnalysis) return;

        // Conta Fixa (AEL-881): the invoice must carry its fiscal document before it can be scheduled/paid. When it
        // isn't in yet, PARK it in PendingAccounting instead of auto-completing — the fiscal document upload +
        // CompleteAccounting release it to Scheduled. Same gate as CompleteAccounting/ConfirmTaxAnalysis: a parsed
        // FiscalDocument OR a Nota Fiscal attachment, and for Conta Fixa a Fatura also qualifies (Fatura OU NF).
        if (payable.RequiresFiscalDocument && !await PayableFiscalGate.HasQualifyingDocumentAsync(uow, payable, ct))
            return;

        payable.CompleteAccounting(actor, now, hasFiscalDocument: false, allowWithoutFiscalDocument: true); // → Scheduled

        // Vendor snapshot (legal name + CNPJ) for the Ledger history — same cross-module read the endpoint does.
        var vendor = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId)
            .Select(v => new { v.Name, v.Document })
            .FirstOrDefaultAsync(ct);
        // Publish before SaveChanges — the consumer EF outbox (AEL-845) commits the event with the business row.
        await publish.Publish(PayableScheduledFactory.Build(payable, vendor?.Name, vendor?.Document), ct);
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
            payable.ApplyAutoApproval(autoApprovalLimit, now); // → PendingAccounting if under limit
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
