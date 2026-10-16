using Swashbuckle.AspNetCore.Annotations;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>Decoded view of a PIX "copia e cola" (EMV/BR Code) — a stateless preview for the requisition/payable
/// form so the operator sees recebedor/valor and whether the QR is static or dynamic before submitting.</summary>
[SwaggerSchema("Decoded PIX copia e cola (EMV/BR Code): validity, static/dynamic modality and the recebedor/valor "
    + "extracted from the payload. Nothing is persisted.")]
public sealed record DecodedPixPayloadEmv(
    [property: SwaggerSchema("Whether the payload is a well-formed PIX BR Code (structure + a br.gov.bcb.pix template).")]
    bool Valid,
    [property: SwaggerSchema("Whether the CRC16 check digit matches — a redacted or tampered payload fails this.")]
    bool CrcOk,
    [property: SwaggerSchema("True for a reusable static QR (decodes to a plain key → CNAB forma 45).")]
    bool IsStatic,
    [property: SwaggerSchema("True for a one-time dynamic QR (payload-location URL → CNAB forma 47).")]
    bool IsDynamic,
    [property: SwaggerSchema("Recebedor name (EMV tag 59), when present.")]
    string? MerchantName,
    [property: SwaggerSchema("Recebedor city (EMV tag 60), when present.")]
    string? MerchantCity,
    [property: SwaggerSchema("Amount encoded in the QR, in BRL — null for a payer-defined-amount QR.")]
    decimal? Amount,
    [property: SwaggerSchema("The PIX key, for a static QR.")]
    string? PixKey,
    [property: SwaggerSchema("The payload-location URL (scheme stripped), for a dynamic QR.")]
    string? Url,
    [property: SwaggerSchema("The QR txid, when present.")]
    string? Txid);
