using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetTrialBalance;

/// <summary>Balancete de verificação: one row per account with its opening balance, the period's debit/credit
/// movement and the closing balance. <c>from</c>/<c>to</c> scope the competence range (omitted = full history,
/// the legacy accumulated view); <c>costCenterId</c> filters the analytical dimension (reuses #6-B). Consolidated
/// when CompanyId is null.</summary>
/// <summary>Query-string filters for the trial balance. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetTrialBalanceRequest
{
    [SwaggerSchema("Company to report on. Omit to consolidate across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Start of the competence range, inclusive.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }

    [SwaggerSchema("End of the competence range, inclusive.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }

    [SwaggerSchema("Restrict to entry lines charged to this cost center. Lines without one are excluded.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Restrict to entry lines charged to this unit. Lines without one are excluded.")]
    public UnitId? UnitId { get; set; }
}

public sealed class GetTrialBalance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/trial-balance", Handle).WithName("GetTrialBalance").WithTags("Accounting")
            .WithSummary("Gets the trial balance.")
            .WithDescription("Balancete: per account, the opening balance, the debit and credit movement in the range and "
                + "the closing balance. Only POSTED entries count - drafts never reach any report. Filtering by "
                + "cost center or unit narrows it to lines carrying that dimension, so the result no longer "
                + "ties to the full ledger.")
            .RequirePermission(Permissions.AccountingRead)
            .Produces<TrialBalanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] GetTrialBalanceRequest req, CancellationToken ct)
    {
        var (companyId, from, to, costCenterId, unitId) =
            (req.CompanyId, req.From, req.To, req.CostCenterId, req.UnitId);

        if (from is { } f && to is { } t && t < f)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.TrialBalance.InvalidRange);

        var lines = await PostedLinesLoader.LoadAsync(uow, companyId?.Value, ct, costCenterId?.Value, unitId?.Value);
        var trialBalance = TrialBalanceCalculator.Build(lines, from, to);
        return TypedResults.Ok(TrialBalanceResponse.Of(trialBalance, companyId, from, to, costCenterId));
    }
}
