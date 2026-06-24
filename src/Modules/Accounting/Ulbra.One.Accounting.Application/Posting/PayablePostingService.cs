using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Posting;

public sealed class PayablePostingService(IUnitOfWork uow, TimeProvider clock, AccountingPostingOptions opts)
    : IPayablePostingService
{
    // ── Idempotency: a posted or draft entry already exists for this sourceKey ──
    private async Task<bool> AlreadyPostedAsync(Guid companyId, string sourceKey, CancellationToken ct) =>
        await uow.ReadSet<JournalEntry>()
            .AnyAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey && e.DeletedAt == null, ct);

    // ── Resolve an active analytical ledger account by code; null if not found ──
    private async Task<LedgerAccount?> ResolveAsync(Guid companyId, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var a = await uow.ReadSet<LedgerAccount>()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Code == code && a.DeletedAt == null, ct);
        return a is { } acct && acct.CanReceiveEntry ? acct : null;
    }

    // ── Next per-company sequential entry number ──────────────────────────────
    private async Task<int> NextNumberAsync(Guid companyId, CancellationToken ct) =>
        (await uow.ReadSet<JournalEntry>()
            .Where(e => e.CompanyId == companyId && e.Number != null)
            .MaxAsync(e => (int?)e.Number, ct) ?? 0) + 1;

    // ── Core: post two-line balanced entry or park a pending ─────────────────
    private async Task PostTwoLineAsync(
        Guid companyId, string sourceKey, string description, PostingKind kind,
        decimal amount, string? debitCode, string? creditCode, CancellationToken ct)
    {
        if (await AlreadyPostedAsync(companyId, sourceKey, ct)) return; // idempotent

        var debit = await ResolveAsync(companyId, debitCode, ct);
        var credit = await ResolveAsync(companyId, creditCode, ct);

        if (debit is null || credit is null)
        {
            var reason = debit is null
                ? $"AccountNotFound:{debitCode}"
                : $"AccountNotFound:{creditCode}";
            var pending = new PendingAccountingPosting(
                companyId, sourceKey, kind, amount, reason, $"Dr {debitCode} / Cr {creditCode}");
            pending.Stamp(clock.GetUtcNow());
            await uow.AddAsync(pending, ct);
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var entry = new JournalEntry(companyId, today, description,
            JournalOrigin.Automatic, sourceReference: sourceKey, sourceKey: sourceKey);
        entry.AddLine(new JournalLine(debit.Id, EntrySide.Debit, amount));
        entry.AddLine(new JournalLine(credit.Id, EntrySide.Credit, amount));
        entry.Post(await NextNumberAsync(companyId, ct), clock.GetUtcNow());
        await uow.AddAsync(entry, ct);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task PostIncurralAsync(Guid companyId, string sourceKey, decimal amount, string? expenseCode, CancellationToken ct) =>
        PostTwoLineAsync(companyId, sourceKey,
            "Provisão de despesa (a pagar)",
            PostingKind.Incurral, amount,
            debitCode: expenseCode,
            creditCode: opts.PayablesLiabilityCode,
            ct);

    public Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, CancellationToken ct) =>
        PostTwoLineAsync(companyId, sourceKey,
            "Pagamento de fornecedor",
            PostingKind.Settlement, amount,
            debitCode: opts.PayablesLiabilityCode,
            creditCode: opts.CashCode,
            ct);

    public async Task ReverseAsync(Guid companyId, string sourceKey, string reason, CancellationToken ct)
    {
        // Idempotency: bail if reversal entry already exists
        if (await AlreadyPostedAsync(companyId, sourceKey + ":reversal", ct)) return;

        // Use tracked Set so the Status mutation (→ Reversed) is persisted on SaveChanges.
        var entry = await uow.Set<JournalEntry>()
            .FirstOrDefaultAsync(
                e => e.CompanyId == companyId && e.SourceKey == sourceKey
                     && e.Status == JournalStatus.Posted && e.DeletedAt == null,
                ct);

        if (entry is null) return;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var reversal = entry.Reverse(reason, today, await NextNumberAsync(companyId, ct), clock.GetUtcNow());
        await uow.AddAsync(reversal, ct);
    }
}
