namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Associação N:N entre <see cref="CentroDeCusto"/> e <see cref="Unidade"/> —
/// em quais unidades cada centro de custo atua.
/// </summary>
public class CentroDeCustoUnidade
{
    public Guid CentroDeCustoId { get; private set; }
    public Guid UnidadeId { get; private set; }

    private CentroDeCustoUnidade() { }

    public CentroDeCustoUnidade(Guid centroDeCustoId, Guid unidadeId)
    {
        CentroDeCustoId = centroDeCustoId;
        UnidadeId = unidadeId;
    }
}
