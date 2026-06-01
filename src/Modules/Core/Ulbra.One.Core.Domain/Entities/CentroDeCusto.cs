namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Centro de custo — dimensão funcional (Marketing, TI, RH…) que pertence a uma
/// <see cref="Empresa"/> e é transversal: atua em N unidades (ver <see cref="CentroDeCustoUnidade"/>).
/// </summary>
public class CentroDeCusto
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public decimal OrcamentoAnual { get; private set; }
    public bool Ativo { get; private set; } = true;
    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;

    private CentroDeCusto() { }

    public CentroDeCusto(Guid empresaId, string codigo, string nome, decimal orcamentoAnual)
    {
        EmpresaId = empresaId;
        Codigo = codigo;
        Nome = nome;
        OrcamentoAnual = orcamentoAnual;
    }
}
