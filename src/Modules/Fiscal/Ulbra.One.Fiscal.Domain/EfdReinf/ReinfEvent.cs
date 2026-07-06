namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Evento periódico da EFD-Reinf para uma retenção. Nesta fase só <c>R4020</c> é gerado (série 4000 —
/// IR/CSLL/PIS/COFINS sobre PJ). <c>R2010</c> (INSS) e o beneficiário PF (<c>R-4010</c>) são follow-ups.</summary>
public enum ReinfEvent
{
    R2010,
    R4020,
    Unmapped,
}

/// <summary>Mapeia a guia de recolhimento no evento da EFD-Reinf. Vive SÓ no adaptador — o domínio/painel usam
/// <see cref="RetentionGroup"/>. PJ-only (sem PersonType): INSS→R-2010 (deferido), IRRF/CSRF→R-4020, resto fora.</summary>
public static class ReinfEventMapper
{
    public static ReinfEvent Map(RetentionGroup group) => group switch
    {
        RetentionGroup.Inss => ReinfEvent.R2010,
        RetentionGroup.Irrf or RetentionGroup.Csrf => ReinfEvent.R4020,
        _ => ReinfEvent.Unmapped, // Iss (municipal) / Other: nunca no arquivo
    };
}
