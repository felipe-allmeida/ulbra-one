using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Contracts;

/// <summary>Response shape for a <see cref="Company"/>. Matches the frontend <c>Company</c> type.</summary>
public sealed record CompanyResponse(Guid Id, string LegalName, string Cnpj, bool Active)
{
    public static CompanyResponse From(Company c) => new(c.Id, c.LegalName, c.Cnpj, c.Active);
}

/// <summary>Response shape for a <see cref="Unit"/>. Matches the frontend <c>Unit</c> type.</summary>
public sealed record UnitResponse(Guid Id, Guid CompanyId, string Name, string Acronym, bool Active)
{
    public static UnitResponse From(Unit u) => new(u.Id, u.CompanyId, u.Name, u.Acronym, u.Active);
}

/// <summary>Response shape for a <see cref="CostCenter"/> with its linked unit ids. Matches the frontend <c>CostCenter</c> type.</summary>
public sealed record CostCenterResponse(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    decimal AnnualBudget,
    decimal AutoApprovalLimit,
    bool Active,
    IReadOnlyList<Guid> UnitIds)
{
    public static CostCenterResponse From(CostCenter c, IReadOnlyList<Guid> unitIds) =>
        new(c.Id, c.CompanyId, c.Code, c.Name, c.AnnualBudget, c.AutoApprovalLimit, c.Active, unitIds);
}
