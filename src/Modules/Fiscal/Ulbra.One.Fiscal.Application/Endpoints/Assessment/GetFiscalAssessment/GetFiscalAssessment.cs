using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
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
            .RequirePermission(Permissions.FiscalRead).Produces<WithholdingAssessmentResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        string perApur, CompanyId? companyId, CostCenterId? costCenterId,
        WithholdingTaxType? taxType, RetentionGroup? retentionGroup,
        IUnitOfWork uow, CancellationToken ct)
    {
        if (!AssessmentPeriodParser.TryParse(perApur, out var period))
            return TypedResults.Problem(detail: "Invalid perApur — expected the format YYYY-MM.",
                statusCode: 400, title: "Assessment.InvalidPeriod");

        var filter = new AssessmentFilter(period, companyId?.Value, costCenterId?.Value, taxType, retentionGroup);
        var facts = await WithholdingFactLoader.LoadAsync(uow, filter, ct);
        var assessment = WithholdingAssessmentCalculator.Build(facts);
        return TypedResults.Ok(WithholdingAssessmentResponse.From(period.PerApur, companyId, assessment));
    }
}
