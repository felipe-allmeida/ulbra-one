using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Endpoints.Payables.ListPayables;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.PaymentQueueSummary;

/// <summary>
/// Whole-set due-date aggregates for the payment queue. Takes the SAME filters as ListPayables
/// (via <see cref="PayableQueueFilters"/>) so the group headers recompute with search/method filters.
/// </summary>
public sealed class GetPaymentQueueSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("payment-queue-summary", Handle)
            .WithName("GetPaymentQueueSummary").WithTags("Finance")
            .WithSummary("Summarizes the payment queue by due date.")
            .WithDescription("Whole-set totals grouped by due date for the payment queue. It takes the SAME filters as the "
                + "payable list, so the group headers always agree with the rows below them - including under a "
                + "search or a payment-method filter. The aggregates cover the entire filtered set, not just "
                + "the current page.")
            .RequirePermission(Permissions.PayablesRead)
            .Produces<PaymentQueueSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListPayablesRequest req,
        UserContext user,
        TimeProvider clock,
        CancellationToken ct)
    {
        // A present companyId always goes through the per-company opt-in filter (AEL-906): a management role
        // WITH a grant is restricted to it, so consolidated ≠ "reads any company". An omitted companyId
        // (consolidated "Todas") is allowed only for a consolidated role.
        if (req.CompanyId is { } companyId)
        {
            if (!user.CanAccess(companyId.Value))
                return ErrorResults.Forbidden("No access to this company.", ErrorCodes.Payable.CompanyForbidden);
        }
        else if (!user.CanSeeConsolidated)
        {
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);
        }

        var payables = await PayableQueueFilters.ApplyAsync(uow.ReadSet<Payable>(), uow, req, ct);

        // Installment-level filters intentionally mirror ListPayables (pinned by an integration test);
        // an anonymous-type-generic helper isn't worth the indirection.
        var rows = payables.SelectMany(p => p.Installments, (p, i) => new
        {
            i.DueDate,
            i.NetAmount,
            InstallmentStatus = i.Status,
        });
        if (req.DueFrom is { } from) rows = rows.Where(x => x.DueDate >= from);
        if (req.DueTo is { } to) rows = rows.Where(x => x.DueDate <= to);
        if (req.Paid is true) rows = rows.Where(x => x.InstallmentStatus == InstallmentStatus.Paid);
        if (req.Paid is false) rows = rows.Where(x => x.InstallmentStatus != InstallmentStatus.Paid);

        var groups = await rows
            .GroupBy(x => x.DueDate)
            .Select(g => new { DueDate = g.Key, Count = g.Count(), NetTotal = g.Sum(x => x.NetAmount) })
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct);

        // Fold past-due groups into a single "Vencidos" bucket; keep the rest per day.
        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var overdueCount = 0;
        var overdueNetTotal = 0m;
        var dayGroups = new List<PaymentQueueDayGroup>();
        foreach (var g in groups)
        {
            if (g.DueDate < asOf)
            {
                overdueCount += g.Count;
                overdueNetTotal += g.NetTotal;
            }
            else
            {
                dayGroups.Add(new PaymentQueueDayGroup(g.DueDate, g.Count, g.NetTotal));
            }
        }

        return TypedResults.Ok(new PaymentQueueSummaryResponse(
            asOf, overdueCount, overdueNetTotal, dayGroups,
            overdueCount + dayGroups.Sum(g => g.Count),
            overdueNetTotal + dayGroups.Sum(g => g.NetTotal)));
    }
}
