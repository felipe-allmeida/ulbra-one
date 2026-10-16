using MassTransit;
using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>AEL-846: a requisition-sourced (Origin=Purchase) payable auto-advances Received → Scheduled inside
/// the consumer, stamping "system:req:{requisition}" and publishing PayableScheduled exactly once. Uses the
/// consumer's directly-testable ProcessAsync (no broker) with in-memory fakes.</summary>
public class PayableAutoScheduleConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Records every published message so tests can assert type + count.</summary>
    private sealed class RecordingPublish : IPublishEndpoint
    {
        public List<object> Messages { get; } = [];
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
    }

    private sealed class NoOpWithholding : IAutoWithholdingService
    {
        public Task RecalculateAndApplyAsync(Payable payable, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubClassifier(PayableClassificationResult result) : IPayableClassifier
    {
        public Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct) => Task.FromResult(result);
    }

    private static Payable PurchasePayable()
    {
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Purchase, Guid.NewGuid(), "ACME Ltda",
            documentNumber: null, issueDate: new DateOnly(2026, 7, 1), description: "Requisição 42",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(1200m, 3, new DateOnly(2026, 8, 10), 1));
        p.SetOrigin(Guid.NewGuid(), Guid.NewGuid());
        p.SetType(PayableType.Service); // requisition type is always mapped onto the payable
        return p;
    }

    [Fact]
    public async Task Purchase_with_candidate_account_auto_schedules_with_suggestion_only()
    {
        var p = PurchasePayable();
        var uow = new PmtStubUow(p);
        var account = Guid.NewGuid();
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(account, 0.95, "conta X", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.StartsWith("system:req:", p.ApprovedBy);
        Assert.StartsWith("system:req:", p.AccountedBy);
        // The AI candidate is never auto-applied — only kept as a suggestion for Accounting's review.
        Assert.Null(p.AccountingAccountId);
        Assert.Equal(account, p.SuggestedAccountingAccountId);
        Assert.Equal(0.95, p.ClassificationConfidence);
        Assert.Equal("[ai] conta X", p.ClassificationRationale);
        // The incurral therefore schedules unclassified and parks a PendingAccountingPosting downstream.
        var scheduled = Assert.Single(publish.Messages.OfType<PayableScheduled>());
        Assert.Equal(p.Id, scheduled.PayableId);
        Assert.Null(scheduled.AccountingAccountId);
    }

    [Fact]
    public async Task Purchase_without_candidate_still_schedules_with_null_account()
    {
        var p = PurchasePayable();
        var uow = new PmtStubUow(p);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(null, 0, "none", "none")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Null(p.AccountingAccountId); // no candidate → incurral parks a PendingAccountingPosting downstream
        var scheduled = Assert.Single(publish.Messages.OfType<PayableScheduled>());
        Assert.Null(scheduled.AccountingAccountId);
    }

    [Fact]
    public async Task Redelivery_after_scheduled_is_a_noop()
    {
        var p = PurchasePayable();
        var uow = new PmtStubUow(p);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.9, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);
        var afterFirst = publish.Messages.OfType<PayableScheduled>().Count();
        await consumer.ProcessAsync(p.Id, CancellationToken.None); // redelivery

        Assert.Equal(1, afterFirst);
        Assert.Single(publish.Messages.OfType<PayableScheduled>()); // guard (Status != Received) makes it a no-op
    }

    [Fact]
    public async Task Flag_off_falls_through_to_manual_esteira()
    {
        var p = PurchasePayable();
        var uow = new PmtStubUow(p);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.95, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = false });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        // Manual path: auto-approval limit is 0 (no CostCenter served) → parks in PendingApproval, no schedule event.
        Assert.Equal(PayableStatus.PendingApproval, p.Status);
        Assert.Empty(publish.Messages.OfType<PayableScheduled>());
    }

    [Fact]
    public async Task FixedExpense_without_fiscal_document_parks_in_pending_accounting()
    {
        // AEL-881 (R3): a Conta Fixa requires the fiscal document per invoice — without a fiscal document the payable does NOT auto-schedule;
        // it approves (the Approver already approved) and STOPS at PendingAccounting (the Accounting "pending fiscal document" tab).
        var p = PurchasePayable();
        p.RequireFiscalDocument();
        var uow = new PmtStubUow(p); // no FiscalDocument or PayableAttachment in the set
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.95, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.Equal(PayableStatus.PendingAccounting, p.Status); // parked awaiting the fiscal document
        Assert.Empty(publish.Messages.OfType<PayableScheduled>()); // nothing scheduled yet
    }

    [Fact]
    public async Task FixedExpense_with_fiscal_document_auto_schedules()
    {
        // With the fiscal document present (here an attached document, exactly like CompleteAccounting's gate), the Conta Fixa
        // payable auto-schedules as usual — the lock only holds while the fiscal document is missing.
        var p = PurchasePayable();
        p.RequireFiscalDocument();
        var nf = new PayableAttachment(p.Id, "nota-fiscal.pdf", "application/pdf", 128, "key", Guid.NewGuid(), Now, PayableAttachmentKind.FiscalDocument);
        var uow = new PmtStubUow(p, nf);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.95, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Single(publish.Messages.OfType<PayableScheduled>());
    }

    [Fact]
    public async Task Tax_title_parks_in_pending_tax_analysis_without_scheduling()
    {
        // Serviço/Conta Fixa (RequiresTaxAnalysis): after the Approver's approval the title enters the blocking
        // análise tributária stage — it does NOT auto-schedule and publishes NO PayableScheduled (so accounting can't
        // post and Finance can't pay) until the Tributário releases it.
        var p = PurchasePayable();
        p.RequireTaxAnalysis();
        var uow = new PmtStubUow(p);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.95, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.Equal(PayableStatus.PendingTaxAnalysis, p.Status);
        Assert.Empty(publish.Messages.OfType<PayableScheduled>());
    }

    [Fact]
    public async Task Purchase_without_type_falls_through_to_manual_esteira()
    {
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Purchase, Guid.NewGuid(), "ACME",
            documentNumber: null, issueDate: new DateOnly(2026, 7, 1), description: "no type",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(300m, 1, new DateOnly(2026, 8, 1), 1));
        p.SetOrigin(Guid.NewGuid(), Guid.NewGuid()); // Type deliberately left null
        var uow = new PmtStubUow(p);
        var publish = new RecordingPublish();
        var consumer = new PayableReceivedConsumer(uow, new StubClassifier(new(Guid.NewGuid(), 0.95, "r", "ai")),
            new FakeTimeProvider(Now), new NoOpWithholding(), publish,
            new FinanceMessagingOptions { AutoScheduleRequisitionPayables = true });

        await consumer.ProcessAsync(p.Id, CancellationToken.None);

        Assert.NotEqual(PayableStatus.Scheduled, p.Status); // undetermined type → never auto-scheduled
        Assert.Empty(publish.Messages.OfType<PayableScheduled>());
    }
}
