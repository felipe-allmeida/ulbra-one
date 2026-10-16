using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Posting;

public sealed record AccountingPostingOptions(
    string PayablesLiabilityCode,
    string CashCode,
    IReadOnlyDictionary<string, string> WithholdingCodes,
    // Year-end closing: the transitory "Apuração do Resultado do Exercício" account the result is cleared into,
    // and the retained-earnings account it is then transferred to (credited on profit, debited on loss).
    string ApuracaoCode = "2.6.20.05.100",
    string RetainedEarningsCode = "2.6.20.05.020",
    // Suspense ("conta transitória") that absorbs the debit of an incurral whose expense account is UNKNOWN —
    // a requisition-sourced payable schedules with a null account, since the AI candidate is only a suggestion.
    // Posting to the suspense keeps the incurral paired with its settlement, so Fornecedores a Pagar is never
    // debited by a baixa that has no matching credit; the accountant's later reclassify moves the expense to
    // its definitive account. Null (the default) restores the previous behaviour: park and post nothing.
    string? UnclassifiedExpenseCode = null);

public interface IPayablePostingService
{
    /// <summary>Books the provision (incurral). <paramref name="competenceDate"/> is the título's competence
    /// (Payable.IssueDate); null falls back to the posting date. <paramref name="costCenterId"/> is set on the
    /// expense (result) line only; <paramref name="unitId"/> (the estabelecimento) is set on every line so the
    /// per-unit trial balance stays balanced. <paramref name="documentRef"/> is the payable's document/NF number, stored on the entry's
    /// human-facing SourceReference. The competence is clamped to the current open period when closed.
    /// <paramref name="counterpartyName"/> (razão social) and <paramref name="counterpartyDocument"/> (CNPJ/CPF)
    /// are stored on the entry for the Livro Razão history only (kept out of the histórico/Description).</summary>
    /// <returns>True when a posting is in place (posted or already posted); false when parked.</returns>
    Task<bool> PostIncurralAsync(Guid companyId, string sourceKey, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct);

    /// <summary>Books the incurral for a (re)scheduled payable, tolerating a prior reversal (AEL-935): idempotent
    /// when a live incurral already exists; when the previous incurral was reversed (title returned then re-approved)
    /// it re-books under the next versioned sourceKey so the AlreadyPosted guard doesn't skip it; otherwise books the
    /// base key (first schedule / still-parked). Same line plan as <see cref="PostIncurralAsync"/>.</summary>
    Task<bool> PostOrRebookIncurralAsync(Guid companyId, Guid payableId, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct);

    /// <summary>Rebooks the incurral after Contábil reclassifies the expense account: reverse the live incurral +
    /// repost under a versioned sourceKey (posted case), or post against the base key + resolve the pending (parked
    /// case). The versioned key sidesteps the AlreadyPosted short-circuit. Idempotent; only the expense line moves.</summary>
    Task ReclassifyIncurralAsync(Guid companyId, Guid payableId, Guid? expenseAccountId, decimal amount,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct);

    /// <summary>Books the settlement (baixa) at <paramref name="paymentDate"/> (clamped to the open period).
    /// The cash credit uses the paying bank's ledger account (<paramref name="cashAccountId"/>) when it resolves
    /// to a postable account, otherwise falls back to the configured global cash code. <paramref name="withholdings"/>
    /// are the CSRF retentions recognized at this baixa: Fornecedores a Pagar is debited for
    /// <c>amount + Σwithholdings</c>, each retention account is credited (taxes sharing an account are summed into
    /// one line), and cash is credited for <c>amount</c> (empty → the classic Dr liability / Cr cash entry).
    /// <paramref name="unitId"/> (the estabelecimento) is set on every line for per-unit tracking.
    /// <paramref name="documentRef"/> is the payable's
    /// document/NF number, stored on the entry's human-facing SourceReference. <paramref name="counterpartyName"/>
    /// (razão social) and <paramref name="counterpartyDocument"/> (CNPJ/CPF) are stored on the entry for the Livro
    /// Razão history only (kept out of the histórico/Description).</summary>
    Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, DateOnly paymentDate, Guid? cashAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings, Guid? unitId, string? documentRef,
        string? counterpartyName, string? counterpartyDocument, CancellationToken ct);

    Task ReverseAsync(Guid companyId, string sourceKey, string reason, CancellationToken ct);

    /// <summary>Refreshes the human-facing SourceReference (número da NF shown in the Livro Razão) on every posted
    /// entry sourced from a payable — the incurral (base + reclassification reposts + their reversals) and each
    /// settlement (found by payment id, base + reversal) — after the número is edited post-schedule/post-payment.
    /// Also refreshes an open parked incurral's stored plan so a later resolve replays the current número.
    /// Metadata-only: no rebook, no reversal, no balance change. Idempotent.</summary>
    Task AmendDocumentReferenceAsync(Guid companyId, Guid payableId, IReadOnlyList<Guid> paymentIds, string? documentRef, CancellationToken ct);

    /// <summary>Replays a parked pending posting from its stored plan; marks it resolved when a posting lands.
    /// Returns false when it still cannot post (missing plan / account, or the target period is still closed).</summary>
    Task<bool> ResolvePendingAsync(PendingAccountingPosting pending, CancellationToken ct);
}
