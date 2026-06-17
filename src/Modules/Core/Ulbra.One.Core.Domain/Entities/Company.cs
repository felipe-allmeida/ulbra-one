using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Legal entity in the group (holding/IES/unit). Root of the ERP org chart.</summary>
public class Company : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string LegalName { get; private set; } = string.Empty;
    public string Cnpj { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

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

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
