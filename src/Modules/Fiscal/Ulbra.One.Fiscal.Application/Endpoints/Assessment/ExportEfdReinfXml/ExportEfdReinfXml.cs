using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Fiscal.Application.Assessment;
using Ulbra.One.Fiscal.Domain.EfdReinf;

namespace Ulbra.One.Fiscal.Application.Endpoints.Assessment.ExportEfdReinfXml;

/// <summary>Generates the EFD-Reinf XML ("Obrigações Acessórias") for the competência, for one company
/// (contribuinte), <b>on-the-fly</b> from the apuração — no persistence. One lote carries both event types:
/// <b>R-4020</b> (evtRetPJ — IR/CSLL/PIS/COFINS on PJ) and <b>R-2010</b> (evtServTom — INSS on serviços tomados).
/// <c>tpAmb</c> default 2 (homologação). The PF beneficiary/R-4010 is out of scope; the natureza do rendimento and
/// tipo de serviço use default maps (🔶 to homologate with Accounting — AEL-627).</summary>
public sealed class ExportEfdReinfXml : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/efd-reinf.xml", Handle).WithName("ExportEfdReinfXml").WithTags("Fiscal")
            .WithSummary("Exports the competency's EFD-Reinf XML.")
            .WithDescription("Builds the EFD-Reinf lote for one company and competency, on the fly from "
                + "the assessment — nothing is persisted, so the file always reflects current data. The "
                + "single lote carries both event types: R-4020 (evtRetPJ, for IR/CSLL/PIS/COFINS on legal "
                + "entities) and R-2010 (evtServTom, for INSS on contracted services). Unlike the other "
                + "assessment routes, companyId is REQUIRED here — the taxpayer's CNPJ root is what "
                + "identifies the lote. Individual beneficiaries (R-4010) are out of scope, and the "
                + "natureza do rendimento and tipo de serviço come from default maps still pending "
                + "homologation with Accounting.")
            .RequirePermission(Permissions.FiscalRead);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Assessment period (competência) in `YYYY-MM`. Required.")]
        [SwaggerExample("2026-08")]
        string perApur,
        [SwaggerSchema("Company (contribuinte) whose lote is generated. REQUIRED here, unlike the other "
            + "assessment routes: omitting it returns 400, not a consolidated export.")]
        CompanyId? companyId,
        [SwaggerSchema("EFD-Reinf environment code (tpAmb): 1 production, 2 homologation. Defaults to 2.")]
        [SwaggerExample("2")]
        int? tpAmb,
        IUnitOfWork uow, CancellationToken ct)
    {
        if (!AssessmentPeriodParser.TryParse(perApur, out var period))
            return ErrorResults.InvalidRequest("Invalid perApur — expected the format YYYY-MM.", ErrorCodes.Assessment.InvalidPeriod);
        if (companyId is not { } cid)
            return ErrorResults.InvalidRequest("Select the company (taxpayer) to export the XML.", ErrorCodes.Assessment.CompanyRequired);

        var company = await uow.ReadSet<Company>().FirstOrDefaultAsync(c => c.Id == cid.Value, ct);
        if (company is null) return TypedResults.NotFound();

        var facts = await WithholdingFactLoader.LoadAsync(uow, new AssessmentFilter(period, cid.Value), ct);

        // Contribuinte root: first 8 digits of the CNPJ (no mask — nrInsc in the XSD is \d{8}).
        var root = new string(DocumentValidator.Normalize(company.Cnpj).Take(8).ToArray());
        var taxpayer = new ReinfTaxpayer(root, company.LegalName);
        var ambiente = tpAmb ?? 2;
        var r4020 = new ReinfR4020Input(period.PerApur, ambiente, taxpayer, ReinfR4020Projection.FromFacts(facts));
        var r2010 = new ReinfR2010Input(period.PerApur, ambiente, taxpayer, ReinfR2010Projection.FromFacts(facts));

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + EfdReinfExport.BuildCombinedLote(r4020, r2010);
        return TypedResults.File(Encoding.UTF8.GetBytes(xml), "application/xml",
            $"efd-reinf-{root}-{period.PerApur}.xml");
    }
}
