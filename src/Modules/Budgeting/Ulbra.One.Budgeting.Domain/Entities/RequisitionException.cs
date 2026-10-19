using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>Signals a violation of a requisition rule, carrying a stable <see cref="Error.Code"/> so
/// callers map each rule 1:1 to a response without string matching (see <see cref="RequisitionErrors"/>).</summary>
public sealed class RequisitionException(Error error)
    : InvalidOperationException(error.Description)
{
    public Error Error { get; } = error;
}

/// <summary>Stable error codes for requisition rules. Shared by the domain transitions and the endpoints
/// so the same rule always yields the same code (the code is the frontend i18n key).</summary>
public static class RequisitionErrors
{
    public static readonly Error InvalidTransition = Error.Validation(
        ErrorCodes.Requisition.InvalidTransition, "Invalid requisition status transition.");

    public static readonly Error NotEditable = Error.Validation(
        ErrorCodes.Requisition.NotEditable, "Only a draft (or a requisition sent back for complement) can be edited.");

    public static readonly Error NotDeletable = Error.Validation(
        ErrorCodes.Requisition.NotDeletable, "Only a draft requisition can be deleted; submitted ones are cancelled.");

    public static readonly Error BudgetExceeded = Error.Validation(
        ErrorCodes.Requisition.BudgetExceeded, "Estimate exceeds the available budget for the cost center/unit in one or more competencies.");

    public static readonly Error InstallmentsRequired = Error.Validation(
        ErrorCodes.Requisition.InstallmentsRequired, "At least one payment installment is required to submit the financial complement.");

    public static readonly Error IssueDateRequired = Error.Validation(
        ErrorCodes.Requisition.IssueDateRequired, "The emission date is required to submit the financial complement.");

    public static readonly Error IssueDateAfterFirstDueDate = Error.Validation(
        ErrorCodes.Requisition.IssueDateAfterFirstDueDate, "The emission date cannot be after the earliest installment due date.");

    public static readonly Error HasPayments = Error.Validation(
        ErrorCodes.Requisition.HasPayments, "A requisition with registered payments cannot be cancelled; reverse the payments in Finance first.");

    // ---- Recurrence (Conta Fixa) cycles, AEL-877 -----------------------------------

    public static readonly Error RecurringPlanInvalid = Error.Validation(
        ErrorCodes.Requisition.RecurringPlanInvalid, "A recurring requisition (Conta Fixa) uses a Monthly estimate (the PER-MONTH value) and no installments count — later months are launched as cycles.");

    public static readonly Error RecurringSingleInstallment = Error.Validation(
        ErrorCodes.Requisition.RecurringSingleInstallment, "A recurring requisition's financial complement authors exactly one installment (the first cycle's invoice); later months are launched as cycles.");

    // A draft may hold a wrong-length boleto line; the 47/48 rule is enforced only on the Draft→Triage transition.
    public static readonly Error BoletoDigitableLineInvalid = Error.Validation(
        ErrorCodes.Requisition.BoletoDigitableLineInvalid, "A boleto installment requires a linha digitável with 47 (bancário) or 48 (convênio, starting with 8) digits.");

    // A draft may hold an incomplete PIX copia-e-cola; the EMV/CRC rule is enforced only on Draft→Triage.
    public static readonly Error PixPayloadEmvInvalid = Error.Validation(
        ErrorCodes.Requisition.PixPayloadEmvInvalid, "A PIX copia e cola installment requires a valid EMV/BR Code (correct structure and CRC).");

    public static readonly Error NotRecurring = Error.Validation(
        ErrorCodes.Requisition.NotRecurring, "This action is only valid for a recurring requisition (Conta Fixa).");

    public static readonly Error NotInPayment = Error.Validation(
        ErrorCodes.Requisition.NotInPayment, "Recurrence cycles can only be launched while the requisition is in payment.");

    public static readonly Error CycleCompetencyInvalid = Error.Validation(
        ErrorCodes.Requisition.CycleCompetencyInvalid, "The cycle competency must be a valid \"YYYY-MM\" value.");

    public static readonly Error CycleBeforeStart = Error.Validation(
        ErrorCodes.Requisition.CycleBeforeStart, "A cycle competency must be after the requisition's start competency (the start is the base cycle).");

    public static readonly Error CycleAfterEnd = Error.Validation(
        ErrorCodes.Requisition.CycleAfterEnd, "A cycle competency cannot be after the terminated recurrence's end competency.");

    public static readonly Error CycleCompetencyTaken = Error.Validation(
        ErrorCodes.Requisition.CycleCompetencyTaken, "A cycle already exists for this competency.");

    public static readonly Error EndBeforeStart = Error.Validation(
        ErrorCodes.Requisition.EndBeforeStart, "The end competency cannot be before the start competency.");

    public static readonly Error EndBeforeLastCycle = Error.Validation(
        ErrorCodes.Requisition.EndBeforeLastCycle, "The end competency cannot be before the last launched cycle.");

    public static readonly Error FixedExpenseDuplicate = Error.Validation(
        ErrorCodes.Requisition.FixedExpenseDuplicate, "An active Conta Fixa already exists for this vendor, unit and natureza (cost center).");
}
