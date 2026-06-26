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

    // ── Core: post a multi-line balanced entry or park a pending ─────────────
    // lines: list of (code, side, amount). All codes must resolve; first unresolvable → Pending.
    private async Task PostEntryAsync(
        Guid companyId, string sourceKey, string description, PostingKind kind,
        IReadOnlyList<(string? Code, EntrySide Side, decimal Amount)> lines, CancellationToken ct)
    {
        if (await AlreadyPostedAsync(companyId, sourceKey, ct)) return; // idempotent

        // Resolve all accounts up-front (all-or-nothing).
        var resolved = new List<(LedgerAccount Account, EntrySide Side, decimal Amount)>(lines.Count);
        bool anyUnresolvable = false;
        string firstMissingLabel = string.Empty;
        foreach (var (code, side, amount) in lines)
        {
            var account = await ResolveAsync(companyId, code, ct);
            if (account is null)
            {
                if (!anyUnresolvable)
                {
                    anyUnresolvable = true;
                    firstMissingLabel = code ?? "(null)";
                }
            }
            else
            {
                resolved.Add((account, side, amount));
            }
        }

        if (anyUnresolvable)
        {
            // Park a single PendingAccountingPosting naming the first unresolvable code.
            var desc = string.Join(" / ", lines.Select(l => $"{(l.Side == EntrySide.Debit ? "Dr" : "Cr")} {l.Code}"));
            var pending = new PendingAccountingPosting(
                companyId, sourceKey, kind, lines.Sum(l => l.Side == EntrySide.Debit ? l.Amount : 0m),
                $"AccountNotFound:{firstMissingLabel}", desc);
            pending.Stamp(clock.GetUtcNow());
            await uow.AddAsync(pending, ct);
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var entry = new JournalEntry(companyId, today, description,
            JournalOrigin.Automatic, sourceReference: sourceKey, sourceKey: sourceKey);
        foreach (var (account, side, amount) in resolved)
            entry.AddLine(new JournalLine(account.Id, side, amount));
        entry.Post(await NextNumberAsync(companyId, ct), clock.GetUtcNow());
        await uow.AddAsync(entry, ct);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task PostIncurralAsync(
        Guid companyId, string sourceKey, decimal amount, string? expenseCode,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings, CancellationToken ct)
    {
        // Build the multi-line plan:
        //   Dr expenseCode         = gross amount
        //   Cr PayablesLiability   = gross − Σwithholdings  (net)
        //   Cr WithholdingCode[t]  = each withholding.Amount  (one line per withholding)
        // When withholdings is empty the net == gross → exactly the classic 2-line entry.
        var totalWithheld = withholdings.Sum(w => w.Amount);
        var netAmount = amount - totalWithheld;

        var lineList = new List<(string? Code, EntrySide Side, decimal Amount)>(2 + withholdings.Count)
        {
            (expenseCode, EntrySide.Debit, amount),
            (opts.PayablesLiabilityCode, EntrySide.Credit, netAmount),
        };

        foreach (var w in withholdings)
        {
            opts.WithholdingCodes.TryGetValue(w.TaxType, out var withholdingCode);
            lineList.Add((withholdingCode, EntrySide.Credit, w.Amount));
        }

        return PostEntryAsync(companyId, sourceKey, "Provisão de despesa (a pagar)", PostingKind.Incurral, lineList, ct);
    }

    public Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, CancellationToken ct) =>
        PostEntryAsync(companyId, sourceKey,
            "Pagamento de fornecedor",
            PostingKind.Settlement,
            [
                (opts.PayablesLiabilityCode, EntrySide.Debit, amount),
                (opts.CashCode, EntrySide.Credit, amount),
            ],
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
