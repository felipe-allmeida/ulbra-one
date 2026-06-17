using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Unidade organizacional vinculada a uma <see cref="Company"/> (campus, polo, diretoria).</summary>
public class Unit : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Acronym { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

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

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
