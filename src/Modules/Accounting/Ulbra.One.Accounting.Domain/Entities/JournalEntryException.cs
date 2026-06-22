using Ulbra.One.Common.Domain;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>Signals a violation of a journal-entry structural invariant, carrying a stable
/// <see cref="Error.Code"/> so callers can map each rule 1:1 to a response without string matching.
/// The domain is the single source of truth for these codes (see <see cref="JournalEntryErrors"/>).</summary>
public sealed class JournalEntryException(Error error)
    : InvalidOperationException(error.Description)
{
    public Error Error { get; } = error;
}

/// <summary>Stable error codes for the journal-entry structural invariants. Shared by every path
/// that enforces them (Post, Reverse, create/update validation) so the same rule always yields the
/// same code.</summary>
public static class JournalEntryErrors
{
    public static readonly Error TooFewLines = Error.Validation(
        "JournalEntry.TooFewLines", "A journal entry needs at least two lines.");

    public static readonly Error NonPositiveAmount = Error.Validation(
        "JournalEntry.NonPositiveAmount", "All line amounts must be positive.");

    public static readonly Error Unbalanced = Error.Validation(
        "JournalEntry.Unbalanced", "Debits and credits must balance.");
}
