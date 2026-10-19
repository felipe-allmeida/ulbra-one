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
    string FavoredPixKey,
    DateOnly? DueDate = null,
    string? BoletoDigitableLine = null,
    string FavoredAddressStreet = "",
    string FavoredAddressNumber = "",
    string FavoredAddressComplement = "",
    string FavoredAddressNeighborhood = "",
    string FavoredAddressCity = "",
    string FavoredAddressCep = "",
    string FavoredAddressState = "",
    // PIX QR dinâmico (copia e cola): URL de endereçamento resolvida + nome do recebedor (AEL-963).
    string? PixQrDynamicUrl = null,
    string? PixQrReceiverName = null
);
