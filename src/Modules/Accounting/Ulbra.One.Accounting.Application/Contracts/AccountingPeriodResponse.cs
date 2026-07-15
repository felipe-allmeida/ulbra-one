using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An accounting period (competência mensal) and its lifecycle status.</summary>
public sealed record AccountingPeriodResponse(
    AccountingPeriodId Id,
    CompanyId CompanyId,
    int FiscalYear,
    int Month,
    AccountingPeriodStatus Status)
{
    public static AccountingPeriodResponse From(AccountingPeriod p) => new(
        new AccountingPeriodId(p.Id),
        new CompanyId(p.CompanyId),
        p.FiscalYear,
        p.Month,
        p.Status);
}
