using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Domain.Entities;

/// <summary>
/// Regra de retenção editável que o resolver aplica POR TRIBUTO. A chave são dimensões opcionais
/// (null = "qualquer"): <see cref="Lc116Subitem"/>, <see cref="VendorRegime"/>, <see cref="MunicipalityIbge"/>,
/// <see cref="CompanyId"/>. O desfecho é <see cref="Retains"/> + alíquota/base/piso. A especificidade
/// (qual regra vence quando várias casam) é CALCULADA na resolução, não armazenada.
/// </summary>
public class WithholdingRule : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? CompanyId { get; private set; }
    public WithholdingTaxType TaxType { get; private set; }
    public string? Lc116Subitem { get; private set; }
    public TaxRegime? VendorRegime { get; private set; }
    public string? MunicipalityIbge { get; private set; }

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

    /// <summary>Grupo de recolhimento (DARF/guia) derivado do tributo.</summary>
    public RetentionGroup Group => RetentionGroups.For(TaxType);

    /// <summary>Quantas dimensões a regra fixa (não-nulas) — base da especificidade na resolução.</summary>
    public int DimensionCount =>
        (Lc116Subitem is not null ? 1 : 0) + (VendorRegime is not null ? 1 : 0)
        + (MunicipalityIbge is not null ? 1 : 0) + (CompanyId is not null ? 1 : 0);

    private WithholdingRule() { }

    public WithholdingRule(WithholdingTaxType taxType, bool retains, RateSource rateSource, decimal? rate,
        string? lc116Subitem = null, TaxRegime? vendorRegime = null, string? municipalityIbge = null,
        Guid? companyId = null, BaseRule baseRule = BaseRule.Cheia, decimal? minimumAmount = null,
        NonRetentionReason reason = NonRetentionReason.None)
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
    }

    public void Activate() => Active = true;
    public void Deactivate() => Active = false;

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
