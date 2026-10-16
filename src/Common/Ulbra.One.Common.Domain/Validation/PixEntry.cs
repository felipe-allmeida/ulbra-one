namespace Ulbra.One.Common.Domain.Validation;

/// <summary>
/// Routes a PIX installment's two inputs — the per-payment key override (<c>varchar(100)</c>) and the "copia e
/// cola" payload (<c>varchar(512)</c>) — to their storage slots. A copia-e-cola is intrinsically long (the full
/// EMV/BR Code), so when one is pasted into the key field (there is no dedicated UI field yet) it is auto-routed
/// to the copia-e-cola slot instead of overflowing the 100-char key column (which surfaced as a raw 500). The
/// payload is always kept whole — the CNAB generation later extracts the short addressing URL from it (AEL-963).
/// </summary>
public static class PixEntry
{
    /// <summary>Returns the (key, payloadEmv) pair to persist. Trims blanks to null; a BR Code sitting in the
    /// key field (and no explicit copia-e-cola) is moved to the payload (copia-e-cola) slot.</summary>
    public static (string? Key, string? PayloadEmv) Resolve(string? key, string? payloadEmv)
    {
        key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        payloadEmv = string.IsNullOrWhiteSpace(payloadEmv) ? null : payloadEmv.Trim();

        // Copia-e-cola pasted into the key field → route it to the wide column instead of overflowing varchar(100).
        if (payloadEmv is null && PixPayloadEmvParser.LooksLikeBrCode(key))
            return (null, key);

        return (key, payloadEmv);
    }
}
