namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>EFD-Reinf periodic event for a withholding. In this phase only <c>R4020</c> is emitted (série 4000 —
/// IR/CSLL/PIS/COFINS on PJ). <c>R2010</c> (INSS) and the PF beneficiary (<c>R-4010</c>) are follow-ups.</summary>
public enum ReinfEvent
{
    R2010,
    R4020,
    Unmapped,
}

/// <summary>Maps the guia de recolhimento to its EFD-Reinf event. Lives ONLY in the adapter — domain/panel use
/// <see cref="RetentionGroup"/>. PJ-only (no PersonType): INSS→R-2010 (deferred), IRRF/CSRF→R-4020, rest excluded.</summary>
public static class ReinfEventMapper
{
    public static ReinfEvent Map(RetentionGroup group) => group switch
    {
        RetentionGroup.Inss => ReinfEvent.R2010,
        RetentionGroup.Irrf or RetentionGroup.Csrf => ReinfEvent.R4020,
        _ => ReinfEvent.Unmapped, // Iss (municipal) / Other: never in the file
    };
}
