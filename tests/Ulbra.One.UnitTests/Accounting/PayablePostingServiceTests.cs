using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.UnitTests.Accounting;

// ── async-queryable stub ──────────────────────────────────────────────────────

internal sealed class AcctAsyncEnumerable<T>(IEnumerable<T> enumerable)
    : EnumerableQuery<T>(enumerable), IAsyncEnumerable<T>, IQueryable<T>
{
    IQueryProvider IQueryable.Provider => new AcctAsyncQueryProvider<T>(this);
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new AcctAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
}

internal sealed class AcctAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}

internal sealed class AcctAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new AcctAsyncEnumerable<TEntity>(inner.CreateQuery<TEntity>(expression));
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new AcctAsyncEnumerable<TElement>(inner.CreateQuery<TElement>(expression));
    public object? Execute(Expression expression) => inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var method = typeof(AcctAsyncQueryProvider<TEntity>)
            .GetMethod(nameof(ExecuteSyncGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(resultType);
        var syncResult = method.Invoke(this, [expression]);
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, [syncResult])!;
    }
    private T? ExecuteSyncGeneric<T>(Expression expression) => inner.Execute<T>(expression);
}

/// <summary>Minimal IUnitOfWork backed by a mutable object list (accounting tests).</summary>
internal sealed class AcctStubUow : IUnitOfWork
{
    private readonly List<object> _store;
    public AcctStubUow(params object[] seed) => _store = [.. seed];

    public IQueryable<T> ReadSet<T>() where T : class =>
        new AcctAsyncEnumerable<T>(_store.OfType<T>().ToList());
    public IQueryable<T> Set<T>() where T : class =>
        new AcctAsyncEnumerable<T>(_store.OfType<T>().ToList());

    public Task AddAsync<T>(T entity, CancellationToken ct) where T : class
    {
        _store.Add(entity!);
        return Task.CompletedTask;
    }

    public void Remove<T>(T entity) where T : class => _store.Remove(entity!);
    public void RemoveRange<T>(IEnumerable<T> entities) where T : class
    {
        foreach (var e in entities) _store.Remove(e!);
    }
    public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
    public void ClearChangeTracker() { }

    // helpers for assertions
    public IReadOnlyList<JournalEntry> JournalEntries => _store.OfType<JournalEntry>().ToList();
    public IReadOnlyList<PendingAccountingPosting> Pendings => _store.OfType<PendingAccountingPosting>().ToList();
}

