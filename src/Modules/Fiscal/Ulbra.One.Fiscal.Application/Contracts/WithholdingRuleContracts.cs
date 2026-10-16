using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Contracts;

/// <summary>A withholding rule as stored — the decision table behind every retention a payable calculates.</summary>
[SwaggerSchema("One row of the withholding decision table. A payable is matched against these to decide "
    + "whether a tax is withheld and at what rate; the null fields are wildcards, so a rule with fewer "
    + "constraints matches more payables.")]
public sealed record WithholdingRuleResponse(
    WithholdingRuleId Id,
    [property: SwaggerSchema("Company the rule applies to. Null makes it a system-wide rule, and a "
        + "company-specific rule wins over the system default for the same profile.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Which tax this rule decides.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("LC 116 service subitem the rule matches, stored canonically. Null is a "
        + "wildcard — it matches any subitem.")]
    [property: SwaggerExample("7.02")]
    string? Lc116Subitem,
    [property: SwaggerSchema("Vendor tax regime the rule matches. Null matches any regime.")]
    TaxRegime? VendorRegime,
    [property: SwaggerSchema("IBGE code of the municipality the rule matches — relevant for ISS, whose "
        + "rate is set locally. Null matches any municipality.")]
    [property: SwaggerExample("4304606")]
    string? MunicipalityIbge,
    [property: SwaggerSchema("Whether a match withholds the tax. False makes this an explicit "
        + "NON-retention rule, which is not the same as having no rule: it records that the exemption was "
        + "decided, and Reason says why.")]
    bool Retains,
    [property: SwaggerSchema("Why the tax is not withheld. Only meaningful when Retains is false.")]
    NonRetentionReason Reason,
    [property: SwaggerSchema("Where the rate comes from: a fixed value stored here, or resolved from the "
        + "invoice. ISS is invoice-dependent, so its rate cannot be known at registration time.")]
    RateSource RateSource,
    [property: SwaggerSchema("Withholding rate as a percentage. Present when the rule retains at a fixed "
        + "rate; null when the rate comes from the invoice or nothing is withheld.")]
    [property: SwaggerExample("4.65")]
    decimal? Rate,
    [property: SwaggerSchema("Which amount the rate applies to — gross, net, or the service portion.")]
    BaseRule BaseRule,
    [property: SwaggerSchema("Floor below which nothing is withheld, in BRL. Null means no floor.")]
    [property: SwaggerExample("10.00")]
    decimal? MinimumAmount,
    [property: SwaggerSchema("Match only vendors with this CPRB status. Null matches regardless — which "
        + "is different from matching vendors whose status was never determined.")]
    bool? Cprb,
    [property: SwaggerSchema("Match only vendors on this Simples Nacional annex. Null matches regardless.")]
    SimplesAnexo? SimplesAnexo,
    [property: SwaggerSchema("Whether the rule is in force. An inactive rule is kept for audit but takes "
        + "no part in matching, so deactivating one changes what new payables withhold.")]
    bool Active)
{
    public static WithholdingRuleResponse From(WithholdingRule r) => new(
        new WithholdingRuleId(r.Id), r.CompanyId is { } c ? new CompanyId(c) : null, r.TaxType, r.Lc116Subitem,
        r.VendorRegime, r.MunicipalityIbge, r.Retains, r.Reason, r.RateSource, r.Rate, r.BaseRule, r.MinimumAmount,
        r.Cprb, r.SimplesAnexo, r.Active);
}

public sealed record WithholdingRuleRequest
{
    [SwaggerSchema("Company the rule applies to. Null makes it a system-wide rule — a company-specific "
        + "rule wins over the system default for the same tax profile.")]
    public Guid? CompanyId { get; set; }

    [SwaggerSchema("Which tax this rule decides.")]
    public WithholdingTaxType TaxType { get; set; }

    [SwaggerSchema("LC 116 service subitem the rule matches, stored canonically. Null matches any subitem.")]
    [SwaggerExample("7.02")]
    public string? Lc116Subitem { get; set; }

    [SwaggerSchema("Vendor tax regime the rule matches. Null matches any regime.")]
    public TaxRegime? VendorRegime { get; set; }

    [SwaggerSchema("IBGE code of the municipality the rule matches — relevant for ISS, where the rate is "
        + "set locally. Null matches any municipality.")]
    [SwaggerExample("4304606")]
    public string? MunicipalityIbge { get; set; }

    [SwaggerSchema("Whether a match withholds the tax. False makes this an explicit non-retention rule, "
        + "and Reason then records why.")]
    public bool Retains { get; set; }

    [SwaggerSchema("Why the tax is NOT withheld. Only meaningful when Retains is false.")]
    public NonRetentionReason Reason { get; set; }

    [SwaggerSchema("Where the rate comes from: a fixed value set here, or resolved from the invoice.")]
    public RateSource RateSource { get; set; }

    [SwaggerSchema("Withholding rate as a percentage. Required and positive when the rule retains with a "
        + "fixed rate — otherwise the call is rejected with 400.")]
    [SwaggerExample("4.65")]
    public decimal? Rate { get; set; }

    [SwaggerSchema("Which amount the rate is applied to.")]
    public BaseRule BaseRule { get; set; }

    [SwaggerSchema("Floor below which nothing is withheld, in BRL. Null means no floor.")]
    [SwaggerExample("10.00")]
    public decimal? MinimumAmount { get; set; }

    [SwaggerSchema("Match only vendors whose CPRB status is this. Null matches regardless.")]
    public bool? Cprb { get; set; }

    [SwaggerSchema("Match only vendors on this Simples Nacional annex. Null matches regardless.")]
    public SimplesAnexo? SimplesAnexo { get; set; }
}

/// <summary>One tax's system-default outcome for a given vendor tax profile — used to preview what the system
/// would apply before/without a vendor-specific override. <see cref="InvoiceDependent"/> flags ISS (rate depends
/// on the invoice/municipality), which cannot be resolved to a number at registration time.</summary>
[SwaggerSchema("What the system rules would do for one tax, given a vendor's tax profile. A preview: it "
    + "shows the default BEFORE any vendor-specific rate override is applied, so the registration screen "
    + "can show what changes if the override is removed.")]
public sealed record WithholdingPreviewItem(
    [property: SwaggerSchema("The tax this row previews.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Whether the system rules would withhold it for this profile.")]
    bool Retains,
    [property: SwaggerSchema("Why it would not be withheld. Only meaningful when Retains is false.")]
    NonRetentionReason Reason,
    [property: SwaggerSchema("Where the rate would come from.")]
    RateSource RateSource,
    [property: SwaggerSchema("The rate that would apply, as a percentage. Null when nothing is withheld "
        + "or when the rate depends on the invoice — see InvoiceDependent.")]
    [property: SwaggerExample("4.65")]
    decimal? Rate,
    [property: SwaggerSchema("Whether the rate can only be known from the invoice. True for ISS, where "
        + "the municipality sets it — so a null Rate here means undeterminable, not zero.")]
    bool InvoiceDependent);
