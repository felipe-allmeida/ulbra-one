using Ulbra.One.Registry.Domain.ValueObjects;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Cliente — pode ser PF ou PJ. Documento normalizado (só dígitos).</summary>
public class CustomerEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public PersonType PersonType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string Document { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public decimal? CreditLimit { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private CustomerEntity() { }

    public CustomerEntity(Guid companyId, PersonType personType, string name, string? tradeName,
        string document, string? email, string? phone, Address address, decimal? creditLimit)
    {
        CompanyId = companyId;
        Apply(personType, name, tradeName, document, email, phone, address, creditLimit);
    }

    public void Update(PersonType personType, string name, string? tradeName, string document,
        string? email, string? phone, Address address, decimal? creditLimit) =>
        Apply(personType, name, tradeName, document, email, phone, address, creditLimit);

    private void Apply(PersonType personType, string name, string? tradeName, string document,
        string? email, string? phone, Address address, decimal? creditLimit)
    {
        PersonType = personType;
        Name = name;
        TradeName = tradeName;
        Document = DocumentValidator.Normalize(document);
        Email = email;
        Phone = phone;
        Address = address;
        CreditLimit = creditLimit;
    }

    public void Deactivate() => Active = false;
    public void Activate() => Active = true;

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
