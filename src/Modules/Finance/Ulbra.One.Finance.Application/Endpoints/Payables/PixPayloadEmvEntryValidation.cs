using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>
/// Validates installment PIX "copia e cola" payloads at payable entry (shift-left, AEL-963): a
/// <em>provided</em> payload must be a well-formed EMV/BR Code with a valid CRC, so a malformed paste is
/// rejected up front instead of silently becoming a remittance-time skip. A blank payload is allowed on
/// purpose — it just means this installment is not paid by a PIX QR. Mirrors <see cref="BoletoEntryValidation"/>.
/// </summary>
public static class PixPayloadEmvEntryValidation
{
    /// <summary>Returns the envelope error when an installment has both a key override AND a copia-e-cola
    /// (mutually exclusive — mirrors the requisition path), or a provided copia-e-cola is malformed; else
    /// <c>null</c>. Each item is the installment's (key override, copia-e-cola) pair as stored.</summary>
    public static IResult? Problem(IReadOnlyList<(string? PixKeyOverride, string? PixPayloadEmv)> installments)
    {
        foreach (var (key, payload) in installments)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(payload))
                return ErrorResults.InvalidRequest(
                    "Informe a chave PIX OU o PIX Copia e Cola da parcela, não os dois.",
                    ErrorCodes.Payable.PixPayloadEmvInvalid);
            if (string.IsNullOrWhiteSpace(payload))
                continue;
            var r = PixPayloadEmvParser.Parse(payload);
            if (!(r.IsValid && r.CrcValid))
                return ErrorResults.InvalidRequest(
                    "O PIX Copia e Cola informado é inválido (estrutura ou CRC incorretos).",
                    ErrorCodes.Payable.PixPayloadEmvInvalid);
        }
        return null;
    }
}
