using MassTransit;
using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Finance.Application.Endpoints.Conciliation;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>Records the messages published so a test can assert on them (the No-Op endpoint discards them).</summary>
internal sealed class RecordingPublishEndpoint : IPublishEndpoint
{
    public List<object> Published { get; } = [];

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    { Published.Add(message); return Task.CompletedTask; }
    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    { Published.Add(message); return Task.CompletedTask; }
    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    { Published.Add(message); return Task.CompletedTask; }
    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish(object message, CancellationToken cancellationToken = default) { Published.Add(message); return Task.CompletedTask; }
    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Published.Add(message); return Task.CompletedTask; }
    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) { Published.Add(message); return Task.CompletedTask; }
    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Published.Add(message); return Task.CompletedTask; }
    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
}

public class ConciliationBaixaTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BankAccountId = Guid.NewGuid();

    private static (Payable payable, Installment installment) NewScheduledPayable(decimal amount)
    {
        var payable = new Payable(CompanyId, PayableOrigin.Manual, Guid.NewGuid(), "Acme Ltda",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Test payable",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(amount, 1, new DateOnly(2026, 7, 10), 1));
        payable.Classify(Guid.NewGuid());
        payable.Approve("test-user", FixedNow);
        payable.CompleteAccounting("test-user", FixedNow, hasFiscalDocument: true);
        return (payable, payable.Installments[0]);
    }

    private static BankAccount CreateBankAccount()
    {
        var account = (BankAccount)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(BankAccount));
        typeof(BankAccount).GetProperty(nameof(BankAccount.Id))!.SetValue(account, BankAccountId);
        typeof(BankAccount).GetProperty(nameof(BankAccount.CompanyId))!.SetValue(account, CompanyId);
        return account;
    }

    private static BankTransaction DebitTxn(decimal amount) =>
        new(Guid.NewGuid(), CompanyId, BankAccountId, "OFX-1", new DateOnly(2026, 7, 10), -amount, "PIX PAGFOR", FixedNow);

    [Fact]
    public async Task Reconcile_registers_payment_publishes_PaymentRegistered_and_reconciles_txn()
    {
        var (payable, installment) = NewScheduledPayable(100m);
        var uow = new PmtStubUow(payable, CreateBankAccount());
        var publish = new RecordingPublishEndpoint();
        var service = new PayablePaymentService(uow, new FakeTimeProvider(FixedNow), publish);
        var txn = DebitTxn(100m);

        var result = await ConciliationBaixa.ReconcileAsync(txn, installment.Id, Guid.NewGuid().ToString(), uow, service, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<Ulbra.One.Finance.Application.Contracts.BankTransactionResponse>>(result);
        // The whole point of AEL-847: an OFX-reconciled baixa now emits the settlement event (accounting + realizado).
        var settlement = Assert.Single(publish.Published.OfType<PaymentRegistered>());
        Assert.Equal(payable.Id, settlement.PayableId);
        Assert.Equal(100m, settlement.Amount);
        Assert.Equal(MatchStatus.Reconciled, txn.MatchStatus);
        Assert.Equal(InstallmentStatus.Paid, installment.Status);
        Assert.Equal(txn.MatchedPaymentId, installment.Payments.Single().Id);
    }

    [Fact]
    public async Task Reconcile_amount_mismatch_does_not_register_or_publish()
    {
        var (payable, installment) = NewScheduledPayable(100m);
        var uow = new PmtStubUow(payable, CreateBankAccount());
        var publish = new RecordingPublishEndpoint();
        var service = new PayablePaymentService(uow, new FakeTimeProvider(FixedNow), publish);
        var txn = DebitTxn(80m); // does not equal the 100 remaining

        var result = await ConciliationBaixa.ReconcileAsync(txn, installment.Id, Guid.NewGuid().ToString(), uow, service, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Empty(publish.Published.OfType<PaymentRegistered>());
        Assert.Empty(installment.Payments);
        Assert.NotEqual(MatchStatus.Reconciled, txn.MatchStatus);
    }
}
