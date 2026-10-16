using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An accounting period (competência mensal) and its lifecycle status.</summary>
[SwaggerSchema("One month of accounting for a company, and where it is in its lifecycle. A closed period "
    + "refuses new postings, which is what makes a payable's competence get clamped forward instead.")]
public sealed record AccountingPeriodResponse(
    AccountingPeriodId Id,
    [property: SwaggerSchema("Company the period belongs to. Periods are closed per company, not globally.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Fiscal year the period falls in.")]
    [property: SwaggerExample("2026")]
    int FiscalYear,
    [property: SwaggerSchema("Month of the period, 1-12.")]
    [property: SwaggerExample("7")]
    int Month,
    [property: SwaggerSchema("Lifecycle status. Only an open period accepts postings; closing is what "
        + "freezes the month, and reopening is an explicit, audited step.")]
    AccountingPeriodStatus Status)
{
    public static AccountingPeriodResponse From(AccountingPeriod p) => new(
        new AccountingPeriodId(p.Id),
        new CompanyId(p.CompanyId),
        p.FiscalYear,
        p.Month,
        p.Status);
}
