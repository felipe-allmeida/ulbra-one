using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>
/// Validates installment boleto lines at payable entry (shift-left, AEL-932): a <em>provided</em> line
/// must be 47 (bancário) or 48 (arrecadação/convênio, which always begins with 8) digits, so a malformed
/// barcode is rejected up front instead of silently becoming a remittance-time skip (or, for a 48-digit
/// line without the 8, an unhandled 500 in the bancário converter). A blank line is allowed on purpose —
/// a boleto payable may be created before its barcode is known, and the payment queue surfaces it as a
/// Manual/BoletoLineMissing pending item until it is filled. Digit rule mirrors
/// <see cref="CnabFieldValidation.BoletoReason"/>.
/// </summary>
public static class BoletoEntryValidation
{
    /// <summary>Returns the envelope error when any provided boleto line is malformed, or <c>null</c>.</summary>
    public static IResult? Problem(IReadOnlyList<string?> boletoLines)
    {
        foreach (var line in boletoLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (CnabFieldValidation.BoletoReason(line) is not null)
                return ErrorResults.InvalidRequest(
                    "The boleto linha digitável must have 47 (bancário) or 48 (convênio, starting with 8) digits.",
                    ErrorCodes.Payable.BoletoDigitableLineInvalid);
        }
        return null;
    }
}
