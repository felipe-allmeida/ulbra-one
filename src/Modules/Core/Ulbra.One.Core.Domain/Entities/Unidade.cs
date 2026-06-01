namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Unidade organizacional vinculada a uma <see cref="Empresa"/> (campus, polo, diretoria).
/// </summary>
public class Unidade
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Sigla { get; private set; } = string.Empty;
    public bool Ativa { get; private set; } = true;
    public DateTime CriadaEm { get; private set; } = DateTime.UtcNow;

    private Unidade() { }

    public Unidade(Guid empresaId, string nome, string sigla)
    {
        EmpresaId = empresaId;
        Nome = nome;
        Sigla = sigla;
    }
}