public class PayablePostingServiceTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();

    // Arbitrary codes for unit tests — not tied to RFB defaults.
    private const string ExpenseCode = "3.1.01";
    private const string LiabilityCode = "2.1.01";
    private const string CashCode = "1.1.01";

    private static readonly AccountingPostingOptions Opts =
        new(PayablesLiabilityCode: LiabilityCode, CashCode: CashCode,
            WithholdingCodes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Irrf"] = "2.1.tax",
            });

    private static LedgerAccount MakeAnalytical(Guid companyId, string code, AccountGroup group)
    {
        var acct = new LedgerAccount(companyId, code, $"Account {code}", group, LedgerAccountKind.Analytical);
        return acct;
    }

    private static (AcctStubUow uow, PayablePostingService sut) BuildSut(params object[] seed)
    {
        var uow = new AcctStubUow(seed);
        var clock = new FakeTimeProvider(FixedNow);
        var sut = new PayablePostingService(uow, clock, Opts);
        return (uow, sut);
    }

    // ── PostIncurralAsync: success path ───────────────────────────────────────

    [Fact]
    public async Task PostIncurral_posts_balanced_two_line_entry_and_no_pending()
    {
        var expense = MakeAnalytical(CompanyId, ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, ExpenseCode, [], CancellationToken.None);

        var entries = uow.JournalEntries;
        Assert.Single(entries);
        var entry = entries[0];

        // Status and key properties
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(JournalOrigin.Automatic, entry.Origin);
        Assert.Equal("payable-incurral:x", entry.SourceKey);
        Assert.Equal(1, entry.Number);
        Assert.True(entry.IsBalanced);
        Assert.Equal(2, entry.Lines.Count);

        // Debit = expense, Credit = liability
        var debitLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(expense.Id, debitLine.LedgerAccountId);
        Assert.Equal(liability.Id, creditLine.LedgerAccountId);
        Assert.Equal(100m, debitLine.Amount);
        Assert.Equal(100m, creditLine.Amount);

        // No pending
        Assert.Empty(uow.Pendings);
    }

    // ── PostIncurralAsync: idempotency ────────────────────────────────────────

    [Fact]
    public async Task PostIncurral_second_call_same_sourceKey_is_no_op()
    {
        var expense = MakeAnalytical(CompanyId, ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, ExpenseCode, [], CancellationToken.None);
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, ExpenseCode, [], CancellationToken.None);

        // Still only ONE entry — second call was idempotent
        Assert.Single(uow.JournalEntries);
        Assert.Empty(uow.Pendings);
    }

    // ── PostIncurralAsync: unknown expense code → pending ────────────────────

    [Fact]
    public async Task PostIncurral_unknown_expense_code_parks_pending_and_no_entry()
    {
        // Only seed the liability; leave the expense account absent
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:y", 200m, "9.9.99.UNKNOWN", [], CancellationToken.None);

        Assert.Empty(uow.JournalEntries);

        var pending = Assert.Single(uow.Pendings);
        Assert.Equal(CompanyId, pending.CompanyId);
        Assert.Equal("payable-incurral:y", pending.SourceKey);
        Assert.Equal(PostingKind.Incurral, pending.Kind);
        Assert.Equal(200m, pending.Amount);
        Assert.NotEqual(default, pending.CreatedAt); // Stamp was called
    }

    // ── PostIncurralAsync: null expense code → pending ────────────────────────

    [Fact]
    public async Task PostIncurral_null_expense_code_parks_pending()
    {
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:z", 50m, null, [], CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        var pending = Assert.Single(uow.Pendings);
        Assert.Equal(PostingKind.Incurral, pending.Kind);
    }

    // ── PostSettlementAsync: success path ─────────────────────────────────────

    [Fact]
    public async Task PostSettlement_posts_balanced_entry_Dr_liability_Cr_cash()
    {
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CompanyId, CashCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(liability, cash);

        await sut.PostSettlementAsync(CompanyId, "payable-settlement:x", 75m, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(JournalOrigin.Automatic, entry.Origin);
        Assert.Equal("payable-settlement:x", entry.SourceKey);
        Assert.True(entry.IsBalanced);

        var debitLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(liability.Id, debitLine.LedgerAccountId);
        Assert.Equal(cash.Id, creditLine.LedgerAccountId);
        Assert.Equal(75m, debitLine.Amount);
        Assert.Equal(75m, creditLine.Amount);

        Assert.Empty(uow.Pendings);
    }

    // ── ReverseAsync: success path ────────────────────────────────────────────

    [Fact]
    public async Task ReverseAsync_reverses_posted_entry_and_adds_reversal()
    {
        var expense = MakeAnalytical(CompanyId, ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        // First post an incurral
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:rev", 300m, ExpenseCode, [], CancellationToken.None);
        Assert.Single(uow.JournalEntries);

        // Now reverse it
        await sut.ReverseAsync(CompanyId, "payable-incurral:rev", "cancelled", CancellationToken.None);

        // Two entries total: original (now Reversed) + reversal (Posted)
        Assert.Equal(2, uow.JournalEntries.Count);
        var original = uow.JournalEntries.First(e => e.Status == JournalStatus.Reversed);
        var reversal = uow.JournalEntries.First(e => e.Status == JournalStatus.Posted);

        Assert.Equal(JournalStatus.Reversed, original.Status);
        Assert.Equal(JournalStatus.Posted, reversal.Status);
        Assert.Equal("payable-incurral:rev:reversal", reversal.SourceKey);
        Assert.True(reversal.IsBalanced);
    }

    // ── ReverseAsync: idempotency ─────────────────────────────────────────────

    [Fact]
    public async Task ReverseAsync_second_call_is_no_op()
    {
        var expense = MakeAnalytical(CompanyId, ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(CompanyId, LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:rev2", 100m, ExpenseCode, [], CancellationToken.None);
        await sut.ReverseAsync(CompanyId, "payable-incurral:rev2", "cancelled", CancellationToken.None);
        await sut.ReverseAsync(CompanyId, "payable-incurral:rev2", "cancelled again", CancellationToken.None);

        // Still only 2 entries — second reverse was no-op
        Assert.Equal(2, uow.JournalEntries.Count);
    }

    // ── ReverseAsync: no original entry → no-op ───────────────────────────────

    [Fact]
    public async Task ReverseAsync_when_no_posted_entry_exists_does_nothing()
    {
        var (uow, sut) = BuildSut();

        await sut.ReverseAsync(CompanyId, "payable-incurral:nonexistent", "reason", CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
    }
}
