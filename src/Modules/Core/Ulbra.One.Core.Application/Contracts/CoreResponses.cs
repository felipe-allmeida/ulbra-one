using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Contracts;

/// <summary>Response shape for a <see cref="Company"/>. Matches the frontend <c>Company</c> type.</summary>
[SwaggerSchema("A company (mantenedora / unidade jurídica) — the root of the org chart. Everything else in "
    + "the system hangs off one.")]
public sealed record CompanyResponse(
    CompanyId Id,
    [property: SwaggerSchema("Registered legal name (razão social).")]
    [property: SwaggerExample("Universidade Luterana do Brasil")]
    string LegalName,
    [property: SwaggerSchema("Company CNPJ, digits only.")]
    [property: SwaggerExample("88332580000165")]
    string Cnpj,
    [property: SwaggerSchema("Whether the company is in use. An inactive company is kept for history but "
        + "drops out of the selector and of every list that offers a company to pick.")]
    bool Active)
{
    public static CompanyResponse From(Company c) => new(new CompanyId(c.Id), c.LegalName, c.Cnpj, c.Active);
}

/// <summary>Response shape for a <see cref="Unit"/>. Matches the frontend <c>Unit</c> type.</summary>
[SwaggerSchema("A unit (campus / establishment) of a company. Spend is charged to a unit, and a scoped "
    + "requester only sees the units granted to them.")]
public sealed record UnitResponse(
    UnitId Id,
    [property: SwaggerSchema("Company the unit belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Unit name as it appears throughout the UI.")]
    [property: SwaggerExample("Campus Canoas")]
    string Name,
    [property: SwaggerSchema("Short form used where the full name does not fit — tables, chips, reports.")]
    [property: SwaggerExample("CANOAS")]
    string Acronym,
    [property: SwaggerSchema("Code this unit had in the legacy ERP. Optional, and only meaningful for "
        + "reconciling against legacy data.")]
    [property: SwaggerExample("0142")]
    string? Code,
    [property: SwaggerSchema("Establishment CNPJ, digits only. Optional — it exists for accounting tracking "
        + "and does not have to be filled for the unit to be usable.")]
    [property: SwaggerExample("88332580000246")]
    string? Cnpj,
    [property: SwaggerSchema("Whether this is the matriz (head establishment) rather than a filial.")]
    bool IsMatriz,
    [property: SwaggerSchema("Whether the unit is in use. An inactive unit is kept for history but cannot "
        + "be chosen on new documents.")]
    bool Active)
{
    public static UnitResponse From(Unit u) => new(new UnitId(u.Id), new CompanyId(u.CompanyId), u.Name, u.Acronym, u.Code, u.Cnpj, u.IsMatriz, u.Active);
}

/// <summary>Response shape for a <see cref="CostCenter"/> with its linked unit ids. Matches the frontend <c>CostCenter</c> type.</summary>
[SwaggerSchema("A cost center (natureza) with the units it serves. It is what a requisition consumes "
    + "budget from, and the budget itself lives in the monthly cells, not here.")]
public sealed record CostCenterResponse(
    CostCenterId Id,
    [property: SwaggerSchema("Company the cost center belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Accounting code of the natureza.")]
    [property: SwaggerExample("3.1.2.04")]
    string Code,
    [property: SwaggerSchema("Cost center name as it appears throughout the UI.")]
    [property: SwaggerExample("Manutenção predial")]
    string Name,
    [property: SwaggerSchema("Sum of the cost center's active monthly budget cells, in BRL. Derived on "
        + "read — there is no stored annual budget, so it changes whenever a cell does.")]
    [property: SwaggerExample("480000.00")]
    decimal AnnualBudget,
    [property: SwaggerSchema("Ceiling, in BRL, under which a payable charged to this cost center is "
        + "approved without human review. Zero disables it, sending everything to an approver.")]
    [property: SwaggerExample("5000.00")]
    decimal AutoApprovalLimit,
    [property: SwaggerSchema("Whether the cost center is in use. An inactive one is kept for history but "
        + "is rejected on new requisitions.")]
    bool Active,
    [property: SwaggerSchema("Units this cost center serves. A requisition is only valid on a unit that "
        + "appears here.")]
    IReadOnlyList<UnitId> UnitIds)
{
    public static CostCenterResponse From(CostCenter c, IReadOnlyList<Guid> unitIds, decimal annualBudget) =>
        new(new CostCenterId(c.Id), new CompanyId(c.CompanyId), c.Code, c.Name, annualBudget, c.AutoApprovalLimit, c.Active,
            unitIds.Select(g => new UnitId(g)).ToArray());
}

/// <summary>Response shape for a single monthly budget cell owned by a cost center (<see cref="CostCenterUnitBudget"/>).</summary>
[SwaggerSchema("One month of budget for a Unit x Cost Center pair. This is where the money actually is — "
    + "the cell is what a requisition consumes and what an adequação moves.")]
public sealed record CostCenterUnitBudgetResponse(
    CostCenterUnitBudgetId Id,
    [property: SwaggerSchema("Cost center that owns the cell.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Unit the budget is earmarked for.")]
    UnitId UnitId,
    [property: SwaggerSchema("Company both belong to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Month the cell covers (`YYYY-MM`).")]
    [property: SwaggerExample("2026-08")]
    string Competency,
    [property: SwaggerSchema("Ceiling for this month, in BRL. Approved requisitions consume it; an "
        + "adequação raises, lowers or transfers it.")]
    [property: SwaggerExample("40000.00")]
    decimal MonthlyLimit,
    [property: SwaggerSchema("Whether the cell counts. An inactive cell is ignored by the budget check and "
        + "by the annual roll-up.")]
    bool Active)
{
    public static CostCenterUnitBudgetResponse From(CostCenterUnitBudget b) =>
        new(new CostCenterUnitBudgetId(b.Id), new CostCenterId(b.CostCenterId), new UnitId(b.UnitId),
            new CompanyId(b.CompanyId), b.Competency, b.MonthlyLimit, b.Active);
}

/// <summary>Result of creating a budget over a period — one monthly cell per competency in the inclusive
/// range, all with the same limit. The model stays one row per month; the period is just how they're created
/// together.</summary>
[SwaggerSchema("What a period budget creation produced. The period is only a way to create many cells at "
    + "once — nothing stores it, and each month can be edited on its own afterwards.")]
public sealed record CostCenterUnitBudgetPeriodResponse(
    [property: SwaggerSchema("First month created (`YYYY-MM`), inclusive.")]
    [property: SwaggerExample("2026-01")]
    string StartCompetency,
    [property: SwaggerSchema("Last month created (`YYYY-MM`), inclusive.")]
    [property: SwaggerExample("2026-12")]
    string EndCompetency,
    [property: SwaggerSchema("How many monthly cells were created.")]
    [property: SwaggerExample("12")]
    int Count,
    [property: SwaggerSchema("The cells themselves, one per month of the range.")]
    IReadOnlyList<CostCenterUnitBudgetResponse> Items)
{
    public static CostCenterUnitBudgetPeriodResponse From(string startCompetency, string endCompetency, IReadOnlyList<CostCenterUnitBudget> cells) =>
        new(startCompetency, endCompetency, cells.Count, cells.Select(CostCenterUnitBudgetResponse.From).ToList());
}
