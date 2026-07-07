using System.Linq.Expressions;
using MassTransit;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

// ── async-queryable stub (mirrors PayableClassifierTests harness) ─────────────

internal sealed class PmtAsyncEnumerable<T>(IEnumerable<T> enumerable)
    : EnumerableQuery<T>(enumerable), IAsyncEnumerable<T>, IQueryable<T>
{
    IQueryProvider IQueryable.Provider => new PmtAsyncQueryProvider<T>(this);
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new PmtAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
}

internal sealed class PmtAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}

internal sealed class PmtAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new PmtAsyncEnumerable<TEntity>(inner.CreateQuery<TEntity>(expression));
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new PmtAsyncEnumerable<TElement>(inner.CreateQuery<TElement>(expression));
    public object? Execute(Expression expression) => inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var method = typeof(PmtAsyncQueryProvider<TEntity>)
            .GetMethod(nameof(ExecuteSyncGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(resultType);
        var syncResult = method.Invoke(this, [expression]);
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, [syncResult])!;
    }
    private T? ExecuteSyncGeneric<T>(Expression expression) => inner.Execute<T>(expression);
}

/// <summary>No-op IPublishEndpoint for unit tests that don't need to assert on published messages.</summary>
internal sealed class NoOpPublishEndpoint : IPublishEndpoint
{
    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
    public Task Publish(object message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
}

/// <summary>Minimal IUnitOfWork backed by an in-memory object array (payment tests).</summary>
internal sealed class PmtStubUow : IUnitOfWork
{
    private readonly object[] _entities;
    public PmtStubUow(params object[] entities) => _entities = entities;
    public IQueryable<T> ReadSet<T>() where T : class =>
        new PmtAsyncEnumerable<T>(_entities.OfType<T>());
    public IQueryable<T> Set<T>() where T : class =>
        new PmtAsyncEnumerable<T>(_entities.OfType<T>());
    public Task AddAsync<T>(T entity, CancellationToken ct) where T : class => Task.CompletedTask;
    public void Remove<T>(T entity) where T : class { }
    public void RemoveRange<T>(IEnumerable<T> entities) where T : class { }
    public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
    public void ClearChangeTracker() { }
}

public class PayablePaymentServiceTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    /// <summary>
    /// Builds a classified+scheduled payable with one open installment of <paramref name="amount"/>.
    /// ClassificationGate is bypassed because <see cref="StubUow"/> returns no <c>ClassificationPolicy</c>
    /// (policy is null → gate returns false = classification not required).
    /// </summary>
    private static (Payable payable, Installment installment) NewScheduledPayable(decimal amount = 100m)
    {
        var payable = new Payable(CompanyId, PayableOrigin.Manual, Guid.NewGuid(), "Acme Ltda",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Test payable",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(amount, 1, new DateOnly(2026, 7, 10), 1));

        payable.Classify(Guid.NewGuid());
        payable.Approve("test-user", FixedNow);

        var installment = payable.Installments[0];
        return (payable, installment);
    }

    private static RegisterPaymentInput ValidInput(decimal amount = 100m) =>
        new(new DateOnly(2026, 7, 1), amount, BankAccountId, PaymentMethod.Ted,
            Interest: 0, Fine: 0, Discount: 0, Notes: null);

    /// <summary>
    /// Stub uow that has a BankAccount belonging to <see cref="CompanyId"/>.
    /// No ClassificationPolicy → gate passes (no classification required).
    /// </summary>
    private static PmtStubUow UowWithBankAccount()
    {
        var bankAccount = CreateBankAccount(BankAccountId, CompanyId);
        return new PmtStubUow(bankAccount);
    }

    private static BankAccount CreateBankAccount(Guid id, Guid companyId)
    {
        // BankAccount has a private constructor; use reflection to set Id and CompanyId.
        var account = (BankAccount)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(BankAccount));
        typeof(BankAccount).GetProperty(nameof(BankAccount.Id))!.SetValue(account, id);
        typeof(BankAccount).GetProperty(nameof(BankAccount.CompanyId))!.SetValue(account, companyId);
        return account;
    }

    private static PayablePaymentService CreateSut(IUnitOfWork uow)
    {
        var clock = new FakeTimeProvider(FixedNow);
        return new PayablePaymentService(uow, clock, new NoOpPublishEndpoint());
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Full_payment_settles_installment_and_returns_paid_payable()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);

        var result = await sut.RegisterAsync(payable, installment, ValidInput(100m), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayableStatus.Paid, result.Value.Status);
        Assert.Equal(InstallmentStatus.Paid, installment.Status);
        Assert.Single(installment.Payments);
    }

    [Fact]
    public async Task Partial_payment_leaves_installment_partially_paid_and_payable_scheduled()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);

        var result = await sut.RegisterAsync(payable, installment, ValidInput(60m), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayableStatus.Scheduled, result.Value.Status);
        Assert.Equal(InstallmentStatus.PartiallyPaid, installment.Status);
    }

    [Fact]
    public async Task Overpay_returns_failure_and_does_not_register_payment()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);

        var result = await sut.RegisterAsync(payable, installment, ValidInput(150m), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.Overpay", result.Error.Code);
        Assert.Empty(installment.Payments);
    }

    [Fact]
    public async Task Zero_amount_returns_failure()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);
        var input = new RegisterPaymentInput(new DateOnly(2026, 7, 1), 0m, BankAccountId, PaymentMethod.Ted, 0, 0, 0, null);

        var result = await sut.RegisterAsync(payable, installment, input, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.AmountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Negative_interest_returns_failure()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);
        var input = new RegisterPaymentInput(new DateOnly(2026, 7, 1), 100m, BankAccountId, PaymentMethod.Ted, -1m, 0, 0, null);

        var result = await sut.RegisterAsync(payable, installment, input, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.AmountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Non_payable_status_returns_failure()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        // Cancel the payable to make it non-payable.
        payable.Cancel(reason: "test");
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);

        var result = await sut.RegisterAsync(payable, installment, ValidInput(100m), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payable.NotPayable", result.Error.Code);
    }

    [Fact]
    public async Task Invalid_bank_account_returns_failure()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        // Use a different BankAccountId that is not in the uow.
        var uow = new PmtStubUow(); // no bank accounts
        var sut = CreateSut(uow);

        var result = await sut.RegisterAsync(payable, installment, ValidInput(100m), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.BankAccountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task CreatedById_is_stamped_on_payment()
    {
        var (payable, installment) = NewScheduledPayable(amount: 100m);
        var uow = UowWithBankAccount();
        var sut = CreateSut(uow);
        var actorId = SystemActor.Id;

        var result = await sut.RegisterAsync(payable, installment, ValidInput(100m), actorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actorId, installment.Payments[0].CreatedById);
    }
}
