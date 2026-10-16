using System.Text;
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
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Endpoints.Assessment.ExportFiscalAssessmentCsv;

/// <summary>Exports the withholding assessment of a competência as a CSV of "valores retidos" (uma linha por
/// retenção) — agnóstico de formato (sem códigos de evento EFD-Reinf). Mesmos filtros do painel.</summary>
public sealed class ExportFiscalAssessmentCsv : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/csv", Handle).WithName("ExportFiscalAssessmentCsv").WithTags("Fiscal")
            .WithSummary("Exports the withholding assessment as CSV.")
            .WithDescription("The same figures as the assessment panel, flattened to one line per "
                + "withholding. Takes the same filters. Format-agnostic — no EFD-Reinf event codes — and "
                + "written with a UTF-8 BOM so Excel in pt-BR renders the accents correctly.")
            .RequirePermission(Permissions.FiscalRead);

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
        var csv = AssessmentCsv.Build(period.PerApur, facts);

        // UTF-8 BOM so Excel (pt-BR) detects the encoding and renders the accents correctly.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);
        return TypedResults.File(bytes, "text/csv", $"apuracao-retencoes-{period.PerApur}.csv");
    }
}
