using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A line of an imported bank statement, and how far it has got in reconciliation. It is "
    + "what the bank says happened — matching it to one of our installments is a separate step.")]
public sealed record BankTransactionResponse(
    BankTransactionId Id,
    [property: SwaggerSchema("Account the statement line belongs to.")]
    BankAccountId BankAccountId,
    [property: SwaggerSchema("Date on the statement.")]
    [property: SwaggerExample("2026-08-09")]
    DateOnly Date,
    [property: SwaggerSchema("Amount on the statement, in BRL. Always positive — the direction is in "
        + "Direction, not the sign.")]
    [property: SwaggerExample("1430.25")]
    decimal Amount,
    [property: SwaggerSchema("Whether money left or arrived.")]
    TransactionDirection Direction,
    [property: SwaggerSchema("Description as the bank wrote it — usually the only clue for matching.")]
    [property: SwaggerExample("TED ENVIADA CONSTRUTORA HORIZONTE LTDA")]
    string Description,
    [property: SwaggerSchema("How far reconciliation has got for this line.")]
    MatchStatus MatchStatus,
    [property: SwaggerSchema("Installment the system thinks this line pays. A SUGGESTION — nothing is "
        + "settled until an operator confirms it.")]
    InstallmentId? SuggestedInstallmentId,
    [property: SwaggerSchema("The payment this line was reconciled to, once confirmed. Null while it is "
        + "still only a suggestion.")]
    PaymentId? MatchedPaymentId,
    [property: SwaggerSchema("True when the reconciliation only LINKED a payment that already existed (a CNAB "
        + "return baixa), instead of creating one. Undoing such a link unlinks only — it never reverses the "
        + "payment, because the conciliation did not create it.")]
    bool PaymentPreexisting,
    [property: SwaggerSchema("When the line was imported.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Why the line is still unmatched. Null once it has been reconciled or ignored, and "
        + "null on the endpoints that do not compute it — only the working list does. It is a reading of the "
        + "pool AS IT STANDS NOW, so it can differ from the verdict the import reached.")]
    ConciliationMismatch? Mismatch = null,
    [property: SwaggerSchema("Installments close enough to this line to be worth showing: the near misses behind "
        + "Mismatch, or the single suggested installment when the matcher did place one. Closest first, capped. "
        + "Empty on a reconciled or ignored line.")]
    IReadOnlyList<ConciliationCandidateResponse>? Candidates = null,
    [property: SwaggerSchema("The título this line was reconciled against — the payable side of the pair. Null "
        + "while the line is still open, and on an ignored line, which has no título behind it.")]
    ConciliationMatchedTitleResponse? MatchedTitle = null)
{
    /// <summary>The line on its own, with no reading of the candidate pool. What the mutation endpoints return:
    /// once a line is confirmed or ignored there is no mismatch left to explain.</summary>
    public static BankTransactionResponse From(BankTransaction t) => From(t, null, null);

    public static BankTransactionResponse From(
        BankTransaction t,
        ConciliationMismatch? mismatch,
        IReadOnlyList<ConciliationCandidateResponse>? candidates) => new(
        new BankTransactionId(t.Id), new BankAccountId(t.BankAccountId), t.Date, t.Amount, t.Direction,
        t.Description, t.MatchStatus,
        t.SuggestedInstallmentId is { } s ? new InstallmentId(s) : null,
        t.MatchedPaymentId is { } p ? new PaymentId(p) : null,
        t.PaymentPreexisting,
        t.CreatedAt,
        mismatch,
        candidates ?? []);
}
