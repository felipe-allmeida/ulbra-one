using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Contracts;

public sealed record WithholdingRuleResponse(
    WithholdingRuleId Id, CompanyId? CompanyId, WithholdingTaxType TaxType, string? Lc116Subitem,
    TaxRegime? VendorRegime, string? MunicipalityIbge, bool Retains, NonRetentionReason Reason,
    RateSource RateSource, decimal? Rate, BaseRule BaseRule, decimal? MinimumAmount,
    bool? Cprb, SimplesAnexo? SimplesAnexo, bool Active)
{
    public static WithholdingRuleResponse From(WithholdingRule r) => new(
        new WithholdingRuleId(r.Id), r.CompanyId is { } c ? new CompanyId(c) : null, r.TaxType, r.Lc116Subitem,
        r.VendorRegime, r.MunicipalityIbge, r.Retains, r.Reason, r.RateSource, r.Rate, r.BaseRule, r.MinimumAmount,
        r.Cprb, r.SimplesAnexo, r.Active);
}

public sealed record WithholdingRuleRequest
{
    public Guid? CompanyId { get; set; }
    public WithholdingTaxType TaxType { get; set; }
    public string? Lc116Subitem { get; set; }
    public TaxRegime? VendorRegime { get; set; }
    public string? MunicipalityIbge { get; set; }
    public bool Retains { get; set; }
    public NonRetentionReason Reason { get; set; }
    public RateSource RateSource { get; set; }
    public decimal? Rate { get; set; }
    public BaseRule BaseRule { get; set; }
    public decimal? MinimumAmount { get; set; }
    public bool? Cprb { get; set; }
    public SimplesAnexo? SimplesAnexo { get; set; }
}

/// <summary>One tax's system-default outcome for a given vendor tax profile — used to preview what the system
/// would apply before/without a vendor-specific override. <see cref="InvoiceDependent"/> flags ISS (rate depends
/// on the invoice/municipality), which cannot be resolved to a number at cadastro time.</summary>
public sealed record WithholdingPreviewItem(
    WithholdingTaxType TaxType, bool Retains, NonRetentionReason Reason,
    RateSource RateSource, decimal? Rate, bool InvoiceDependent);
