using Microsoft.AspNetCore.Http;
using Ulbra.One.Budgeting.Domain;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions;

/// <summary>Shared field validation for create/update requests, returning RFC7807 problems with stable
/// title codes (the frontend i18n contract). Dimension resolution (create) and editability (update)
/// are handled by the endpoints themselves. Returns <c>null</c> when the fields are valid.</summary>
internal static class RequisitionValidation
{
    public static IResult? Validate(
        RequisitionType type, EstimateType estimateType,
        string description, decimal estimateValue, string startCompetency, string? endCompetency, int? installments)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Problem(ErrorCodes.Requisition.DescriptionRequired, "Description is required.");

        if (estimateValue <= 0)
            return Problem(ErrorCodes.Requisition.EstimateInvalid, "EstimateValue must be greater than zero.");

        // Recurring (Conta Fixa): the estimate is the PER-MONTH value, so the plan must be Monthly and without
        // an installments count — mirrors the domain guard in Requisition.SetDetails (AEL-877).
        if (type is RequisitionType.FixedExpense
            && (estimateType != EstimateType.Monthly || installments is not null))
            return Problem(ErrorCodes.Requisition.RecurringPlanInvalid,
                "A recurring requisition uses a Monthly estimate (the per-month value) and no installments count.");

        var start = CompetencyFormat.Normalize(startCompetency);
        if (!CompetencyFormat.IsValid(start))
            return Problem(ErrorCodes.Requisition.CompetencyInvalid, $"StartCompetency '{start}' is not a valid \"YYYY-MM\" value.");

        if (!string.IsNullOrWhiteSpace(endCompetency))
        {
            var end = CompetencyFormat.Normalize(endCompetency);
            if (!CompetencyFormat.IsValid(end))
                return Problem(ErrorCodes.Requisition.CompetencyInvalid, $"EndCompetency '{end}' is not a valid \"YYYY-MM\" value.");
            if (CompetencyFormat.Compare(end, start) < 0)
                return Problem(ErrorCodes.Requisition.CompetencyRangeInvalid, $"EndCompetency '{end}' cannot precede StartCompetency '{start}'.");
        }

        if (installments is int count && count < 1)
            return Problem(ErrorCodes.Requisition.InstallmentsInvalid, "Installments count must be positive when provided.");

        return null;
    }

    private static IResult Problem(string title, string detail) =>
        ErrorResults.InvalidRequest(detail, title);
}
