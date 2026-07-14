using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Periods;

/// <summary>The closed-period guard, shared by every posting path. Behaviour is deliberately asymmetric:
/// manual endpoints <b>block</b> (the human picks another competence); automatic postings (CaP) <b>clamp</b>
/// to the current open period via <see cref="IsClosedAsync"/> so an incurral is never parked while its
/// settlement posts — which would leave the liability unbalanced. Absence of a period row means Open;
/// only <c>Closed</c> blocks (<c>Closing</c> still accepts adjustment postings).</summary>
public static class AccountingPeriodGuard
{
    /// <summary>True when the (company, competence-month) is in a <c>Closed</c> period. A missing period row
    /// is treated as Open (permissive).</summary>
    public static async Task<bool> IsClosedAsync(
        IUnitOfWork uow, Guid companyId, DateOnly competenceDate, CancellationToken ct)
    {
        var status = await uow.ReadSet<AccountingPeriod>()
            .Where(p => p.CompanyId == companyId
                && p.FiscalYear == competenceDate.Year
                && p.Month == competenceDate.Month)
            .Select(p => (AccountingPeriodStatus?)p.Status)
            .FirstOrDefaultAsync(ct);
        return status == AccountingPeriodStatus.Closed;
    }

    /// <summary>Manual-path guard: a 409 problem when the competence falls in a Closed period, else null.</summary>
    public static async Task<IResult?> EnsureOpenAsync(
        IUnitOfWork uow, Guid companyId, DateOnly competenceDate, CancellationToken ct)
    {
        if (await IsClosedAsync(uow, companyId, competenceDate, ct))
            return TypedResults.Problem(
                detail: AccountingPeriodErrors.Closed.Description,
                statusCode: StatusCodes.Status409Conflict,
                title: AccountingPeriodErrors.Closed.Code);
        return null;
    }
}
