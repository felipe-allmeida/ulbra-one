using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.UpdateWithholdingRule;

public sealed class UpdateWithholdingRule : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateWithholdingRule").WithTags("Fiscal")
            .RequirePermission(Permissions.FiscalWrite)
            .Produces<WithholdingRuleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(WithholdingRuleId id, WithholdingRuleRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var rule = await uow.Set<WithholdingRule>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (rule is null)
            return TypedResults.Problem(detail: "Withholding rule not found.", statusCode: 404, title: "WithholdingRule.NotFound");

        if (req.Retains && req.RateSource == RateSource.Fixed && req.Rate is null or <= 0)
            return TypedResults.Problem(detail: "A retaining Fixed rule requires a positive rate.",
                statusCode: 400, title: "WithholdingRule.RateRequired");

        rule.Update(req.TaxType, req.Retains, req.RateSource, req.Rate, Lc116.Canonical(req.Lc116Subitem),
            req.VendorRegime, req.MunicipalityIbge, req.CompanyId, req.BaseRule, req.MinimumAmount, req.Reason,
            req.Cprb, req.SimplesAnexo);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(WithholdingRuleResponse.From(rule));
    }
}
