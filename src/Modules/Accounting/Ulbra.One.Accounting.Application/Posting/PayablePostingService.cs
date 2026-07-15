using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Periods;
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

    // ── Idempotency: an OPEN pending already exists for this sourceKey (redelivery must not duplicate) ──
    private async Task<bool> AlreadyPendingAsync(Guid companyId, string sourceKey, CancellationToken ct) =>
        await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey && p.ResolvedAt == null, ct);

    // ── Resolve an active analytical ledger account by code (global chart); null if not found ──
    private async Task<LedgerAccount?> ResolveAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var a = await uow.ReadSet<LedgerAccount>()
            .FirstOrDefaultAsync(a => a.Code == code && a.DeletedAt == null, ct);
        return a is { } acct && acct.CanReceiveEntry ? acct : null;
    }

    // ── Resolve a ledger account's CODE by id (regardless of active/postable) so the incurral plan is
    //    code-based and replayable: an inactive expense account parks with its real code and can be
    //    reprocessed once activated (ResolveAsync re-checks CanReceiveEntry at post/replay time). ──
    private async Task<string?> ResolveCodeByIdAsync(Guid? id, CancellationToken ct)
    {
        if (id is not { } accountId) return null;
        return await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => a.Code)
            .FirstOrDefaultAsync(ct);
    }

    // ── Resolve a POSTABLE ledger account's code by id; null when missing/inactive/synthetic/deleted. Used for
    //    the settlement cash account: a stale bank→ledger mapping must NOT park (that would strand the paired
    //    incurral, cf. PostEntryAsync); the caller falls back to the configured global cash code instead. ──
    private async Task<string?> ResolvePostableCodeByIdAsync(Guid? id, CancellationToken ct)
    {
        if (id is not { } accountId) return null;
        var a = await uow.ReadSet<LedgerAccount>()
            .FirstOrDefaultAsync(x => x.Id == accountId && x.DeletedAt == null, ct);
        return a is { } acct && acct.CanReceiveEntry ? acct.Code : null;
    }

    // ── Next per-company sequential entry number ──────────────────────────────
    private async Task<int> NextNumberAsync(Guid companyId, CancellationToken ct) =>
        (await uow.ReadSet<JournalEntry>()
            .Where(e => e.CompanyId == companyId && e.Number != null)
            .MaxAsync(e => (int?)e.Number, ct) ?? 0) + 1;

    // ── Core: post a multi-line balanced entry or park a pending ─────────────
    // lines: list of (code, side, amount, costCenterId). All codes must resolve; first unresolvable → Pending.
    // Returns true when a posting is in place (posted now or already posted); false when parked (still blocked).
    private async Task<bool> PostEntryAsync(
        Guid companyId, string sourceKey, string description, PostingKind kind, DateOnly competenceDate,
        IReadOnlyList<(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId)> lines, CancellationToken ct)
    {
        if (await AlreadyPostedAsync(companyId, sourceKey, ct)) return true; // idempotent

        // Resolve all accounts up-front (all-or-nothing).
        var resolved = new List<(LedgerAccount Account, EntrySide Side, decimal Amount, Guid? CostCenterId)>(lines.Count);
        bool anyUnresolvable = false;
        string firstMissingLabel = string.Empty;
        foreach (var (code, side, amount, costCenterId) in lines)
        {
            var account = await ResolveAsync(code, ct);
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
                resolved.Add((account, side, amount, costCenterId));
            }
        }

        var intendedCodes = string.Join(" / ", lines.Select(l => $"{(l.Side == EntrySide.Debit ? "Dr" : "Cr")} {l.Code}"));
        var debitTotal = lines.Sum(l => l.Side == EntrySide.Debit ? l.Amount : 0m);
        // Replay plan stores the ORIGINAL (pre-clamp) competence, so a later reprocess re-applies the clamp.
        var plan = new PostingPlan(competenceDate, description,
            lines.Select(l => new PostingPlanLine(l.Code, l.Side, l.Amount, l.CostCenterId)).ToList()).Serialize();

        if (anyUnresolvable)
        {
            await ParkPendingAsync(companyId, sourceKey, kind, debitTotal, $"AccountNotFound:{firstMissingLabel}", intendedCodes, plan, ct);
            return false;
        }

        // Closed-period clamp (automatic path never blocks): if the true competence sits in a closed period,
        // book it in the current open period so the incurral and its settlement stay paired (a parked incurral
        // beside a posted settlement would leave Fornecedores a Pagar unbalanced). Last resort: if even today
        // is closed, park rather than post silently into the wrong period.
        var competence = competenceDate;
        var clamped = false;
        if (await AccountingPeriodGuard.IsClosedAsync(uow, companyId, competence, ct))
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (await AccountingPeriodGuard.IsClosedAsync(uow, companyId, today, ct))
            {
                await ParkPendingAsync(companyId, sourceKey, kind, debitTotal,
                    $"PeriodClosed:{competence.Year:D4}-{competence.Month:D2}", intendedCodes, plan, ct);
                return false;
            }
            competence = today;
            clamped = true;
        }

        var finalDescription = clamped
            ? PostingNarratives.ClampedCompetence(description, competenceDate, competence)
            : description;

        var entry = new JournalEntry(companyId, competence, finalDescription,
            JournalOrigin.Automatic, sourceReference: sourceKey, sourceKey: sourceKey);
        foreach (var (account, side, amount, costCenterId) in resolved)
            entry.AddLine(new JournalLine(account.Id, side, amount, costCenterId));
        entry.Post(await NextNumberAsync(companyId, ct), clock.GetUtcNow());
        await uow.AddAsync(entry, ct);
        return true;
    }

    // ── Park a payable posting that could not be booked (missing account or fully-closed period) ──
    // Idempotent: skips if an open pending for this sourceKey already exists (at-least-once redelivery).
    private async Task ParkPendingAsync(
        Guid companyId, string sourceKey, PostingKind kind, decimal amount,
        string reason, string intendedCodes, string? plan, CancellationToken ct)
    {
        if (await AlreadyPendingAsync(companyId, sourceKey, ct)) return;
        var pending = new PendingAccountingPosting(companyId, sourceKey, kind, amount, reason, intendedCodes, plan);
        pending.Stamp(clock.GetUtcNow());
        await uow.AddAsync(pending, ct);
    }

    // ── Reprocess a parked posting from its stored plan (after fixing the chart / reopening the period) ──
    public async Task<bool> ResolvePendingAsync(PendingAccountingPosting pending, CancellationToken ct)
    {
        if (pending.ResolvedAt is not null) return true; // already resolved (idempotent)

        var plan = PostingPlan.Deserialize(pending.Plan);
        if (plan is null) return false; // legacy pending without a replayable plan

        var lines = plan.Lines
            .Select(l => (l.Code, l.Side, l.Amount, l.CostCenterId))
            .ToList();
        var posted = await PostEntryAsync(pending.CompanyId, pending.SourceKey, plan.Description, pending.Kind, plan.CompetenceDate, lines, ct);
        if (posted) pending.MarkResolved(clock.GetUtcNow());
        return posted;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task PostIncurralAsync(
        Guid companyId, string sourceKey, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, CancellationToken ct)
    {
        // The expense line comes from the payable's classified global ledger account (resolved by id);
        // liability/cash/withholding lines are Accounting-internal config codes. An unresolvable expense
        // account leaves a null code, which PostEntryAsync parks as a PendingAccountingPosting.
        //
        // Build the multi-line plan (cost center carried on the expense/result line only):
        //   Dr expenseAccount      = gross amount        (+ costCenterId)
        //   Cr PayablesLiability   = gross − Σwithholdings  (net)
        //   Cr WithholdingCode[t]  = each withholding.Amount  (one line per withholding)
        // When withholdings is empty the net == gross → exactly the classic 2-line entry.
        var expenseCode = await ResolveCodeByIdAsync(expenseAccountId, ct);
        var totalWithheld = withholdings.Sum(w => w.Amount);
        var netAmount = amount - totalWithheld;

        var lineList = new List<(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId)>(2 + withholdings.Count)
        {
            (expenseCode, EntrySide.Debit, amount, costCenterId),
            (opts.PayablesLiabilityCode, EntrySide.Credit, netAmount, (Guid?)null),
        };

        foreach (var w in withholdings)
        {
            opts.WithholdingCodes.TryGetValue(w.TaxType, out var withholdingCode);
            lineList.Add((withholdingCode, EntrySide.Credit, w.Amount, (Guid?)null));
        }

        // Competence = título's real competence (IssueDate); null (e.g. advances with no NF) falls back to today.
        var competence = competenceDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await PostEntryAsync(companyId, sourceKey, PostingNarratives.Incurral, PostingKind.Incurral, competence, lineList, ct);
    }

    public async Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, DateOnly paymentDate, Guid? cashAccountId, CancellationToken ct)
    {
        // Credit the paying bank's own ledger account when it resolves to a postable account; otherwise fall
        // back to the configured global cash code (unmapped bank, or a stale/inactive mapping).
        var cashCode = await ResolvePostableCodeByIdAsync(cashAccountId, ct) ?? opts.CashCode;
        await PostEntryAsync(companyId, sourceKey,
            PostingNarratives.Settlement,
            PostingKind.Settlement,
            paymentDate,
            [
                (opts.PayablesLiabilityCode, EntrySide.Debit, amount, (Guid?)null),
                (cashCode, EntrySide.Credit, amount, (Guid?)null),
            ],
            ct);
    }

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
