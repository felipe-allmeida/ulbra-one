using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.ListWithholdingRules;

public sealed class ListWithholdingRules : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListWithholdingRules").WithTags("Fiscal")
            .WithSummary("Lists withholding rules.")
            .WithDescription("The rule table that decides, per tax, whether a vendor's invoice is withheld "
                + "and at what rate. Ordered by tax then LC 116 subitem. A reference list — no paging. "
                + "Deleted rules are soft-deleted and never appear.")
            .RequirePermission(Permissions.FiscalRead).Produces<List<WithholdingRuleResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [SwaggerSchema("Include rules that are inactive. Omitted or false returns only active ones.")]
        bool? includeInactive,
        CancellationToken ct)
    {
        var q = uow.ReadSet<WithholdingRule>();
        if (includeInactive != true) q = q.Where(r => r.Active);
        var rows = await q.OrderBy(r => r.TaxType).ThenBy(r => r.Lc116Subitem).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(WithholdingRuleResponse.From).ToList());
    }
}
