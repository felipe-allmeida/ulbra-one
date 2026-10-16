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
        IReadOnlyList<(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId, Guid? UnitId)> lines,
        string? documentRef, string? counterpartyName, string? counterpartyDocument, CancellationToken ct)
    {
        if (await AlreadyPostedAsync(companyId, sourceKey, ct)) return true; // idempotent

        // Resolve all accounts up-front (all-or-nothing).
        var resolved = new List<(LedgerAccount Account, EntrySide Side, decimal Amount, Guid? CostCenterId, Guid? UnitId)>(lines.Count);
        bool anyUnresolvable = false;
        string firstMissingLabel = string.Empty;
        foreach (var (code, side, amount, costCenterId, unitId) in lines)
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
                resolved.Add((account, side, amount, costCenterId, unitId));
            }
        }

        var intendedCodes = string.Join(" / ", lines.Select(l => $"{(l.Side == EntrySide.Debit ? "Dr" : "Cr")} {l.Code}"));
        var debitTotal = lines.Sum(l => l.Side == EntrySide.Debit ? l.Amount : 0m);
        // Replay plan stores the ORIGINAL (pre-clamp) competence, so a later reprocess re-applies the clamp.
        var plan = new PostingPlan(competenceDate, description,
            lines.Select(l => new PostingPlanLine(l.Code, l.Side, l.Amount, l.CostCenterId, l.UnitId)).ToList(), documentRef,
            counterpartyName, counterpartyDocument).Serialize();

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

        // SourceReference is the human-facing document number (shown as "Documento" in the reports); SourceKey is
        // the internal idempotency key. They are distinct — never surface the idempotency key to the user.
        var entry = new JournalEntry(companyId, competence, finalDescription,
            JournalOrigin.Automatic, sourceReference: documentRef, sourceKey: sourceKey,
            counterpartyName: counterpartyName, counterpartyDocument: counterpartyDocument);
        foreach (var (account, side, amount, costCenterId, unitId) in resolved)
            entry.AddLine(new JournalLine(account.Id, side, amount, costCenterId, unitId));
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
            .Select(l => (l.Code, l.Side, l.Amount, l.CostCenterId, l.UnitId))
            .ToList();
        var posted = await PostEntryAsync(pending.CompanyId, pending.SourceKey, plan.Description, pending.Kind, plan.CompetenceDate, lines, plan.DocumentRef, plan.CounterpartyName, plan.CounterpartyDocument, ct);
        if (posted) pending.MarkResolved(clock.GetUtcNow());
        return posted;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<bool> PostIncurralAsync(
        Guid companyId, string sourceKey, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct)
    {
        // The expense line comes from the payable's classified global ledger account (resolved by id);
        // liability/cash/withholding lines are Accounting-internal config codes.
        //
        // An UNKNOWN expense account (null id — the requisition path schedules with the AI candidate as a mere
        // suggestion — or an id that no longer exists) falls back to the suspense account so the incurral still
        // posts. Parking it while the settlement posts anyway is what leaves Fornecedores a Pagar debited with
        // no matching credit. An INACTIVE account is deliberately NOT redirected: it is a known account, so it
        // keeps its own code and parks with a replayable plan (see ResolveCodeByIdAsync). With no suspense
        // configured the code stays null and PostEntryAsync parks exactly as before.
        //
        // Build the multi-line plan (cost center carried on the expense/result line only):
        //   Dr expenseAccount      = gross amount        (+ costCenterId)
        //   Cr PayablesLiability   = gross − Σwithholdings  (net)
        //   Cr WithholdingCode[t]  = each withholding.Amount  (one line per withholding)
        // When withholdings is empty the net == gross → exactly the classic 2-line entry.
        var classifiedCode = await ResolveCodeByIdAsync(expenseAccountId, ct);
        var usingSuspense = classifiedCode is null && opts.UnclassifiedExpenseCode is not null;
        var expenseCode = classifiedCode ?? opts.UnclassifiedExpenseCode;
        var totalWithheld = withholdings.Sum(w => w.Amount);
        var netAmount = amount - totalWithheld;

        var lineList = new List<(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId, Guid? UnitId)>(2 + withholdings.Count)
        {
            (expenseCode, EntrySide.Debit, amount, costCenterId, unitId),
            (opts.PayablesLiabilityCode, EntrySide.Credit, netAmount, (Guid?)null, unitId),
        };

        // One credit line per retention account: taxes sharing an account (e.g. PIS/COFINS/CSLL) collapse into a
        // single summed line. Unmapped taxes (null code) group together and still park the entry.
        foreach (var g in withholdings.GroupBy(w => opts.WithholdingCodes.GetValueOrDefault(w.TaxType)))
            lineList.Add((g.Key, EntrySide.Credit, g.Sum(x => x.Amount), (Guid?)null, unitId));

        // Competence = título's real competence (IssueDate); null (e.g. advances with no NF) falls back to today.
        var competence = competenceDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // The histórico carries the pending-classification note only while the expense sits in the suspense —
        // a reclassification reposts with a resolved account and the note drops off on its own.
        var narrative = usingSuspense ? PostingNarratives.Unclassified(PostingNarratives.Incurral) : PostingNarratives.Incurral;
        return await PostEntryAsync(companyId, sourceKey, narrative, PostingKind.Incurral, competence, lineList, documentRef, counterpartyName, counterpartyDocument, ct);
    }

    public async Task<bool> PostOrRebookIncurralAsync(
        Guid companyId, Guid payableId, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct)
    {
        var baseKey = $"payable-incurral:{payableId}";
        var entries = await uow.ReadSet<JournalEntry>()
            .Where(e => e.CompanyId == companyId && e.SourceKey != null
                && e.SourceKey.StartsWith(baseKey) && e.DeletedAt == null)
            .Select(e => new { e.SourceKey, e.Status })
            .ToListAsync(ct);

        // A live (Posted, non-reversal) incurral already exists → idempotent (redelivery / already booked).
        if (entries.Any(e => e.Status == JournalStatus.Posted && IsIncurralKey(e.SourceKey!, baseKey)))
            return true;

        // Otherwise book the next free key: base the first time, ":r{n+1}" once a prior incurral was reversed
        // (title returned) — the reversed entry still occupies the base key.
        var key = NextIncurralKey(entries.Select(e => e.SourceKey!), baseKey);
        return await PostIncurralAsync(companyId, key, amount, expenseAccountId, withholdings,
            competenceDate, costCenterId, unitId, documentRef, counterpartyName, counterpartyDocument, ct);
    }

    public async Task ReclassifyIncurralAsync(
        Guid companyId, Guid payableId, Guid? expenseAccountId, decimal amount,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct)
    {
        var baseKey = $"payable-incurral:{payableId}";

        // Incurral postings for this payable (base + prior reclassification reposts + their reversals).
        var entries = await uow.Set<JournalEntry>()
            .Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.SourceKey != null
                && e.SourceKey.StartsWith(baseKey) && e.DeletedAt == null)
            .ToListAsync(ct);

        // Live incurral = a Posted entry keyed base or ":r{n}" (never a ":reversal"); the highest version wins.
        var live = entries
            .Where(e => e.Status == JournalStatus.Posted && IsIncurralKey(e.SourceKey!, baseKey))
            .OrderByDescending(e => IncurralVersion(e.SourceKey!, baseKey))
            .FirstOrDefault();

        if (live is not null)
        {
            // Idempotent: the live incurral already debits the target account (redelivery / no-op).
            var currentExpense = live.Lines.FirstOrDefault(l => l.Side == EntrySide.Debit);
            if (currentExpense is not null && expenseAccountId is { } target && currentExpense.LedgerAccountId == target)
                return;

            // Posted case: reverse the current incurral, repost under the next versioned key with the new account.
            await ReverseAsync(companyId, live.SourceKey!, "Reclassificação contábil", ct);
            var nextKey = $"{baseKey}:r{IncurralVersion(live.SourceKey!, baseKey) + 1}";
            await PostIncurralAsync(companyId, nextKey, amount, expenseAccountId, withholdings,
                competenceDate, costCenterId, unitId, documentRef, counterpartyName, counterpartyDocument, ct);
            return;
        }

        // Parked case: no posted incurral — an open pending holds the (null-account) plan. Post against the base
        // key and resolve the pending.
        var posted = await PostIncurralAsync(companyId, baseKey, amount, expenseAccountId, withholdings,
            competenceDate, costCenterId, unitId, documentRef, counterpartyName, counterpartyDocument, ct);
        if (posted)
        {
            var pending = await uow.Set<PendingAccountingPosting>()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.SourceKey == baseKey && p.ResolvedAt == null, ct);
            pending?.MarkResolved(clock.GetUtcNow());
        }
    }

    // ── Incurral sourceKey versioning: base = "payable-incurral:{id}" (v0), reclassification reposts = ":r{n}".
    //    Reversal keys always end ":reversal" and are excluded (they also start with ":r"). ──
    private static bool IsIncurralKey(string sourceKey, string baseKey) =>
        sourceKey == baseKey ||
        (sourceKey.StartsWith(baseKey + ":r", StringComparison.Ordinal) && !sourceKey.EndsWith(":reversal", StringComparison.Ordinal));

    private static int IncurralVersion(string sourceKey, string baseKey) =>
        sourceKey.Length > baseKey.Length + 2 && int.TryParse(sourceKey.AsSpan(baseKey.Length + 2), out var n) ? n : 0;

    // Next free incurral key for a payable: the base "payable-incurral:{id}" the first time, else ":r{max+1}"
    // once any incurral key (base or a prior ":r{n}") already exists — whatever its status. Keeps a re-book off
    // an occupied key, since AlreadyPostedAsync is status-blind and a reversed entry still holds its key.
    private static string NextIncurralKey(IEnumerable<string> incurralSourceKeys, string baseKey)
    {
        var versions = incurralSourceKeys
            .Where(k => IsIncurralKey(k, baseKey))
            .Select(k => IncurralVersion(k, baseKey))
            .ToList();
        return versions.Count > 0 ? $"{baseKey}:r{versions.Max() + 1}" : baseKey;
    }

    public async Task AmendDocumentReferenceAsync(
        Guid companyId, Guid payableId, IReadOnlyList<Guid> paymentIds, string? documentRef, CancellationToken ct)
    {
        var incurralPrefix = $"payable-incurral:{payableId}";
        // Settlement entries are keyed by payment id (base + its ":reversal"); build the exact set for an IN filter.
        var settlementKeys = paymentIds
            .SelectMany(id => new[] { $"payment:{id}", $"payment:{id}:reversal" })
            .ToList();

        // Every entry sourced from this payable, ANY status: a reversed original, its reversal and a repost all
        // carry the número and must all show the corrected value in the Razão (metadata-only — no rebook).
        var entries = await uow.Set<JournalEntry>()
            .Where(e => e.CompanyId == companyId && e.SourceKey != null && e.DeletedAt == null
                && (e.SourceKey.StartsWith(incurralPrefix) || settlementKeys.Contains(e.SourceKey)))
            .ToListAsync(ct);
        foreach (var entry in entries)
            entry.AmendSourceReference(documentRef);

        // Parked incurral (unclassified, never posted): refresh the stored plan's DocumentRef so a later resolve
        // replays the corrected número. Only the still-open incurral pending — settlements never park a plan here.
        var pending = await uow.Set<PendingAccountingPosting>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.SourceKey == incurralPrefix && p.ResolvedAt == null, ct);
        if (pending is not null && PostingPlan.Deserialize(pending.Plan) is { } plan)
        {
            var normalized = string.IsNullOrWhiteSpace(documentRef) ? null : documentRef.Trim();
            pending.SetPlan((plan with { DocumentRef = normalized }).Serialize());
        }
    }

    public async Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, DateOnly paymentDate, Guid? cashAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct)
    {
        // Credit the paying bank's own ledger account when it resolves to a postable account; otherwise fall
        // back to the configured global cash code (unmapped bank, or a stale/inactive mapping).
        //
        // Build the settlement plan — the CSRF retentions (PIS/COFINS/CSLL) recognized at this baixa raise the
        // liability debit and take their own credit lines, so the cash actually paid stays = amount:
        //   Dr PayablesLiability   = amount + Σwithholdings
        //   Cr WithholdingCode[t]  = each withholding.Amount   (one line per CSRF tax)
        //   Cr cash                = amount
        // When withholdings is empty this is exactly the classic 2-line Dr liability / Cr cash entry.
        var cashCode = await ResolvePostableCodeByIdAsync(cashAccountId, ct) ?? opts.CashCode;
        var totalWithheld = withholdings.Sum(w => w.Amount);

        var lineList = new List<(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId, Guid? UnitId)>(2 + withholdings.Count)
        {
            (opts.PayablesLiabilityCode, EntrySide.Debit, amount + totalWithheld, (Guid?)null, unitId),
        };
        // One credit line per retention account (PIS/COFINS/CSLL share one → summed); unmapped codes park.
        foreach (var g in withholdings.GroupBy(w => opts.WithholdingCodes.GetValueOrDefault(w.TaxType)))
            lineList.Add((g.Key, EntrySide.Credit, g.Sum(x => x.Amount), (Guid?)null, unitId));
        lineList.Add((cashCode, EntrySide.Credit, amount, (Guid?)null, unitId));

        await PostEntryAsync(companyId, sourceKey,
            PostingNarratives.Settlement,
            PostingKind.Settlement,
            paymentDate,
            lineList,
            documentRef,
            counterpartyName,
            counterpartyDocument,
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
