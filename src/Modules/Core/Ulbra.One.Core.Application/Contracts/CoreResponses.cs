using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Contracts;

/// <summary>Response shape for a <see cref="Company"/>. Matches the frontend <c>Company</c> type.</summary>
public sealed record CompanyResponse(CompanyId Id, string LegalName, string Cnpj, bool Active)
{
    public static CompanyResponse From(Company c) => new(new CompanyId(c.Id), c.LegalName, c.Cnpj, c.Active);
}

/// <summary>Response shape for a <see cref="Unit"/>. Matches the frontend <c>Unit</c> type.</summary>
public sealed record UnitResponse(UnitId Id, CompanyId CompanyId, string Name, string Acronym, bool Active)
{
    public static UnitResponse From(Unit u) => new(new UnitId(u.Id), new CompanyId(u.CompanyId), u.Name, u.Acronym, u.Active);
}

/// <summary>Response shape for a <see cref="CostCenter"/> with its linked unit ids. Matches the frontend <c>CostCenter</c> type.</summary>
public sealed record CostCenterResponse(
    CostCenterId Id,
    CompanyId CompanyId,
    string Code,
    string Name,
    decimal AnnualBudget,
    decimal AutoApprovalLimit,
    bool Active,
    IReadOnlyList<UnitId> UnitIds)
{
    public static CostCenterResponse From(CostCenter c, IReadOnlyList<Guid> unitIds) =>
        new(new CostCenterId(c.Id), new CompanyId(c.CompanyId), c.Code, c.Name, c.AnnualBudget, c.AutoApprovalLimit, c.Active,
            unitIds.Select(g => new UnitId(g)).ToArray());
}
