namespace Ulbra.One.Financeiro.Domain.Entities;

/// <summary>
/// Lançamento financeiro (conta a pagar/receber), classificado por Empresa × Unidade × Centro de Custo.
/// </summary>
public class Lancamento
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public Guid UnidadeId { get; private set; }
    public Guid CentroDeCustoId { get; private set; }

    public TipoLancamento Tipo { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public string Contraparte { get; private set; } = string.Empty;
    public decimal Valor { get; private set; }
    public DateOnly Vencimento { get; private set; }

    public StatusLancamento Status { get; private set; } = StatusLancamento.Pendente;
    public string? MotivoRejeicao { get; private set; }

    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;
    public DateTime? AprovadoEm { get; private set; }
    public DateTime? PagoEm { get; private set; }

    private Lancamento() { }

    public Lancamento(Guid empresaId, Guid unidadeId, Guid centroDeCustoId, TipoLancamento tipo,
        string descricao, string contraparte, decimal valor, DateOnly vencimento)
    {
        EmpresaId = empresaId;
        UnidadeId = unidadeId;
        CentroDeCustoId = centroDeCustoId;
        Tipo = tipo;
        Descricao = descricao;
        Contraparte = contraparte;
        Valor = valor;
        Vencimento = vencimento;
    }

    /// <summary>Aprova automaticamente despesas (pagar) dentro do limite do centro de custo.</summary>
    public void AplicarAutoAprovacao(decimal limiteAutoAprovacao)
    {
        if (Tipo == TipoLancamento.Pagar && limiteAutoAprovacao > 0 && Valor <= limiteAutoAprovacao)
            Aprovar();
    }

    public void Aprovar()
    {
        if (Status is StatusLancamento.Pago) return;
        Status = StatusLancamento.Aprovado;
        AprovadoEm = DateTime.UtcNow;
        MotivoRejeicao = null;
    }

    public void Rejeitar(string? motivo)
    {
        Status = StatusLancamento.Rejeitado;
        MotivoRejeicao = motivo;
    }

    public void MarcarPago()
    {
        if (Status != StatusLancamento.Aprovado)
            throw new InvalidOperationException("Só é possível pagar um lançamento aprovado.");
        Status = StatusLancamento.Pago;
        PagoEm = DateTime.UtcNow;
    }
}
