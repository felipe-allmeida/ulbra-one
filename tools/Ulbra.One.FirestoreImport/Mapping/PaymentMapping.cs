using Ulbra.One.FirestoreImport.Source;

namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Maps a legacy `pagamentos` document onto the One's Finance Payable/Installment/Payment enums.
/// Enum storage in Finance is MIXED: PaymentMethod and InstallmentStatus are ints (ordinals); PayableStatus
/// and PayableType are strings (enum names). Values verified against Finance.Domain/Enums.cs and
/// Common.Domain/PaymentMethod.cs.</summary>
public static class PaymentMapping
{
    // PaymentMethod ordinals (Common.Domain/PaymentMethod.cs): Pix=0, Ted=1, Boleto=2, Cash=3, DirectDebit=4, Other=5.
    public const int MethodPix = 0;
    public const int MethodTed = 1;
    public const int MethodBoleto = 2;
    public const int MethodOther = 5;

    // InstallmentStatus ordinals (Finance.Domain/Enums.cs): Open=0, PartiallyPaid=1, Paid=2, InRemittance=3.
    public const int InstallmentOpen = 0;
    public const int InstallmentPaid = 2;
    public const int InstallmentInRemittance = 3;

    /// <summary>Legacy free-text payment method → PaymentMethod ordinal.</summary>
    public static int MapMethod(string? legacy) => (legacy ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "transferência bancária" or "transferencia bancaria" => MethodTed,
        "boleto bancário" or "boleto bancario" => MethodBoleto,
        "chave pix" or "pix copia e cola" => MethodPix,
        _ => MethodOther, // "informar posteriormente" / unknown
    };

    /// <summary>A paid installment is one whose legacy status settled the money (has a real baixa).</summary>
    public static bool IsPaid(string? legacyStatus) =>
        (legacyStatus ?? string.Empty).Trim() is "Pago" or "Contabilizado";

    public static bool IsCancelled(string? legacyStatus) =>
        string.Equals((legacyStatus ?? string.Empty).Trim(), "Cancelado", StringComparison.OrdinalIgnoreCase);

    public static bool IsReturned(string? legacyStatus) =>
        string.Equals((legacyStatus ?? string.Empty).Trim(), "Devolvido", StringComparison.OrdinalIgnoreCase);

    /// <summary>Legacy `pagamento.status` → InstallmentStatus ordinal.</summary>
    public static int MapInstallmentStatus(string? legacyStatus) => (legacyStatus ?? string.Empty).Trim() switch
    {
        "Pago" or "Contabilizado" => InstallmentPaid,
        "Fila_Remessa" => InstallmentInRemittance,
        _ => InstallmentOpen, // Fila_Pagamento, Pendente_Aprovação, Avaliacao_Imposto, Devolvido
    };

    // PayableStatus is stored as the enum NAME (string).
    public const string PayableReceived = "Received";
    public const string PayablePendingAccounting = "PendingAccounting";
    public const string PayablePendingTaxAnalysis = "PendingTaxAnalysis";
    public const string PayableRejected = "Rejected";
    public const string PayableCancelled = "Cancelled";

    /// <summary>Payable head status from the set of its installments' legacy statuses + whether the Tributário
    /// must evaluate retention first. Decisions: everything lands PendingAccounting (Contábil classifies) even
    /// when already paid; all-cancelled → Cancelled; any returned → Rejected; a still-to-pay Service/FixedExpense
    /// without retention → PendingTaxAnalysis.</summary>
    public static string MapPayableStatus(IReadOnlyCollection<string?> installmentStatuses, bool taxAnalysisFirst)
    {
        if (installmentStatuses.Count > 0 && installmentStatuses.All(IsCancelled)) return PayableCancelled;
        if (installmentStatuses.Any(IsReturned)) return PayableRejected;
        var anyPaid = installmentStatuses.Any(IsPaid);
        if (!anyPaid && taxAnalysisFirst) return PayablePendingTaxAnalysis;
        return PayablePendingAccounting;
    }

    /// <summary>The Payable requires Tributário analysis when it is Service/FixedExpense, still to be paid, and
    /// has no retention (the legacy never carries retention, so this is decided by type + not-yet-paid).</summary>
    public static bool RequiresTaxAnalysis(string payableType, bool anyPaid) =>
        !anyPaid && payableType is "Service"; // FixedExpense também é Service no de-para de tipo (ver RequisitionMapping)

    // Firebase Storage object path a legacy blob URL points at (the `fiscalDocumentUrl` etc. are storage paths,
    // not http URLs), e.g. "requisicoes/REQ_x/installment_1/fiscal/uuid.pdf".
    public static string? BlobPath(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
