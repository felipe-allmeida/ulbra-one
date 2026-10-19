using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.CashFlow;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.Endpoints.CashFlow;

/// <summary>Cash-flow projection over the existing payable/receivable/payment/receipt aggregates.</summary>
public sealed class GetCashFlow : IEndpoint
{
    private const int MaxHorizon = 60;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetCashFlow").WithTags("Finance")
            .WithSummary("Projects the cash flow.")
            .WithDescription("Forecast built from what is already recorded - outstanding payable and receivable "
                + "installments - bucketed over a horizon. It projects from commitments, not from a model: "
                + "nothing is estimated. The starting point is the reconciled balance anchor of the bank "
                + "accounts, so the projection is only as accurate as the last balance someone confirmed.")
            .RequirePermission(Permissions.TreasuryRead).Produces<CashFlowResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company to project. Omit to consolidate across every company.")]
        CompanyId? companyId,
        [SwaggerSchema("Bucket size of the projection.")]
        CashFlowGranularity? granularity,
        [SwaggerSchema("How many buckets to project. Capped at 60.")]
        [SwaggerExample("12")]
        int? horizon,
        [SwaggerSchema("Date the projection starts from. Omitted uses today.")]
        [SwaggerExample("2026-09-01")]
        DateOnly? asOf,
        [SwaggerSchema("Minimum cash reserve to flag against, in BRL. Buckets projected below it are "
            + "marked, which is what surfaces an upcoming squeeze.")]
        [SwaggerExample("100000.00")]
        decimal? minReserve,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var gran = granularity ?? CashFlowGranularity.Month;
        var periods = Math.Clamp(horizon ?? 12, 1, MaxHorizon);
        var reserve = minReserve ?? 0m;
        Guid? cid = companyId?.Value;

        var (accounts, startingBalance) = await CashFlowQueries.AccountBalancesAsync(uow, cid, ct);
        var forecastOut = await CashFlowQueries.ForecastOutflowsAsync(uow, cid, ct);
        var forecastIn = await CashFlowQueries.ForecastInflowsAsync(uow, cid, ct);
        var actualOut = await CashFlowQueries.ActualOutflowsAsync(uow, cid, ct);
        var actualIn = await CashFlowQueries.ActualInflowsAsync(uow, cid, ct);

        var result = CashFlowCalculator.Calculate(new CashFlowInput(
            startingBalance, today, gran, periods, reserve,
            forecastIn, forecastOut, actualIn, actualOut));

        var response = new CashFlowResponse(
            today, gran, CashFlowQueries.DefaultCurrency, startingBalance, accounts,
            new CashFlowKpis(
                result.Kpis.StartingBalance, result.Kpis.LowestProjectedBalance,
                result.Kpis.LowestProjectedPeriodLabel, result.Kpis.RunwayPeriods,
                result.Kpis.NetResult, result.Kpis.TotalInflowForecast, result.Kpis.TotalOutflowForecast),
            result.OverdueReceivable,
            result.Periods.Select(p => new CashFlowPeriod(
                p.Start, p.End, p.Label, p.IsPast, p.OpeningBalance, p.ClosingBalance,
                p.InflowForecast, p.OutflowForecast, p.NetForecast,
                p.InflowActual, p.OutflowActual, p.BelowReserve)).ToList());

        return TypedResults.Ok(response);
    }
}
