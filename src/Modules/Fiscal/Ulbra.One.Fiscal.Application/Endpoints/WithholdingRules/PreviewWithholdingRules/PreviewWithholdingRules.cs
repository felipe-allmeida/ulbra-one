using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Application.Contracts;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Fiscal.Domain.Withholdings;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.PreviewWithholdingRules;

/// <summary>Previews, per tax, what the SYSTEM defaults (vendor-specific rules excluded) would apply for a given
/// vendor tax profile — so the withholding section of a vendor can show what an override would replace. The
/// resolver is the single source of truth; this avoids reimplementing the matching in the client.</summary>
public sealed class PreviewWithholdingRules : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/preview", Handle).WithName("PreviewWithholdingRules").WithTags("Fiscal")
            .WithSummary("Previews the default withholdings for a vendor tax profile.")
            .WithDescription("Answers, per tax, what the SYSTEM would withhold for a given vendor tax "
                + "profile — vendor-specific overrides deliberately excluded, so the vendor form can show "
                + "what an override would be replacing. It runs the real resolver rather than "
                + "reimplementing the matching client-side, which is what keeps the preview honest. ISS "
                + "comes back flagged as invoice-dependent: its rate follows the invoice's municipality "
                + "and cannot be resolved at registration time.")
            .RequirePermission(Permissions.FiscalRead).Produces<List<WithholdingPreviewItem>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [SwaggerSchema("Vendor tax regime to simulate. Required.")]
        TaxRegime regime,
        [SwaggerSchema("Company whose rules to include alongside the system-wide ones. Omit to consider "
            + "only system-wide rules.")]
        CompanyId? companyId,
        [SwaggerSchema("Vendor's CPRB status to simulate. Omit if unknown.")]
        bool? cprb,
        [SwaggerSchema("Vendor's Simples Nacional annex to simulate. Omit if not applicable.")]
        SimplesAnexo? simplesAnexo,
        CancellationToken ct)
    {
        // System/company rules — the preview is the system default for the vendor's tax profile.
        var rules = await uow.ReadSet<WithholdingRule>()
            .Where(r => r.Active
                && (r.CompanyId == null || (companyId != null && r.CompanyId == companyId.Value.Value)))
            .ToListAsync(ct);

        // A single synthetic service line: no subitem/municipality/invoice → federal + INSS resolve; ISS stays
        // invoice-dependent (rate comes from the municipality/invoice, not resolvable at registration time).
        var ctx = new ResolutionContext(
            companyId?.Value, regime, [new ServiceLine(null, null, null, 100m)], cprb, simplesAnexo);

        var items = WithholdingRuleResolver.ResolvePerTax(rules, ctx)
            .Select(x => new WithholdingPreviewItem(
                x.Tax,
                Retains: x.Rule?.Retains ?? false,
                Reason: x.Rule?.Reason ?? NonRetentionReason.None,
                RateSource: x.Rule?.RateSource ?? RateSource.Fixed,
                Rate: x.Rule?.Rate,
                InvoiceDependent: x.Tax == WithholdingTaxType.Iss || x.Rule?.RateSource == RateSource.FromNota))
            .ToList();

        return TypedResults.Ok(items);
    }
}
