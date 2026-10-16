namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>Remittance knobs bound from the <c>Cnab</c> config section.</summary>
public sealed class RemittanceOptions
{
    /// <summary>
    /// Emit production NSAs (file sequence ≥ 11). Santander treats NSA 1-10 as a TEST file (manual note
    /// G010): it passes the crítica but is never executed. Null falls back to <c>IsProduction()</c>.
    /// </summary>
    public bool? ProductionRemittance { get; init; }

    /// <summary>
    /// Enables emitting dynamic PIX QR Code ("copia e cola") — forma de lançamento 47, Segmentos J-PIX +
    /// J52-PIX (AEL-963). Until the layout is homologated byte-for-byte against a bank-accepted file, a
    /// dynamic-QR installment is SKIPPED (no forma 47 leaves). Plain PIX keys and static QR (which decode to a
    /// key) are unaffected — they ride the already-validated forma 45.
    /// </summary>
    public bool EnablePixQrCode { get; init; }
}
