using Microsoft.AspNetCore.Http;
using Ulbra.One.Budgeting.Domain;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments;

/// <summary>Shared field validation for create/update adjustment requests, returning RFC7807 problems with
/// stable title codes (the frontend i18n contract). Dimension resolution (create) and editability (update)
/// are handled by the endpoints. Mirrors <see cref="Requisitions.RequisitionValidation"/>; runs before the
/// domain so ordinary bad input is a 400, not a 500. Returns <c>null</c> when the fields are valid.</summary>
internal static class BudgetAdjustmentValidation
{
    public static IResult? Validate(
        BudgetAdjustmentType type, string competency, string? sourceCompetency,
        decimal value, string justification, string? counterparts)
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Problem(ErrorCodes.BudgetAdjustment.JustificationRequired, "Justification is required.");

        if (value <= 0)
            return Problem(ErrorCodes.BudgetAdjustment.ValueInvalid, "Value must be greater than zero.");

        var dest = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(dest))
            return Problem(ErrorCodes.BudgetAdjustment.CompetencyInvalid, $"Competency '{dest}' is not a valid \"YYYY-MM\" value.");

        if (type == BudgetAdjustmentType.Transfer)
        {
            var source = CompetencyFormat.Normalize(sourceCompetency);
            if (string.IsNullOrWhiteSpace(source))
                return Problem(ErrorCodes.BudgetAdjustment.SourceRequired, "A transfer requires a source competency.");
            if (!CompetencyFormat.IsValid(source))
                return Problem(ErrorCodes.BudgetAdjustment.CompetencyInvalid, $"SourceCompetency '{source}' is not a valid \"YYYY-MM\" value.");
            if (CompetencyFormat.Compare(source, dest) == 0)
                return Problem(ErrorCodes.BudgetAdjustment.SourceEqualsDestination, "A transfer's source and destination competencies must differ.");
        }
        else if (!string.IsNullOrWhiteSpace(sourceCompetency))
        {
            return Problem(ErrorCodes.BudgetAdjustment.SourceNotAllowed, "Only a transfer may carry a source competency.");
        }

        // Counterparts (entregas) justify the extra budget — required except for a Redução (giving budget back).
        if (type != BudgetAdjustmentType.Reduction && string.IsNullOrWhiteSpace(counterparts))
            return Problem(ErrorCodes.BudgetAdjustment.CounterpartsRequired, "Counterparts are required except for a reduction.");

        return null;
    }

    private static IResult Problem(string title, string detail) =>
        ErrorResults.InvalidRequest(detail, title);
}
