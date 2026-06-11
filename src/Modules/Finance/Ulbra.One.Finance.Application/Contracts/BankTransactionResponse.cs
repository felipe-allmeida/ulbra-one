using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record BankTransactionResponse(
    BankTransactionId Id, BankAccountId BankAccountId, DateOnly Date, decimal Amount, TransactionDirection Direction,
    string Description, MatchStatus MatchStatus, InstallmentId? SuggestedInstallmentId, PaymentId? MatchedPaymentId,
    DateTimeOffset CreatedAt)
{
    public static BankTransactionResponse From(BankTransaction t) => new(
        new BankTransactionId(t.Id), new BankAccountId(t.BankAccountId), t.Date, t.Amount, t.Direction,
        t.Description, t.MatchStatus,
        t.SuggestedInstallmentId is { } s ? new InstallmentId(s) : null,
        t.MatchedPaymentId is { } p ? new PaymentId(p) : null,
        t.CreatedAt);
}
