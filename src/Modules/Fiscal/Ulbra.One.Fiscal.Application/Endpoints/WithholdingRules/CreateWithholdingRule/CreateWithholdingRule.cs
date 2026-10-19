using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.CreateWithholdingRule;

public sealed class CreateWithholdingRule : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateWithholdingRule").WithTags("Fiscal")
            .WithSummary("Creates a withholding rule.")
            .WithDescription("Adds a rule to the withholding table. A rule matches a vendor tax profile — "
                + "tax, LC 116 subitem, regime, municipality, CPRB and Simples annex — and each of those "
                + "left null widens the match. A company-specific rule takes precedence over the "
                + "system-wide default for the same profile. A retaining rule with a fixed rate needs a "
                + "positive rate, otherwise 400.")
            .RequirePermission(Permissions.FiscalWrite)
            .Produces<WithholdingRuleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(WithholdingRuleRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        if (req.Retains && req.RateSource == RateSource.Fixed && req.Rate is null or <= 0)
            return ErrorResults.InvalidRequest("A retaining Fixed rule requires a positive rate.", ErrorCodes.WithholdingRule.RateRequired);

        var rule = new WithholdingRule(req.TaxType, req.Retains, req.RateSource, req.Rate,
            Lc116.Canonical(req.Lc116Subitem), req.VendorRegime, req.MunicipalityIbge, req.CompanyId, req.BaseRule,
            req.MinimumAmount, req.Reason, req.Cprb, req.SimplesAnexo);
        await uow.AddAsync(rule, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/withholding-rules/{new WithholdingRuleId(rule.Id)}", WithholdingRuleResponse.From(rule));
    }
}
