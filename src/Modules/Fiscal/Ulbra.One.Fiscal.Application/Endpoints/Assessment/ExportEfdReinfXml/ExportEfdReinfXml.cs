using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Fiscal.Application.Assessment;
using Ulbra.One.Fiscal.Domain.EfdReinf;

namespace Ulbra.One.Fiscal.Application.Endpoints.Assessment.ExportEfdReinfXml;

/// <summary>Gera o XML da EFD-Reinf (evento <b>R-4020</b> — IR/CSLL/PIS/COFINS sobre PJ) da competência, para
/// uma empresa (contribuinte), <b>on-the-fly</b> a partir da apuração — sem persistência. <c>tpAmb</c> default 2
/// (homologação). INSS/R-2010 e beneficiário PF/R-4010 ficam fora desta fatia; a natureza do rendimento usa o
/// mapa default (🔶 homologar com a Contabilidade — AEL-627).</summary>
public sealed class ExportEfdReinfXml : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/efd-reinf.xml", Handle).WithName("ExportEfdReinfXml").WithTags("Fiscal")
            .RequireAuthorization();

    private static async Task<IResult> Handle(
        string perApur, CompanyId? companyId, int? tpAmb, IUnitOfWork uow, CancellationToken ct)
    {
        if (!AssessmentPeriodParser.TryParse(perApur, out var period))
            return TypedResults.BadRequest("perApur inválido — use o formato YYYY-MM.");
        if (companyId is not { } cid)
            return TypedResults.BadRequest("Selecione a empresa (contribuinte) para exportar o XML.");

        var company = await uow.ReadSet<Company>().FirstOrDefaultAsync(c => c.Id == cid.Value, ct);
        if (company is null) return TypedResults.NotFound();

        var facts = await WithholdingFactLoader.LoadAsync(uow, new AssessmentFilter(period, cid.Value), ct);
        var items = ReinfR4020Projection.FromFacts(facts);

        // Contribuinte = raiz (8 primeiros dígitos do CNPJ, tolerante a máscara/alfanumérico).
        var raiz = new string(company.Cnpj.Where(char.IsLetterOrDigit).Take(8).ToArray());
        var input = new ReinfR4020Input(period.PerApur, tpAmb ?? 2,
            new ReinfContribuinte(raiz, company.LegalName), items);

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + EfdReinfXmlBuilder.Build(input);
        return TypedResults.File(Encoding.UTF8.GetBytes(xml), "application/xml",
            $"efd-reinf-r4020-{raiz}-{period.PerApur}.xml");
    }
}
