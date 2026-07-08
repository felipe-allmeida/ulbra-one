using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

// ── async queryable helpers (required for EF ToListAsync / FirstOrDefaultAsync) ──

internal sealed class TestAsyncEnumerable<T>(IEnumerable<T> enumerable)
    : EnumerableQuery<T>(enumerable), IAsyncEnumerable<T>, IQueryable<T>
{
    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
}

internal sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}

internal sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new TestAsyncEnumerable<TEntity>(inner.CreateQuery<TEntity>(expression));

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new TestAsyncEnumerable<TElement>(inner.CreateQuery<TElement>(expression));

    public object? Execute(Expression expression) => inner.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        // TResult is Task<TElem>; unwrap the element type, execute sync, wrap in Task.
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var method = typeof(TestAsyncQueryProvider<TEntity>)
            .GetMethod(nameof(ExecuteSyncGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(resultType);
        var syncResult = method.Invoke(this, [expression]);
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, [syncResult])!;
    }

    private T? ExecuteSyncGeneric<T>(Expression expression) => inner.Execute<T>(expression);
}

public class PayableClassifierTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static readonly Guid AccId = Guid.NewGuid();

    // Helper to build a LedgerAccount with a specific Id via reflection (private set).
    private static LedgerAccount AccountWithId(Guid id, string code, string name)
    {
        var account = new LedgerAccount(code, name, AccountGroup.Expense, LedgerAccountKind.Analytical);
        typeof(LedgerAccount).GetProperty(nameof(LedgerAccount.Id))!
            .SetValue(account, id);
        return account;
    }

    private static Payable NewPayable() =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "Acme Cloud Ltda",
            documentNumber: "NF-123", issueDate: new DateOnly(2026, 7, 1),
            description: "Cloud hosting subscription",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(1234.56m, 1, new DateOnly(2026, 7, 10), 1));

    // ── stubs ─────────────────────────────────────────────────────────────────

    /// <summary>AI stub that always succeeds with the given confidence and account code.</summary>
    private sealed class StubAi(double confidence, string? accCode) : IAiCompletion
    {
        public Task<Result<T>> CompleteAsync<T>(AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
            where T : class
        {
            object result = Activator.CreateInstance(typeof(T), accCode, confidence, "stub")!;
            return Task.FromResult(Result.Success((T)result));
        }
    }

    /// <summary>AI stub that always returns a disabled failure — simulates AI off / low confidence fall-through.</summary>
    private sealed class DisabledAi : IAiCompletion
    {
        public Task<Result<T>> CompleteAsync<T>(AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
            where T : class
            => Task.FromResult(Result.Failure<T>(Error.Failure(AiErrors.Disabled, "AI provider is not configured.")));
    }

    /// <summary>
    /// Minimal IUnitOfWork that serves a fixed set of entities via an async-compatible queryable.
    /// ReadSet&lt;T&gt; filters by type; Set&lt;T&gt; and write ops are unused in the classifier.
    /// </summary>
    private sealed class StubUow : IUnitOfWork
    {
        private readonly object[] _entities;

        public StubUow(params object[] entities) => _entities = entities;

        public IQueryable<T> ReadSet<T>() where T : class =>
            new TestAsyncEnumerable<T>(_entities.OfType<T>());

        public IQueryable<T> Set<T>() where T : class =>
            new TestAsyncEnumerable<T>(_entities.OfType<T>());

        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class => Task.CompletedTask;
        public void Remove<T>(T entity) where T : class { }
        public void RemoveRange<T>(IEnumerable<T> entities) where T : class { }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public void ClearChangeTracker() { }
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task High_confidence_ai_returns_source_ai_with_resolved_account_id()
    {
        var accAccount = AccountWithId(AccId, "3.1.1", "Software expense");

        var uow = new StubUow(accAccount);
        var ai = new StubAi(confidence: 0.9, accCode: "3.1.1");
        var sut = new PayableClassifier(ai, uow);

        var result = await sut.ClassifyAsync(NewPayable(), CancellationToken.None);

        Assert.Equal("ai", result.Source);
        Assert.Equal(AccId, result.AccountingAccountId);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task Low_confidence_ai_falls_through_to_none_when_no_vendor_history()
    {
        var accAccount = AccountWithId(AccId, "3.1.1", "Software expense");

        var uow = new StubUow(accAccount); // no prior payables → no vendor history
        var ai = new StubAi(confidence: 0.3, accCode: "3.1.1"); // below threshold
        var sut = new PayableClassifier(ai, uow);

        var result = await sut.ClassifyAsync(NewPayable(), CancellationToken.None);

        Assert.Equal("none", result.Source);
        Assert.Null(result.AccountingAccountId);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task Disabled_ai_falls_through_to_heuristic_when_vendor_history_exists()
    {
        var accAccount = AccountWithId(AccId, "3.1.1", "Software expense");

        // Build a classified prior payable with the same VendorId.
        var vendorId = Guid.NewGuid();
        var priorPayable = new Payable(Guid.NewGuid(), PayableOrigin.Manual, vendorId, "Acme Cloud Ltda",
            documentNumber: "NF-001", issueDate: new DateOnly(2026, 6, 1),
            description: "Prior hosting", costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(500m, 1, new DateOnly(2026, 6, 30), 1));
        priorPayable.Classify(AccId);

        // Target payable with the same vendor.
        var targetPayable = new Payable(Guid.NewGuid(), PayableOrigin.Manual, vendorId, "Acme Cloud Ltda",
            documentNumber: "NF-456", issueDate: new DateOnly(2026, 7, 1),
            description: "Cloud hosting subscription", costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(1234.56m, 1, new DateOnly(2026, 7, 10), 1));

        var uow = new StubUow(accAccount, priorPayable);
        var sut = new PayableClassifier(new DisabledAi(), uow);

        var result = await sut.ClassifyAsync(targetPayable, CancellationToken.None);

        Assert.Equal("heuristic", result.Source);
        Assert.Equal(AccId, result.AccountingAccountId);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task Disabled_ai_and_no_vendor_history_returns_source_none()
    {
        var uow = new StubUow(); // empty — no chart accounts, no prior payables
        var sut = new PayableClassifier(new DisabledAi(), uow);

        var result = await sut.ClassifyAsync(NewPayable(), CancellationToken.None);

        Assert.Equal("none", result.Source);
        Assert.Null(result.AccountingAccountId);
        Assert.Equal(0, result.Confidence);
    }
}
