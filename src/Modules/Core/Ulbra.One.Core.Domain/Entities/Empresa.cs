namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Pessoa jurídica do grupo (mantenedora, IES, unidade). Raiz do organograma do ERP.
/// </summary>
public class Empresa
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string RazaoSocial { get; private set; } = string.Empty;
    public string Cnpj { get; private set; } = string.Empty;
    public bool Ativa { get; private set; } = true;
    public DateTime CriadaEm { get; private set; } = DateTime.UtcNow;

    private Empresa() { }

    public Empresa(string razaoSocial, string cnpj)
    {
        RazaoSocial = razaoSocial;
        Cnpj = cnpj;
    }

    public void Atualizar(string razaoSocial, string cnpj)
    {
        RazaoSocial = razaoSocial;
        Cnpj = cnpj;
    }
}
