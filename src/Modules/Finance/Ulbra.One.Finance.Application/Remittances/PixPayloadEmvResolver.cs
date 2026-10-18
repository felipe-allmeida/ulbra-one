using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>How a PIX "copia e cola" payload resolves for the CNAB remittance.</summary>
public enum PixPayloadEmvKind
{
    /// <summary>Static BR Code — decodes to a plain PIX key; rides the existing forma 45 (Seg A+B) path.</summary>
    StaticKey,
    /// <summary>Dynamic BR Code — carries a payload-location URL; rides forma 47 (Seg J-PIX + J52-PIX).</summary>
    DynamicQr,
    /// <summary>Not a valid copia e cola (bad structure/CRC, or URL longer than the CNAB field).</summary>
    Invalid,
}

/// <summary>Outcome of resolving a copia-e-cola payload for the remittance.</summary>
public sealed record PixPayloadEmvResolution(
    PixPayloadEmvKind Kind,
    string? PixKey = null,
    string? PayloadLocationUrl = null,
    string? ReceiverName = null,
    decimal? Amount = null,
    string? Reason = null);

/// <summary>
/// Bridges <see cref="PixPayloadEmvParser"/> (pure EMV parser) to the CNAB routing rule so the generation service and
/// the field validator agree on how a PIX copia-e-cola payload is treated: static → a plain key (forma 45),
/// dynamic → a payload-location URL (forma 47), anything malformed → a clean skip. Reasons are the internal
/// (English) skip strings translated by <see cref="NothingToRemitDescription"/>.
/// </summary>
public static class PixPayloadEmvResolver
{
    /// <summary>Max length of the payload-location URL that fits Seg J52-PIX 132-210 (X079).</summary>
    public const int PayloadUrlMaxLen = 79;

    // Internal skip reasons — kept in step with NothingToRemitDescription's pt-BR arms.
    public const string ReasonInvalid = "invalid PIX copia e cola";
    public const string ReasonCrc = "PIX copia e cola failed CRC check";
    public const string ReasonUrlTooLong = "PIX QR payload URL exceeds CNAB limit";
    public const string ReasonQrDisabled = "PIX QR dinâmico disabled by config";

    public static PixPayloadEmvResolution Resolve(string? payload)
    {
        if (!PixPayloadEmvParser.LooksLikeBrCode(payload))
            return Invalid(ReasonInvalid);

        var r = PixPayloadEmvParser.Parse(payload);
        if (!r.IsValid)
            return Invalid(ReasonInvalid);
        if (!r.CrcValid)
            return Invalid(ReasonCrc);

        if (r.Initiation == PixPayloadEmvInitiation.Dynamic)
        {
            if (string.IsNullOrEmpty(r.PayloadLocationUrl) || r.PayloadLocationUrl.Length > PayloadUrlMaxLen)
                return Invalid(ReasonUrlTooLong);
            return new PixPayloadEmvResolution(
                PixPayloadEmvKind.DynamicQr,
                PayloadLocationUrl: r.PayloadLocationUrl,
                ReceiverName: r.ReceiverName,
                Amount: r.Amount);
        }

        // Static → the embedded key, normalized to match a manually-entered key. A payload whose POI claims
        // static but carries no key (forged/malformed) normalizes to empty — reject it so it never becomes a
        // blank-key forma 45 (defense-in-depth on a P0 path, AEL-963).
        var staticKey = PixKeyNormalizer.Normalize(r.PixKey);
        return string.IsNullOrEmpty(staticKey)
            ? Invalid(ReasonInvalid)
            : new PixPayloadEmvResolution(PixPayloadEmvKind.StaticKey, PixKey: staticKey);
    }

    private static PixPayloadEmvResolution Invalid(string reason) => new(PixPayloadEmvKind.Invalid, Reason: reason);
}
