using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Fiscal.Domain;

/// <summary>Why a rule/line does not produce a withholding — audit/explanation metadata.</summary>
public enum NonRetentionReason { None, RegimeExemption, NoMunicipalSubstitution, BelowMinimum }

/// <summary>Rate origin: fixed on the rule, or taken from the invoice itself (Simples ISS = provider's band rate).</summary>
public enum RateSource { Fixed, FromNota }

/// <summary>Withholding calculation base. MVP applies on the full line amount; Reduced is [to be confirmed].</summary>
public enum BaseRule { Full, Reduced }

/// <summary>Collection group (DARF/guia code): the unit of minimum/exemption and of future tax assessment.</summary>
public enum RetentionGroup { Irrf, Csrf, Inss, Iss, Other }

/// <summary>Maps the atomic tax to its collection group. PIS+COFINS+CSLL = CSRF (DARF 5952).</summary>
public static class RetentionGroups
{
    public static RetentionGroup For(WithholdingTaxType taxType) => taxType switch
    {
        WithholdingTaxType.Irrf => RetentionGroup.Irrf,
        WithholdingTaxType.Pis or WithholdingTaxType.Cofins or WithholdingTaxType.Csll => RetentionGroup.Csrf,
        WithholdingTaxType.Inss => RetentionGroup.Inss,
        WithholdingTaxType.Iss => RetentionGroup.Iss,
        _ => RetentionGroup.Other,
    };
}
