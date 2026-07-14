using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Periods.ReopenAccountingPeriod;

/// <summary>Reopens an accounting period (creating it if absent) so postings into that competence are
/// allowed again. Idempotent — reopening an already-open period returns it unchanged.</summary>
public sealed class ReopenAccountingPeriod : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        public int FiscalYear { get; set; }
        public int Month { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/reopen", Handle).WithName("ReopenAccountingPeriod").WithTags("Accounting")
            .RequireAuthorization()
            .Produces<AccountingPeriodResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (req.CompanyId.Value == Guid.Empty)
            return TypedResults.Problem(detail: "CompanyId is required.", statusCode: 400, title: "AccountingPeriod.Invalid");
        if (req.FiscalYear < 1 || req.Month is < 1 or > 12)
            return TypedResults.Problem(detail: "FiscalYear and Month are required (Month 1..12).", statusCode: 400, title: "AccountingPeriod.Invalid");

        var period = await uow.Set<AccountingPeriod>().FirstOrDefaultAsync(
            p => p.CompanyId == req.CompanyId.Value && p.FiscalYear == req.FiscalYear && p.Month == req.Month, ct);
        if (period is null)
        {
            period = new AccountingPeriod(req.CompanyId.Value, req.FiscalYear, req.Month);
            await uow.AddAsync(period, ct);
        }
        period.Reopen();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(AccountingPeriodResponse.From(period));
    }
}
