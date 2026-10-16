using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions;

/// <summary>Shared validation + projection of a submitted financial plan (installments + single emission date),
/// returning RFC7807 problems with stable title codes (the frontend i18n contract). Used by the financial
/// complement (required) and the front-load path on create/update (optional). Mirrors the guards so all
/// three agree; domain-level rules (IssueDateAfterFirstDueDate, RecurringSingleInstallment) are enforced by the
/// aggregate. Reuses <see cref="FinancialComplementInstallment"/> as the single installment CLR type so
/// Swashbuckle emits one OpenAPI schema.</summary>
internal static class FinancialPlanValidation
{
    /// <summary>Validates the plan and projects it to the domain tuple shape. With <paramref name="required"/>
    /// false (front-load), an entirely-absent plan (no installments and no issue date) yields
    /// <paramref name="plan"/> = null with no error — the caller then clears any prior plan. Returns false and
    /// sets <paramref name="error"/> on a validation failure.</summary>
    public static bool TryBuild(
        DateOnly? issueDate,
        IReadOnlyList<FinancialComplementInstallment>? installments,
        bool required,
        out IReadOnlyList<(DateOnly DueDate, decimal Amount, PaymentMethod Method, string? BoletoDigitableLine, string? PixKeyOverride, string? PixPayloadEmv)>? plan,
        out IResult? error,
        // false (create/update draft) STORES a wrong-length boleto line instead of rejecting it — the draft (and
        // its read boleto attachment) saves; the 47/48 rule is enforced at Submit. Complement submit keeps it true.
        bool enforceBoletoLine = true)
    {
        plan = null;
        error = null;

        var provided = installments is { Count: > 0 } || issueDate is not null;
        if (!provided)
        {
            if (required) error = Problem(ErrorCodes.Requisition.InstallmentsRequired, "At least one installment is required.");
            return !required;
        }

        if (installments is null || installments.Count == 0)
        {
            error = Problem(ErrorCodes.Requisition.InstallmentsRequired, "At least one installment is required.");
            return false;
        }
        if (installments.Any(i => i.Amount <= 0))
        {
            error = Problem(ErrorCodes.Requisition.InstallmentAmountInvalid, "Each installment amount must be greater than zero.");
            return false;
        }
        if (issueDate is not DateOnly issue || issue == default)
        {
            error = Problem(ErrorCodes.Requisition.IssueDateRequired, "The emission date is required.");
            return false;
        }
        // A boleto installment must carry a valid linha digitável, validated at entry so an invalid/missing
        // barcode never becomes a silent remittance-time skip (AEL-932). Mirrors the CNAB remittance rule
        // (Finance CnabFieldValidation.BoletoReason): 47 digits (bancário) or 48 (convênio, starting with 8).
        if (enforceBoletoLine
            && installments.Any(i => i.PaymentMethod == PaymentMethod.Boleto && !BoletoLine.IsValid(i.BoletoDigitableLine)))
        {
            error = Problem(ErrorCodes.Requisition.BoletoDigitableLineInvalid,
                "A boleto installment requires a linha digitável with 47 (bancário) or 48 (convênio, starting with 8) digits.");
            return false;
        }

        // PIX key vs copia-e-cola (AEL-963). A copia-e-cola is intrinsically long (the full EMV/BR Code); when
        // pasted into the key field it is auto-routed to its own (wide) column by PixEntry — so it is never
        // rejected/truncated for being long, and never overflows the 100-char key column (which surfaced as a
        // raw 500). Validity (structure + CRC) is enforced like the boleto rule: a draft may hold an incomplete
        // paste (enforceBoletoLine=false); Submit and the remittance guard catch it.
        foreach (var i in installments.Where(x => x.PaymentMethod == PaymentMethod.Pix))
        {
            var (key, payloadEmv) = PixEntry.Resolve(i.PixKeyOverride, i.PixPayloadEmv);

            // Both a real key AND an explicit copia-e-cola → ambiguous.
            if (!string.IsNullOrWhiteSpace(i.PixKeyOverride) && !string.IsNullOrWhiteSpace(i.PixPayloadEmv))
            {
                error = Problem(ErrorCodes.Requisition.PixPayloadEmvInvalid,
                    "Provide either a PIX key override or a copia e cola for the installment, not both.");
                return false;
            }
            if (enforceBoletoLine && payloadEmv is not null)
            {
                var r = PixPayloadEmvParser.Parse(payloadEmv);
                if (!(r.IsValid && r.CrcValid))
                {
                    error = Problem(ErrorCodes.Requisition.PixPayloadEmvInvalid,
                        "The PIX copia e cola is invalid (structure or CRC).");
                    return false;
                }
            }
            // A plain key over the CNAB/column width (a real key is <= 77) is malformed — clean 400, never a 500.
            if (key is { Length: > 100 })
            {
                error = Problem(ErrorCodes.Requisition.PixPayloadEmvInvalid,
                    "The PIX key is invalid (too long).");
                return false;
            }
        }

        plan = installments.Select(i => (i.DueDate, i.Amount, i.PaymentMethod,
            i.PaymentMethod == PaymentMethod.Boleto ? i.BoletoDigitableLine : null,
            i.PaymentMethod == PaymentMethod.Pix ? i.PixKeyOverride : null,
            i.PaymentMethod == PaymentMethod.Pix ? i.PixPayloadEmv : null)).ToList();
        return true;
    }

    private static IResult Problem(string errorCode, string detail) =>
        ErrorResults.InvalidRequest(detail, errorCode);
}
