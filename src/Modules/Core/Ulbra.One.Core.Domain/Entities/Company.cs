namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Pessoa jurídica do grupo (mantenedora, IES, unidade). Raiz do organograma do ERP.</summary>
public class Company
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string LegalName { get; private set; } = string.Empty;
    public string Cnpj { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Company() { }

    public Company(string legalName, string cnpj)
    {
        LegalName = legalName;
        Cnpj = cnpj;
    }

    public void Update(string legalName, string cnpj)
    {
        LegalName = legalName;
        Cnpj = cnpj;
    }
}
