using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.ListWithholdingRules;

public sealed class ListWithholdingRules : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListWithholdingRules").WithTags("Fiscal")
            .RequireAuthorization().Produces<List<WithholdingRuleResponse>>(StatusCodes.Status200OK);

    // vendorId filters to a single vendor's own rules (the "Retenções" section of a vendor). Omitted = all rules.
    private static async Task<IResult> Handle(IUnitOfWork uow, bool? includeInactive, VendorId? vendorId, CancellationToken ct)
    {
        var q = uow.ReadSet<WithholdingRule>();
        if (includeInactive != true) q = q.Where(r => r.Active);
        if (vendorId is { } vid) q = q.Where(r => r.VendorId == vid.Value);
        var rows = await q.OrderBy(r => r.TaxType).ThenBy(r => r.Lc116Subitem).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(WithholdingRuleResponse.From).ToList());
    }
}
