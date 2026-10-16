using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.PaymentsReport;

/// <summary>
/// Whole-set aggregate of the Relação de Pagamentos, behind the "Copiar Resumo" button: count + total and
/// breakdowns by unit and by vendor. Same filters as the list, but never paginated.
/// </summary>
public sealed class GetPaymentsReportSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/summary", Handle).WithName("GetPaymentsReportSummary").WithTags("Finance")
            .WithSummary("Summarizes the payments report for the copy-summary action.")
            .WithDescription("Aggregates the WHOLE filtered set (not a page): the row count, the gross total, and "
                + "the totals grouped by unit and by vendor, each ordered by amount descending. Takes the same "
                + "filters as the report list.")
            .RequirePermission(Permissions.PayablesRead)
            .Produces<PaymentsReportSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        UserContext user,
        [AsParameters] PaymentsReportRequest req,
        CancellationToken ct)
    {
        if (req.CompanyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);

        var rows = PaymentsReportQuery.Build(uow, req);

        var count = await rows.CountAsync(ct);
        var total = await rows.SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

        var byUnitRaw = await rows
            .GroupBy(r => r.OriginUnitId)
            .Select(g => new { UnitId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(ct);

        var byProvider = (await rows
                .GroupBy(r => r.CounterpartyName)
                .Select(g => new { Name = g.Key, Total = g.Sum(r => r.Amount) })
                .ToListAsync(ct))
            .OrderByDescending(g => g.Total)
            .Select(g => new PaymentsReportGroupTotal(g.Name, g.Total))
            .ToList();

        // Resolve unit names (imported rows have no unit → "Externo"), then order by amount descending.
        var unitIds = byUnitRaw.Where(u => u.UnitId != null).Select(u => u.UnitId!.Value).Distinct().ToList();
        var unitNames = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<Unit>().Where(u => unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);

        var byUnit = byUnitRaw
            .OrderByDescending(u => u.Total)
            .Select(u => new PaymentsReportGroupTotal(
                u.UnitId is { } id && unitNames.TryGetValue(id, out var name) ? name : "Externo", u.Total))
            .ToList();

        return TypedResults.Ok(new PaymentsReportSummaryResponse(count, total, byUnit, byProvider));
    }
}
