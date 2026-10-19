using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>Signals a violation of an accounting-period rule, carrying a stable <see cref="Error.Code"/>
/// so callers map each rule 1:1 to a response without string matching (see <see cref="AccountingPeriodErrors"/>).</summary>
public sealed class AccountingPeriodException(Error error)
    : InvalidOperationException(error.Description)
{
    public Error Error { get; } = error;
}

/// <summary>Stable error codes for accounting-period rules. Shared by the entity transitions and the
/// closed-period guard so the same rule always yields the same code.</summary>
public static class AccountingPeriodErrors
{
    public static readonly Error Closed = Error.Validation(
        ErrorCodes.AccountingPeriod.Closed, "The accounting period for this competence date is closed.");

    public static readonly Error InvalidTransition = Error.Validation(
        ErrorCodes.AccountingPeriod.InvalidTransition, "Invalid accounting period status transition.");
}
