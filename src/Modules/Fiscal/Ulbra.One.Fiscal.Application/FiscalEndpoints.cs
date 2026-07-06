using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Fiscal.Application.Endpoints.Assessment.ExportFiscalAssessmentCsv;
using Ulbra.One.Fiscal.Application.Endpoints.Assessment.GetFiscalAssessment;
using Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.CreateWithholdingRule;
using Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.DeleteWithholdingRule;
using Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.ListWithholdingRules;
using Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.UpdateWithholdingRule;

namespace Ulbra.One.Fiscal.Application;

public static class FiscalEndpoints
{
    public static IEndpointRouteBuilder MapFiscalEndpoints(this IEndpointRouteBuilder builder)
    {
        var rules = builder.MapGroup("/withholding-rules");
        rules.MapEndpoint<ListWithholdingRules>();
        rules.MapEndpoint<CreateWithholdingRule>();
        rules.MapEndpoint<UpdateWithholdingRule>();
        rules.MapEndpoint<DeleteWithholdingRule>();

        // Apuração de retenções (agnóstica de formato) — base do painel de Obrigações Acessórias.
        var assessment = builder.MapGroup("/fiscal/assessment");
        assessment.MapEndpoint<GetFiscalAssessment>();
        assessment.MapEndpoint<ExportFiscalAssessmentCsv>();
        return builder;
    }
}
