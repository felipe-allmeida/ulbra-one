namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Centro de custo de uma <see cref="Unidade"/>. Suporta hierarquia (CC pai) para rollup orçamentário.
/// </summary>
public class CentroDeCusto
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UnidadeId { get; private set; }
    public Guid? CentroDeCustoPaiId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public decimal OrcamentoAnual { get; private set; }
    public bool Ativo { get; private set; } = true;
    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;

    private CentroDeCusto() { }

    public CentroDeCusto(Guid unidadeId, string codigo, string nome, decimal orcamentoAnual, Guid? centroDeCustoPaiId = null)
    {
        UnidadeId = unidadeId;
        Codigo = codigo;
        Nome = nome;
        OrcamentoAnual = orcamentoAnual;
        CentroDeCustoPaiId = centroDeCustoPaiId;
    }
}
