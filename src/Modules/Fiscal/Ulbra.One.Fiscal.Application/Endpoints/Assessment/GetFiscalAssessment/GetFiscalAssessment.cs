using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
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
            .WithSummary("Assesses the withholdings of a competency.")
            .WithDescription("Consolidated withholding assessment (apuração de retenções) for one "
                + "competency: headline totals plus breakdowns by company, retention group (guia), tax and "
                + "cost center, and the per-line facts behind them for drill-down. Computed on the fly from "
                + "the settled payments — nothing is persisted, so re-running it always reflects current "
                + "data. Format-agnostic: the EFD-Reinf XML is a separate export off the same figures.")
            .RequirePermission(Permissions.FiscalRead).Produces<WithholdingAssessmentResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] FiscalAssessmentRequest req,
        IUnitOfWork uow, CancellationToken ct)
    {
        var (perApur, companyId, costCenterId, taxType, retentionGroup) =
            (req.PerApur, req.CompanyId, req.CostCenterId, req.TaxType, req.RetentionGroup);
        if (!AssessmentPeriodParser.TryParse(perApur, out var period))
            return ErrorResults.InvalidRequest("Invalid perApur — expected the format YYYY-MM.", ErrorCodes.Assessment.InvalidPeriod);

        var filter = new AssessmentFilter(period, companyId?.Value, costCenterId?.Value, taxType, retentionGroup);
        var facts = await WithholdingFactLoader.LoadAsync(uow, filter, ct);
        var assessment = WithholdingAssessmentCalculator.Build(facts);
        return TypedResults.Ok(WithholdingAssessmentResponse.From(period.PerApur, companyId, assessment));
    }
}
