using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Fiscal.Domain;

/// <summary>Por que uma regra/linha não gera retenção — metadado de auditoria/explicação.</summary>
public enum NonRetentionReason { None, RegimeDispensa, NaoSubstituicaoMunicipal, AbaixoDoPiso }

/// <summary>Origem da alíquota: fixa na regra, ou tirada da própria nota (ISS de Simples = faixa do prestador).</summary>
public enum RateSource { Fixed, FromNota }

/// <summary>Base de cálculo da retenção. MVP aplica sobre o valor cheio da linha; Reduzida fica [a confirmar].</summary>
public enum BaseRule { Cheia, Reduzida }

/// <summary>Grupo de recolhimento (código DARF/guia): unidade de piso/dispensa e da apuração futura.</summary>
public enum RetentionGroup { Irrf, Csrf, Inss, Iss, Outros }

/// <summary>Mapeia o tributo atômico ao seu grupo de recolhimento. PIS+COFINS+CSLL = CSRF (DARF 5952).</summary>
public static class RetentionGroups
{
    public static RetentionGroup For(WithholdingTaxType taxType) => taxType switch
    {
        WithholdingTaxType.Irrf => RetentionGroup.Irrf,
        WithholdingTaxType.Pis or WithholdingTaxType.Cofins or WithholdingTaxType.Csll => RetentionGroup.Csrf,
        WithholdingTaxType.Inss => RetentionGroup.Inss,
        WithholdingTaxType.Iss => RetentionGroup.Iss,
        _ => RetentionGroup.Outros,
    };
}
