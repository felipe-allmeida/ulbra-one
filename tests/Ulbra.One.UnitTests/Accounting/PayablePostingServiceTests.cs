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

    private static LedgerAccount MakeAnalytical(string code, AccountGroup group)
    {
        var acct = new LedgerAccount(code, $"Account {code}", group, LedgerAccountKind.Analytical);
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
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-1001", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entries = uow.JournalEntries;
        Assert.Single(entries);
        var entry = entries[0];

        // Status and key properties
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(JournalOrigin.Automatic, entry.Origin);
        Assert.Equal("payable-incurral:x", entry.SourceKey);
        // Documento (human-facing) = the payable's document number, never the idempotency key.
        Assert.Equal("NF-1001", entry.SourceReference);
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
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:x", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        // Still only ONE entry — second call was idempotent
        Assert.Single(uow.JournalEntries);
        Assert.Empty(uow.Pendings);
    }

    // ── PostIncurralAsync: unknown expense account → pending ────────────────────

    [Fact]
    public async Task PostIncurral_unknown_expense_account_parks_pending_and_no_entry()
    {
        // Only seed the liability; leave the expense account absent
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:y", 200m, Guid.NewGuid(), [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

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
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:z", 50m, null, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        var pending = Assert.Single(uow.Pendings);
        Assert.Equal(PostingKind.Incurral, pending.Kind);
    }

    // ── PostSettlementAsync: success path ─────────────────────────────────────

    [Fact]
    public async Task PostSettlement_posts_balanced_entry_Dr_liability_Cr_cash()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(liability, cash);

        await sut.PostSettlementAsync(CompanyId, "payable-settlement:x", 75m, new DateOnly(2026, 7, 1), cashAccountId: null, withholdings: [], unitId: null, documentRef: "NF-2002", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(JournalOrigin.Automatic, entry.Origin);
        Assert.Equal("payable-settlement:x", entry.SourceKey);
        Assert.Equal("NF-2002", entry.SourceReference);
        Assert.True(entry.IsBalanced);

        var debitLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(liability.Id, debitLine.LedgerAccountId);
        Assert.Equal(cash.Id, creditLine.LedgerAccountId);
        Assert.Equal(75m, debitLine.Amount);
        Assert.Equal(75m, creditLine.Amount);

        Assert.Empty(uow.Pendings);
    }

    // ── PostSettlementAsync: credits the paying bank's ledger account when mapped ──

    [Fact]
    public async Task PostSettlement_credits_mapped_bank_ledger_account()
    {
        const string BankCode = "1.1.05"; // the bank account's own ledger account (≠ the global CashCode)
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var bank = MakeAnalytical(BankCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(liability, cash, bank);

        await sut.PostSettlementAsync(CompanyId, "payment:m", 75m, new DateOnly(2026, 7, 1), cashAccountId: bank.Id, withholdings: [], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(bank.Id, creditLine.LedgerAccountId); // the mapped bank ledger, NOT the global CashCode
        Assert.Empty(uow.Pendings);
    }

    [Fact]
    public async Task PostSettlement_falls_back_to_cash_code_when_mapping_unresolvable()
    {
        // Bank's ledger id does not resolve (missing/inactive) → fall back to the global CashCode, never park.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(liability, cash);

        await sut.PostSettlementAsync(CompanyId, "payment:fb", 75m, new DateOnly(2026, 7, 1), cashAccountId: Guid.NewGuid(), withholdings: [], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(cash.Id, creditLine.LedgerAccountId); // fell back to CashCode
        Assert.Empty(uow.Pendings); // fallback, not parked
    }

    // ── PostSettlementAsync: CSRF withheld at payment ─────────────────────────

    private const string CsrfCode = "2.1.csrf"; // PIS/COFINS/CSLL collapse to one account

    private static AccountingPostingOptions CsrfOpts() =>
        new(PayablesLiabilityCode: LiabilityCode, CashCode: CashCode,
            WithholdingCodes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pis"] = CsrfCode, ["Cofins"] = CsrfCode, ["Csll"] = CsrfCode,
            });

    [Fact]
    public async Task PostSettlement_with_csrf_debits_liability_and_credits_one_summed_csrf_line_and_cash()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var csrf = MakeAnalytical(CsrfCode, AccountGroup.Liability);
        var uow = new AcctStubUow(liability, cash, csrf);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), CsrfOpts());

        // amount = cash paid to supplier (95); CSRF = 4.65 → liability debit = 99.65
        await sut.PostSettlementAsync(CompanyId, "payment:csrf", 95m, new DateOnly(2026, 7, 1), cashAccountId: null,
            withholdings: [("Pis", 0.65m), ("Cofins", 3m), ("Csll", 1m)], unitId: null, documentRef: "NF-9", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.True(entry.IsBalanced);

        var debit = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        Assert.Equal(liability.Id, debit.LedgerAccountId);
        Assert.Equal(99.65m, debit.Amount);

        var credits = entry.Lines.Where(l => l.Side == EntrySide.Credit).ToList();
        Assert.Equal(2, credits.Count); // PIS/COFINS/CSLL collapse into one summed line + cash
        Assert.Equal(95m, Assert.Single(credits, l => l.LedgerAccountId == cash.Id).Amount);
        Assert.Equal(4.65m, Assert.Single(credits, l => l.LedgerAccountId == csrf.Id).Amount);
        Assert.Empty(uow.Pendings);
    }

    [Fact]
    public async Task ReverseAsync_reverses_a_csrf_settlement_including_its_withholding_lines()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var csrf = MakeAnalytical(CsrfCode, AccountGroup.Liability);
        var uow = new AcctStubUow(liability, cash, csrf);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), CsrfOpts());

        await sut.PostSettlementAsync(CompanyId, "payment:rev-csrf", 95m, new DateOnly(2026, 7, 1), cashAccountId: null,
            withholdings: [("Pis", 0.65m), ("Cofins", 3m), ("Csll", 1m)], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        var original = Assert.Single(uow.JournalEntries);
        var lineCount = original.Lines.Count; // 1 debit + 1 merged CSRF line + cash = 3

        // Reversal reverses the whole entry by SourceKey — no special-casing of retention lines.
        await sut.ReverseAsync(CompanyId, "payment:rev-csrf", "estorno", CancellationToken.None);

        Assert.Equal(2, uow.JournalEntries.Count);
        var reversal = uow.JournalEntries.First(e => e.Status == JournalStatus.Posted);
        Assert.Equal("payment:rev-csrf:reversal", reversal.SourceKey);
        Assert.Equal(lineCount, reversal.Lines.Count);
        Assert.True(reversal.IsBalanced);
    }

    [Fact]
    public async Task PostSettlement_with_unmapped_csrf_account_parks_pending()
    {
        // CsrfOpts maps Pis/Cofins/Csll to CsrfCode, but the account is NOT seeded → parks the whole entry.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var uow = new AcctStubUow(liability, cash);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), CsrfOpts());

        await sut.PostSettlementAsync(CompanyId, "payment:csrf-miss", 95m, new DateOnly(2026, 7, 1), cashAccountId: null,
            withholdings: [("Pis", 0.65m)], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        var pending = Assert.Single(uow.Pendings);
        Assert.Equal(PostingKind.Settlement, pending.Kind);
    }

    // ── ReverseAsync: success path ────────────────────────────────────────────

    [Fact]
    public async Task ReverseAsync_reverses_posted_entry_and_adds_reversal()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        // First post an incurral (with a document number, so we can assert the reversal inherits it).
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:rev", 300m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-REV", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
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
        Assert.Equal("NF-REV", reversal.SourceReference); // reversal carries the original's document number
        Assert.True(reversal.IsBalanced);
    }

    // ── ReverseAsync: idempotency ─────────────────────────────────────────────

    [Fact]
    public async Task ReverseAsync_second_call_is_no_op()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:rev2", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
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

    // ── #7 competência ────────────────────────────────────────────────────────

    [Fact]
    public async Task PostIncurral_books_at_provided_competence()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);
        var competence = new DateOnly(2026, 3, 10);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:c", 100m, expense.Id, [],
            competenceDate: competence, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Equal(competence, uow.JournalEntries[0].CompetenceDate);
    }

    [Fact]
    public async Task PostIncurral_null_competence_falls_back_to_today()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:c2", 100m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Equal(DateOnly.FromDateTime(FixedNow.UtcDateTime), uow.JournalEntries[0].CompetenceDate);
    }

    [Fact]
    public async Task PostSettlement_books_at_payment_date()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(liability, cash);
        var paymentDate = new DateOnly(2026, 5, 20);

        await sut.PostSettlementAsync(CompanyId, "payment:c", 75m, paymentDate, cashAccountId: null, withholdings: [], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Equal(paymentDate, uow.JournalEntries[0].CompetenceDate);
    }

    // ── #6-A cost center propagation ──────────────────────────────────────────

    [Fact]
    public async Task PostIncurral_sets_cost_center_on_expense_line_only()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);
        var costCenterId = Guid.NewGuid();

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:cc", 100m, expense.Id, [],
            competenceDate: null, costCenterId: costCenterId, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = uow.JournalEntries[0];
        var debitLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        var creditLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(costCenterId, debitLine.CostCenterId);
        Assert.Null(creditLine.CostCenterId);
    }

    // ── Unit (estabelecimento) propagation — set on EVERY line so the per-unit balancete balances ──

    [Fact]
    public async Task PostIncurral_sets_unit_on_all_lines()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);
        var unitId = Guid.NewGuid();

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:unit", 100m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: unitId, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = uow.JournalEntries[0];
        Assert.All(entry.Lines, l => Assert.Equal(unitId, l.UnitId));
    }

    [Fact]
    public async Task PostIncurral_sums_retentions_sharing_one_account_into_a_single_line()
    {
        // Two on-issue taxes mapped to the SAME retention account collapse into one summed credit line.
        const string SharedTaxCode = "2.1.shared";
        var opts = new AccountingPostingOptions(LiabilityCode, CashCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Irrf"] = SharedTaxCode, ["Inss"] = SharedTaxCode });
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var shared = MakeAnalytical(SharedTaxCode, AccountGroup.Liability);
        var uow = new AcctStubUow(expense, liability, shared);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), opts);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:grp", 100m, expense.Id,
            [("Irrf", 10m), ("Inss", 5m)], competenceDate: null, costCenterId: null, unitId: null,
            documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.True(entry.IsBalanced);
        var credits = entry.Lines.Where(l => l.Side == EntrySide.Credit).ToList();
        Assert.Equal(2, credits.Count); // liability (net) + one merged retention line — NOT two retention lines
        Assert.Equal(15m, Assert.Single(credits, l => l.LedgerAccountId == shared.Id).Amount);
        Assert.Equal(85m, Assert.Single(credits, l => l.LedgerAccountId == liability.Id).Amount);
    }

    [Fact]
    public async Task PostSettlement_sets_unit_on_all_lines()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var csrf = MakeAnalytical(CsrfCode, AccountGroup.Liability);
        var uow = new AcctStubUow(liability, cash, csrf);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), CsrfOpts());
        var unitId = Guid.NewGuid();

        await sut.PostSettlementAsync(CompanyId, "payment:unit", 95m, new DateOnly(2026, 7, 1), cashAccountId: null,
            withholdings: [("Pis", 0.65m), ("Cofins", 3m), ("Csll", 1m)], unitId: unitId, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.All(entry.Lines, l => Assert.Equal(unitId, l.UnitId));
    }

    // ── #1 automatic-path clamp ───────────────────────────────────────────────

    [Fact]
    public async Task PostIncurral_clamps_competence_when_period_is_closed()
    {
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var closedMarch = new AccountingPeriod(CompanyId, 2026, 3);
        closedMarch.Close();
        var (uow, sut) = BuildSut(expense, liability, closedMarch);

        // Competence in the closed March period; today (FixedNow) is July → open.
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:clamp", 100m, expense.Id, [],
            competenceDate: new DateOnly(2026, 3, 10), costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(DateOnly.FromDateTime(FixedNow.UtcDateTime), entry.CompetenceDate); // clamped to today
        Assert.Contains("ajustada", entry.Description);
        Assert.Empty(uow.Pendings); // clamped, not parked
    }

    // ── #10 pending idempotency + resolution ──────────────────────────────────

    [Fact]
    public async Task ParkPending_is_idempotent_across_redeliveries()
    {
        // No accounts seeded → the posting parks a pending.
        var (uow, sut) = BuildSut();

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:dup", 100m, Guid.NewGuid(), [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.PostIncurralAsync(CompanyId, "payable-incurral:dup", 100m, Guid.NewGuid(), [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Single(uow.Pendings); // redelivery did not duplicate
    }

    [Fact]
    public async Task ResolvePending_replays_plan_after_account_is_added()
    {
        // Expense resolves, but the liability code is missing → parks with a replayable plan.
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var (uow, sut) = BuildSut(expense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:res", 100m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-RES", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        var pending = Assert.Single(uow.Pendings);
        Assert.Null(pending.ResolvedAt);
        Assert.Empty(uow.JournalEntries);

        // Fix the mapping: add the missing liability account, then reprocess.
        await uow.AddAsync(MakeAnalytical(LiabilityCode, AccountGroup.Liability), CancellationToken.None);
        var resolved = await sut.ResolvePendingAsync(pending, CancellationToken.None);

        Assert.True(resolved);
        Assert.NotNull(pending.ResolvedAt);
        Assert.Single(uow.JournalEntries);
        // The document number survives the park → replay round-trip (carried in the serialized plan).
        Assert.Equal("NF-RES", uow.JournalEntries[0].SourceReference);
    }

    // ── AEL-848: reclassification (reverse-then-repost with a versioned sourceKey) ──

    private const string ExpenseCodeB = "3.1.02";

    [Fact]
    public async Task Reclassify_posted_incurral_reverses_and_reposts_under_versioned_key_with_new_account()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var expenseA = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var expenseB = MakeAnalytical(ExpenseCodeB, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expenseA, expenseB, liability);

        await sut.PostIncurralAsync(CompanyId, baseKey, 100m, expenseA.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-1", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        Assert.Single(uow.JournalEntries);

        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expenseB.Id, 100m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-1", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entries = uow.JournalEntries;
        Assert.Equal(3, entries.Count); // base (reversed) + base:reversal + :r1

        var baseEntry = entries.Single(e => e.SourceKey == baseKey);
        Assert.Equal(JournalStatus.Reversed, baseEntry.Status);
        Assert.Equal(JournalStatus.Posted, entries.Single(e => e.SourceKey == baseKey + ":reversal").Status);

        var repost = entries.Single(e => e.SourceKey == baseKey + ":r1"); // versioned key sidesteps AlreadyPosted
        Assert.Equal(JournalStatus.Posted, repost.Status);
        Assert.True(repost.IsBalanced);
        // Only the expense debit moved A→B; the liability credit is unchanged.
        var debit = Assert.Single(repost.Lines, l => l.Side == EntrySide.Debit);
        Assert.Equal(expenseB.Id, debit.LedgerAccountId);
        Assert.Equal(100m, debit.Amount);
        var credit = Assert.Single(repost.Lines, l => l.Side == EntrySide.Credit);
        Assert.Equal(liability.Id, credit.LedgerAccountId);
    }

    [Fact]
    public async Task Reclassify_parked_incurral_posts_and_resolves_pending_without_reversal()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var expenseB = MakeAnalytical(ExpenseCodeB, AccountGroup.Expense);
        var (uow, sut) = BuildSut(liability, expenseB);

        // Scheduled with no account → the incurral parks a pending (no journal entry).
        await sut.PostIncurralAsync(CompanyId, baseKey, 100m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-2", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        Assert.Empty(uow.JournalEntries);
        var pending = Assert.Single(uow.Pendings);
        Assert.Null(pending.ResolvedAt);

        // Contábil applies the account → posts the incurral under the base key and resolves the pending. No reversal.
        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expenseB.Id, 100m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-2", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(baseKey, entry.SourceKey); // base key — no ":r{n}", no ":reversal"
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(expenseB.Id, Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit).LedgerAccountId);
        Assert.NotNull(pending.ResolvedAt);
    }

    [Fact]
    public async Task Reclassify_redelivery_of_same_target_is_a_no_op()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var expenseA = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var expenseB = MakeAnalytical(ExpenseCodeB, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expenseA, expenseB, liability);

        await sut.PostIncurralAsync(CompanyId, baseKey, 100m, expenseA.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expenseB.Id, 100m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        // Redelivery: the live incurral (:r1) already debits B → nothing more happens.
        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expenseB.Id, 100m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entries = uow.JournalEntries;
        Assert.Equal(3, entries.Count); // no second reverse, no :r2
        Assert.DoesNotContain(entries, e => e.SourceKey == baseKey + ":r2");
        Assert.Equal(JournalStatus.Posted, entries.Single(e => e.SourceKey == baseKey + ":r1").Status);
    }

    // ── AmendDocumentReferenceAsync: refresh the número da NF on posted entries after a post-pay edit ──

    [Fact]
    public async Task AmendDocumentReference_updates_incurral_and_settlement_entries_any_status()
    {
        var payableId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var incurralKey = $"payable-incurral:{payableId}";
        var settlementKey = $"payment:{paymentId}";
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var (uow, sut) = BuildSut(expense, liability, cash);

        // Incurral + settlement posted with the OLD number; then reverse the incurral so we also have a
        // Reversed original + its :reversal — all three must be renumbered.
        await sut.PostIncurralAsync(CompanyId, incurralKey, 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-OLD", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.PostSettlementAsync(CompanyId, settlementKey, 100m, new DateOnly(2026, 7, 1), cashAccountId: null, withholdings: [], unitId: null, documentRef: "NF-OLD", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.ReverseAsync(CompanyId, incurralKey, "estorno", CancellationToken.None);
        Assert.Equal(3, uow.JournalEntries.Count);
        Assert.All(uow.JournalEntries, e => Assert.Equal("NF-OLD", e.SourceReference));

        await sut.AmendDocumentReferenceAsync(CompanyId, payableId, [paymentId], "NF-NEW", CancellationToken.None);

        // Incurral base (Reversed), incurral:reversal (Posted) and settlement (Posted) all now show NF-NEW.
        Assert.Equal(3, uow.JournalEntries.Count);
        Assert.All(uow.JournalEntries, e => Assert.Equal("NF-NEW", e.SourceReference));
    }

    [Fact]
    public async Task AmendDocumentReference_does_not_touch_other_payables_entries()
    {
        var payableA = Guid.NewGuid();
        var payableB = Guid.NewGuid();
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostIncurralAsync(CompanyId, $"payable-incurral:{payableA}", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-A", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.PostIncurralAsync(CompanyId, $"payable-incurral:{payableB}", 100m, expense.Id, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-B", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        await sut.AmendDocumentReferenceAsync(CompanyId, payableA, [], "NF-A2", CancellationToken.None);

        Assert.Equal("NF-A2", uow.JournalEntries.Single(e => e.SourceKey == $"payable-incurral:{payableA}").SourceReference);
        Assert.Equal("NF-B", uow.JournalEntries.Single(e => e.SourceKey == $"payable-incurral:{payableB}").SourceReference); // untouched
    }

    // ── AEL-935: PostOrRebook — re-book the incurral after a return-reversal under a versioned key ──

    [Fact]
    public async Task PostOrRebook_first_schedule_posts_under_base_key()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(baseKey, entry.SourceKey);
        Assert.Equal(JournalStatus.Posted, entry.Status);
    }

    [Fact]
    public async Task PostOrRebook_redelivery_with_live_incurral_is_no_op()
    {
        var payableId = Guid.NewGuid();
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expense.Id, [], null, null, null, null, null, null, CancellationToken.None);
        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expense.Id, [], null, null, null, null, null, null, CancellationToken.None);

        Assert.Single(uow.JournalEntries); // the live incurral already exists → idempotent
    }

    [Fact]
    public async Task PostOrRebook_after_return_reversal_reposts_under_versioned_key()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(expense, liability);

        // Scheduled once (base incurral), then returned → the incurral is reversed.
        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expense.Id, [], null, null, null, null, null, null, CancellationToken.None);
        await sut.ReverseAsync(CompanyId, baseKey, "Devolução de título", CancellationToken.None);
        Assert.Equal(2, uow.JournalEntries.Count); // base (reversed) + base:reversal

        // Re-approved → re-scheduled: re-books under :r1 instead of being skipped by the base-key guard.
        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expense.Id, [], null, null, null, null, null, null, CancellationToken.None);

        var repost = uow.JournalEntries.Single(e => e.SourceKey == baseKey + ":r1");
        Assert.Equal(JournalStatus.Posted, repost.Status);
        Assert.True(repost.IsBalanced);
        Assert.Equal(JournalStatus.Reversed, uow.JournalEntries.Single(e => e.SourceKey == baseKey).Status);
    }

    [Fact]
    public async Task PostOrRebook_when_unclassified_reuses_the_open_pending()
    {
        // Unclassified scheduled title parks a pending (no posted incurral); returning it reverses nothing and a
        // re-schedule reuses the same pending — no duplicate, no versioned key.
        var payableId = Guid.NewGuid();
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);

        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expenseAccountId: null, [], null, null, null, null, null, null, CancellationToken.None);
        await sut.PostOrRebookIncurralAsync(CompanyId, payableId, 100m, expenseAccountId: null, [], null, null, null, null, null, null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        Assert.Single(uow.Pendings); // reused, not duplicated
    }

    [Fact]
    public async Task AmendDocumentReference_refreshes_parked_incurral_plan()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        // Only the liability is seeded — the expense code is absent, so the incurral parks a pending with a plan
        // that snapshots the OLD document number.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSut(liability);
        await sut.PostIncurralAsync(CompanyId, baseKey, 100m, Guid.NewGuid(), [], competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-OLD", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        Assert.Equal("NF-OLD", PostingPlan.Deserialize(Assert.Single(uow.Pendings).Plan)!.DocumentRef);

        await sut.AmendDocumentReferenceAsync(CompanyId, payableId, [], "NF-NEW", CancellationToken.None);

        // A later ResolvePending would now replay the corrected number.
        Assert.Equal("NF-NEW", PostingPlan.Deserialize(Assert.Single(uow.Pendings).Plan)!.DocumentRef);
    }

    // ── Suspense account: an unclassified incurral posts instead of parking ───
    //
    // A requisition-sourced payable schedules with AccountingAccountId = null (the AI candidate is only a
    // suggestion). Parking the incurral there while the settlement posts anyway leaves Fornecedores a Pagar
    // debited without ever having been credited. With a suspense account configured the incurral always posts,
    // and the accountant's later reclassify moves the expense to its definitive account.

    private const string SuspenseCode = "1.2.99";

    private static AccountingPostingOptions SuspenseOpts() =>
        new(PayablesLiabilityCode: LiabilityCode, CashCode: CashCode,
            WithholdingCodes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Irrf"] = "2.1.tax",
            },
            UnclassifiedExpenseCode: SuspenseCode);

    private static (AcctStubUow uow, PayablePostingService sut) BuildSuspenseSut(params object[] seed)
    {
        var uow = new AcctStubUow(seed);
        var sut = new PayablePostingService(uow, new FakeTimeProvider(FixedNow), SuspenseOpts());
        return (uow, sut);
    }

    [Fact]
    public async Task PostIncurral_without_expense_account_debits_the_suspense_account_and_parks_nothing()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var (uow, sut) = BuildSuspenseSut(liability, suspense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s1", 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.True(entry.IsBalanced);
        Assert.Equal(suspense.Id, Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit).LedgerAccountId);
        Assert.Equal(liability.Id, Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit).LedgerAccountId);

        // The whole point: nothing is parked, so the liability is credited before any settlement debits it.
        Assert.Empty(uow.Pendings);
    }

    [Fact]
    public async Task Settlement_after_a_suspense_incurral_leaves_the_liability_square()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var cash = MakeAnalytical(CashCode, AccountGroup.Asset);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var (uow, sut) = BuildSuspenseSut(liability, cash, suspense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s2", 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.PostSettlementAsync(CompanyId, "payable-settlement:s2", 150m, new DateOnly(2026, 7, 1),
            cashAccountId: null, withholdings: [], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        // Sum every movement on the liability across both entries: the credit from the incurral must cancel
        // the debit from the settlement. This is the invariant the production defect broke.
        var net = uow.JournalEntries
            .SelectMany(e => e.Lines)
            .Where(l => l.LedgerAccountId == liability.Id)
            .Sum(l => l.Side == EntrySide.Credit ? l.Amount : -l.Amount);
        Assert.Equal(0m, net);
    }

    [Fact]
    public async Task PostIncurral_parks_when_the_suspense_account_is_configured_but_absent_from_the_chart()
    {
        // Safe degradation: until Contabilidade creates the analytical, behaviour is exactly what it is today.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var (uow, sut) = BuildSuspenseSut(liability);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s3", 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        Assert.Single(uow.Pendings);
    }

    [Fact]
    public async Task PostIncurral_keeps_parking_an_inactive_expense_account_instead_of_using_the_suspense()
    {
        // An inactive expense account is a KNOWN account — it parks with its real code so it can be replayed
        // once activated. Redirecting it to the suspense would lose that intent.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        expense.Deactivate();
        var (uow, sut) = BuildSuspenseSut(liability, suspense, expense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s4", 150m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        var pending = Assert.Single(uow.Pendings);
        Assert.Contains(ExpenseCode, pending.IntendedCodes);
    }

    [Fact]
    public async Task PostIncurral_with_the_suspense_still_parks_when_a_withholding_account_is_unmapped()
    {
        // The suspense covers the expense line only — it must not paper over any other missing account.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var (uow, sut) = BuildSuspenseSut(liability, suspense); // "2.1.tax" deliberately not seeded

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s5", 150m, expenseAccountId: null,
            [("Irrf", 10m)],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        Assert.Empty(uow.JournalEntries);
        Assert.Single(uow.Pendings);
    }

    [Fact]
    public async Task Suspense_incurral_says_in_the_histórico_that_the_account_is_still_pending()
    {
        // The razão has to explain itself: an accountant reading the Livro Razão must see that this expense
        // sits in a suspense account awaiting classification, without cross-referencing another screen.
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var (uow, sut) = BuildSuspenseSut(liability, suspense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s6", 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.Contains("classificar", entry.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_normally_classified_incurral_keeps_the_plain_histórico()
    {
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var expense = MakeAnalytical(ExpenseCode, AccountGroup.Expense);
        var (uow, sut) = BuildSuspenseSut(liability, suspense, expense);

        await sut.PostIncurralAsync(CompanyId, "payable-incurral:s7", 150m, expense.Id, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entry = Assert.Single(uow.JournalEntries);
        Assert.DoesNotContain("classificar", entry.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reclassifying_drops_the_pending_note_from_the_histórico()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var expense = MakeAnalytical(ExpenseCodeB, AccountGroup.Expense);
        var (uow, sut) = BuildSuspenseSut(liability, suspense, expense);

        await sut.PostIncurralAsync(CompanyId, baseKey, 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expense.Id, 150m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var repost = uow.JournalEntries.Single(e => e.SourceKey == baseKey + ":r1");
        Assert.DoesNotContain("classificar", repost.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reclassify_after_a_suspense_incurral_reverses_it_and_reposts_on_the_real_account()
    {
        var payableId = Guid.NewGuid();
        var baseKey = $"payable-incurral:{payableId}";
        var liability = MakeAnalytical(LiabilityCode, AccountGroup.Liability);
        var suspense = MakeAnalytical(SuspenseCode, AccountGroup.Asset);
        var expense = MakeAnalytical(ExpenseCodeB, AccountGroup.Expense);
        var (uow, sut) = BuildSuspenseSut(liability, suspense, expense);

        await sut.PostIncurralAsync(CompanyId, baseKey, 150m, expenseAccountId: null, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-9", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        // Contábil picks the definitive account: the existing "posted" branch reverses + reposts at :r1.
        await sut.ReclassifyIncurralAsync(CompanyId, payableId, expense.Id, 150m, [],
            competenceDate: null, costCenterId: null, unitId: null, documentRef: "NF-9", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);

        var entries = uow.JournalEntries;
        Assert.Equal(3, entries.Count); // suspense incurral (reversed) + :reversal + :r1
        Assert.Equal(JournalStatus.Reversed, entries.Single(e => e.SourceKey == baseKey).Status);

        var repost = entries.Single(e => e.SourceKey == baseKey + ":r1");
        Assert.Equal(JournalStatus.Posted, repost.Status);
        Assert.Equal(expense.Id, Assert.Single(repost.Lines, l => l.Side == EntrySide.Debit).LedgerAccountId);

        // The suspense nets to zero once reclassified, and the liability is still square.
        var suspenseNet = entries.SelectMany(e => e.Lines)
            .Where(l => l.LedgerAccountId == suspense.Id)
            .Sum(l => l.Side == EntrySide.Debit ? l.Amount : -l.Amount);
        Assert.Equal(0m, suspenseNet);
        Assert.Empty(uow.Pendings);
    }
}
