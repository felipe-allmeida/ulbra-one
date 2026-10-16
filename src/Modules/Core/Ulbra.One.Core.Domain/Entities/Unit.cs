using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Unidade organizacional vinculada a uma <see cref="Company"/> (campus, polo, diretoria).</summary>
public class Unit : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Acronym { get; private set; } = string.Empty;

    /// <summary>Legacy ERP company code (stable natural key from the legacy system). Optional —
    /// carried so budget imports and cross-system reconciliation can match by code.</summary>
    public string? Code { get; private set; }

    /// <summary>Establishment CNPJ (14 digits, no mask). Optional — used for accounting tracking
    /// (segments movements per establishment), never for payment (the paying company owns that).</summary>
    public string? Cnpj { get; private set; }

    /// <summary>Marks this unit as the group's headquarters (matriz) vs a branch (filial).</summary>
    public bool IsMatriz { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private Unit() { }

    public Unit(Guid companyId, string name, string acronym, string? cnpj = null, bool isMatriz = false, string? code = null)
    {
        CompanyId = companyId;
        Name = name;
        Acronym = acronym;
        Cnpj = string.IsNullOrWhiteSpace(cnpj) ? null : cnpj;
        IsMatriz = isMatriz;
        Code = string.IsNullOrWhiteSpace(code) ? null : code;
    }

    public void Update(string name, string acronym, string? cnpj = null, bool isMatriz = false, string? code = null)
    {
        Name = name;
        Acronym = acronym;
        Cnpj = string.IsNullOrWhiteSpace(cnpj) ? null : cnpj;
        IsMatriz = isMatriz;
        Code = string.IsNullOrWhiteSpace(code) ? null : code;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
