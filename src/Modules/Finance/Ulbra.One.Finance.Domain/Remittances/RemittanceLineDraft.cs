namespace Ulbra.One.Finance.Domain.Remittances;

/// <summary>
/// Pure input snapshot for a single line to be added to a <see cref="Entities.Remittance"/>.
/// <c>Reference</c> is NOT supplied here — it is computed by the aggregate inside <c>AddLine</c>
/// as <c>$"{SequenceNumber:D6}{Sequence:D5}"</c>.
/// </summary>
public record RemittanceLineDraft(
    Guid PayableId,
    Guid InstallmentId,
    PaymentMethod Method,
    decimal Amount,
    string FavoredName,
    string FavoredDocument,
    string FavoredBankCode,
    string FavoredAgency,
    string FavoredAccount,
    string FavoredAccountDigit,
    string FavoredPixKey
);
