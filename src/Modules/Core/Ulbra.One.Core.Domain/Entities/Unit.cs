namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Unidade organizacional vinculada a uma <see cref="Company"/> (campus, polo, diretoria).</summary>
public class Unit
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Acronym { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Unit() { }

    public Unit(Guid companyId, string name, string acronym)
    {
        CompanyId = companyId;
        Name = name;
        Acronym = acronym;
    }

    public void Update(string name, string acronym)
    {
        Name = name;
        Acronym = acronym;
    }
}
