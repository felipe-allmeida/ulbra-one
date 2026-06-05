using Ulbra.One.Registry.Domain.ValueObjects;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Employee — always an individual (CPF). Linked to a Unit and, optionally, a CostCenter (logical references).</summary>
public class EmployeeEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Document { get; private set; } = string.Empty; // CPF
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string Role { get; private set; } = string.Empty; // job title
    public Guid UnitId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public DateOnly HireDate { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private EmployeeEntity() { }

    public EmployeeEntity(Guid companyId, string name, string document, string? email, string? phone,
        string role, Guid unitId, Guid? costCenterId, DateOnly hireDate, Address address)
    {
        CompanyId = companyId;
        UnitId = unitId;
        Apply(name, document, email, phone, role, costCenterId, hireDate, address);
    }

    public void Update(string name, string document, string? email, string? phone, string role,
        Guid unitId, Guid? costCenterId, DateOnly hireDate, Address address)
    {
        UnitId = unitId;
        Apply(name, document, email, phone, role, costCenterId, hireDate, address);
    }

    private void Apply(string name, string document, string? email, string? phone, string role,
        Guid? costCenterId, DateOnly hireDate, Address address)
    {
        Name = name;
        Document = DocumentValidator.Normalize(document);
        Email = email;
        Phone = phone;
        Role = role;
        CostCenterId = costCenterId;
        HireDate = hireDate;
        Address = address;
    }

    public void Deactivate() => Active = false;
    public void Activate() => Active = true;

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
