using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Periods.CloseAccountingPeriod;

/// <summary>Closes an accounting period (creating it if absent), blocking further postings into that
/// competence. Idempotent — closing an already-closed period returns it unchanged.</summary>
public sealed class CloseAccountingPeriod : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company whose period is affected. Periods are per company: closing one company's "
            + "month leaves the others open.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Fiscal year of the period.")]
        [SwaggerExample("2026")]
        public int FiscalYear { get; set; }

        [SwaggerSchema("Month of the period, 1 to 12.")]
        [SwaggerExample("8")]
        public int Month { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/close", Handle).WithName("CloseAccountingPeriod").WithTags("Accounting")
            .WithSummary("Closes an accounting period.")
            .WithDescription("Blocks any further posting into that competence — the guard the "
                + "month-end close relies on. The period row is created on the fly if it does not exist "
                + "yet, so a month never posted into can still be closed. Idempotent: closing an "
                + "already-closed period returns it unchanged.")
            .RequirePermission(Permissions.AccountingPost)
            .Produces<AccountingPeriodResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (req.CompanyId.Value == Guid.Empty)
            return ErrorResults.InvalidRequest("CompanyId is required.", ErrorCodes.AccountingPeriod.Invalid);
        if (req.FiscalYear < 1 || req.Month is < 1 or > 12)
            return ErrorResults.InvalidRequest("FiscalYear and Month are required (Month 1..12).", ErrorCodes.AccountingPeriod.Invalid);

        var period = await uow.Set<AccountingPeriod>().FirstOrDefaultAsync(
            p => p.CompanyId == req.CompanyId.Value && p.FiscalYear == req.FiscalYear && p.Month == req.Month, ct);
        if (period is null)
        {
            period = new AccountingPeriod(req.CompanyId.Value, req.FiscalYear, req.Month);
            await uow.AddAsync(period, ct);
        }
        period.Close();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(AccountingPeriodResponse.From(period));
    }
}
