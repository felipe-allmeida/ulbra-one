namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Anexo do Simples Nacional (LC 123). Relevante para a retenção de INSS: o <b>Anexo IV</b> (construção
/// civil e afins) recolhe a CPP fora do DAS, então sofre a retenção de 11%; os demais anexos são <b>isentos</b> da
/// retenção. Só faz sentido quando <see cref="TaxRegime.SimplesNacional"/>.</summary>
public enum SimplesAnexo
{
    I = 1,
    II = 2,
    III = 3,
    IV = 4,
    V = 5,
}
