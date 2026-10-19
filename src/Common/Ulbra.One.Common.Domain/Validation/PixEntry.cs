namespace Ulbra.One.Common.Domain.Validation;

/// <summary>
/// Normalizes a PIX installment's two inputs — the per-payment key override (<c>varchar(100)</c>) and the "copia e
/// cola" payload (<c>varchar(512)</c>) — before they are stored. The payload is always kept whole — the CNAB
/// generation later extracts the short addressing URL from it (AEL-963).
/// </summary>
public static class PixEntry
{
    /// <summary>Returns the (key, payloadEmv) pair to persist. Trims blanks to null.</summary>
    public static (string? Key, string? PayloadEmv) Resolve(string? key, string? payloadEmv)
    {
        key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        payloadEmv = string.IsNullOrWhiteSpace(payloadEmv) ? null : payloadEmv.Trim();
        return (key, payloadEmv);
    }
}
