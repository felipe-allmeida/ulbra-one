using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Domain.Entities;

/// <summary>
/// Editable withholding rule applied by the resolver PER TAX. The key is a set of optional dimensions
/// (null = "any"): <see cref="Lc116Subitem"/>, <see cref="VendorRegime"/>, <see cref="MunicipalityIbge"/>,
/// <see cref="CompanyId"/>, and the INSS-specific <see cref="Cprb"/> / <see cref="SimplesAnexo"/>. The outcome
/// is <see cref="Retains"/> + rate/base/minimum. Specificity (which rule wins when several match) is COMPUTED
/// during resolution, not stored. Per-vendor tuning is a rate override on the vendor cadastro (not a rule here):
/// the system rules decide WHETHER to retain; the vendor may only override the rate.
/// </summary>
public class WithholdingRule : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? CompanyId { get; private set; }
    public WithholdingTaxType TaxType { get; private set; }
    public string? Lc116Subitem { get; private set; }
    public TaxRegime? VendorRegime { get; private set; }
    public string? MunicipalityIbge { get; private set; }
    // INSS-specific dimensions (null = "any"): CPRB (folha desonerada) → 3,5%; Simples Anexo IV → 11% vs demais isento.
    public bool? Cprb { get; private set; }
    public SimplesAnexo? SimplesAnexo { get; private set; }

    public bool Retains { get; private set; }
    public NonRetentionReason Reason { get; private set; }
    public RateSource RateSource { get; private set; }
    public decimal? Rate { get; private set; }
    public BaseRule BaseRule { get; private set; }
    public decimal? MinimumAmount { get; private set; }
    public bool Active { get; private set; } = true;

    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>Collection group (DARF/guia) derived from the tax.</summary>
    public RetentionGroup Group => RetentionGroups.For(TaxType);

    /// <summary>How many dimensions the rule pins (non-null) — the basis of specificity during resolution.</summary>
    public int DimensionCount =>
        (Lc116Subitem is not null ? 1 : 0) + (VendorRegime is not null ? 1 : 0)
        + (MunicipalityIbge is not null ? 1 : 0) + (CompanyId is not null ? 1 : 0)
        + (Cprb is not null ? 1 : 0) + (SimplesAnexo is not null ? 1 : 0);

    private WithholdingRule() { }

    public WithholdingRule(WithholdingTaxType taxType, bool retains, RateSource rateSource, decimal? rate,
        string? lc116Subitem = null, TaxRegime? vendorRegime = null, string? municipalityIbge = null,
        Guid? companyId = null, BaseRule baseRule = BaseRule.Full, decimal? minimumAmount = null,
        NonRetentionReason reason = NonRetentionReason.None, bool? cprb = null, SimplesAnexo? simplesAnexo = null)
    {
        TaxType = taxType;
        Retains = retains;
        RateSource = rateSource;
        Rate = rate;
        Lc116Subitem = lc116Subitem;
        VendorRegime = vendorRegime;
        MunicipalityIbge = municipalityIbge;
        CompanyId = companyId;
        BaseRule = baseRule;
        MinimumAmount = minimumAmount;
        Reason = reason;
        Cprb = cprb;
        SimplesAnexo = simplesAnexo;
    }

    /// <summary>Replaces all rule fields (CRUD update).</summary>
    public void Update(WithholdingTaxType taxType, bool retains, RateSource rateSource, decimal? rate,
        string? lc116Subitem, TaxRegime? vendorRegime, string? municipalityIbge, Guid? companyId,
        BaseRule baseRule, decimal? minimumAmount, NonRetentionReason reason,
        bool? cprb, SimplesAnexo? simplesAnexo)
    {
        TaxType = taxType;
        Retains = retains;
        RateSource = rateSource;
        Rate = rate;
        Lc116Subitem = lc116Subitem;
        VendorRegime = vendorRegime;
        MunicipalityIbge = municipalityIbge;
        CompanyId = companyId;
        BaseRule = baseRule;
        MinimumAmount = minimumAmount;
        Reason = reason;
        Cprb = cprb;
        SimplesAnexo = simplesAnexo;
    }

    public void Activate() => Active = true;
    public void Deactivate() => Active = false;

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
