using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>Signals a violation of a budget-adjustment (adequação) rule, carrying a stable
/// <see cref="Error.Code"/> so callers map each rule 1:1 to a response without string matching
/// (see <see cref="BudgetAdjustmentErrors"/>). Mirrors <see cref="RequisitionException"/>.</summary>
public sealed class BudgetAdjustmentException(Error error)
    : InvalidOperationException(error.Description)
{
    public Error Error { get; } = error;
}

/// <summary>Stable error codes for budget-adjustment rules. Shared by the domain transitions, the
/// budget-cell ceiling guard and the endpoints so the same rule always yields the same code
/// (the code is the frontend i18n key).</summary>
public static class BudgetAdjustmentErrors
{
    public static readonly Error InvalidTransition = Error.Validation(
        ErrorCodes.BudgetAdjustment.InvalidTransition, "Invalid budget-adjustment status transition.");

    public static readonly Error NotEditable = Error.Validation(
        ErrorCodes.BudgetAdjustment.NotEditable, "Only an adjustment still awaiting triage can be edited.");

    public static readonly Error NotDeletable = Error.Validation(
        ErrorCodes.BudgetAdjustment.NotDeletable, "Only an adjustment still awaiting triage can be deleted; others are cancelled.");

    public static readonly Error SourceRequired = Error.Validation(
        ErrorCodes.BudgetAdjustment.SourceRequired, "A transfer requires a source competency.");

    public static readonly Error SourceNotAllowed = Error.Validation(
        ErrorCodes.BudgetAdjustment.SourceNotAllowed, "Only a transfer may carry a source competency.");

    public static readonly Error SourceEqualsDestination = Error.Validation(
        ErrorCodes.BudgetAdjustment.SourceEqualsDestination, "A transfer's source and destination competencies must differ.");

    public static readonly Error CellNotFound = Error.Validation(
        ErrorCodes.BudgetAdjustment.CellNotFound, "No budget cell exists for this unit, cost center and competency to reduce.");

    public static readonly Error LimitWouldGoNegative = Error.Validation(
        ErrorCodes.BudgetAdjustment.LimitWouldGoNegative, "The adjustment would drive the budget ceiling below zero.");

    public static readonly Error ApprovalLimitExceeded = Error.Validation(
        ErrorCodes.BudgetAdjustment.ApprovalLimitExceeded, "Value exceeds the approver's authorization limit (alçada).");
}
