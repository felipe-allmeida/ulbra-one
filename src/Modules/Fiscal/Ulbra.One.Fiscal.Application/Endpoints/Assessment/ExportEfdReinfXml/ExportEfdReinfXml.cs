using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
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
            .RequireAuthorization();

    private static async Task<IResult> Handle(
        string perApur, CompanyId? companyId, int? tpAmb, IUnitOfWork uow, CancellationToken ct)
    {
        if (!AssessmentPeriodParser.TryParse(perApur, out var period))
            return TypedResults.Problem(detail: "Invalid perApur — expected the format YYYY-MM.",
                statusCode: 400, title: "Assessment.InvalidPeriod");
        if (companyId is not { } cid)
            return TypedResults.Problem(detail: "Select the company (taxpayer) to export the XML.",
                statusCode: 400, title: "Assessment.CompanyRequired");

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
