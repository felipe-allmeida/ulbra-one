using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Application.Assessment;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Endpoints.Assessment.GetFiscalAssessment;

/// <summary>Consolidated withholding assessment (apuração de retenções) for a competência
/// (<c>perApur</c> = YYYY-MM): headline totals + breakdowns by company/establishment, by retention group (guia),
/// by tax and by cost center, plus the per-line facts for drill-down. Format-agnostic — the EFD-Reinf export is
/// a separate adapter. Optional filters: <c>companyId</c>, <c>costCenterId</c>, <c>taxType</c>,
/// <c>retentionGroup</c>.</summary>
public sealed class GetFiscalAssessment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetFiscalAssessment").WithTags("Fiscal")
            .RequireAuthorization().Produces<WithholdingAssessmentResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        string perApur, CompanyId? companyId, CostCenterId? costCenterId,
        WithholdingTaxType? taxType, RetentionGroup? retentionGroup,
        IUnitOfWork uow, CancellationToken ct)
    {
        if (!TryParsePerApur(perApur, out var period))
            return TypedResults.BadRequest("perApur inválido — use o formato YYYY-MM.");

        var filter = new AssessmentFilter(period, companyId?.Value, costCenterId?.Value, taxType, retentionGroup);
        var facts = await WithholdingFactLoader.LoadAsync(uow, filter, ct);
        var assessment = WithholdingAssessmentCalculator.Build(facts);
        return TypedResults.Ok(WithholdingAssessmentResponse.From(period.PerApur, companyId, assessment));
    }

    private static bool TryParsePerApur(string? value, out AssessmentPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return false;
        if (year is < 2000 or > 9999 || month is < 1 or > 12) return false;
        period = new AssessmentPeriod(year, month);
        return true;
    }
}
