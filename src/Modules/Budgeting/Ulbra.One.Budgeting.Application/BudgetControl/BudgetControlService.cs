using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;

namespace Ulbra.One.Budgeting.Application.BudgetControl;

/// <summary>Authoritative budget control. Aggregates the Core budget cells with the requisitions of a
/// (cost center, unit) dimension, per competency, using the empenho→liquidação model:
/// consumido = realizado (pagamentos reais) + empenhado (estimativa ainda não liquidada de requisições em
/// pagamento); previsto = estimativa das requisições em fila (triagem/aprovação/complemento). This is the
/// server-side successor of the interim client-side <c>budget-guard.ts</c>.</summary>
public sealed class BudgetControlService(
    ICostCenterBudgetLookup budgets, IBudgetDimensionLookup dimensions, IUnitOfWork uow) : IBudgetControl
{
    /// <summary>Horizon for open-ended recurrences (no EndCompetency): 12 months from the start.</summary>
    private const int OpenEndedHorizonMonths = 12;

    /// <summary>Upper bound on the evaluated window for closed ranges — bounds the in-memory scan.</summary>
    private const int MaxHorizonMonths = 36;

    public Task<IReadOnlyList<BudgetMonth>> DescribeAsync(
        Guid costCenterId, Guid unitId, string from, string to, Guid? excludeRequisitionId, CancellationToken ct) =>
        ComputeMonthsAsync(costCenterId, unitId, from, to, excludeRequisitionId, ct);

    public async Task<BudgetEvaluation> EvaluateCandidateAsync(Requisition candidate, CancellationToken ct)
    {
        var from = candidate.StartCompetency;
        var to = ResolveHorizon(candidate);
        var months = await ComputeMonthsAsync(candidate.CostCenterId, candidate.UnitId, from, to, candidate.Id, ct);

        // NB: Conta Fixa (AEL-881) is NOT exempted here on purpose. The read model keeps reporting the breach so
        // the panel/chart VISUALIZE the impact (the value still counts as consumido); only `approve`/`AddCycle`
        // waive the block. The inbox badge exemption for FixedExpense lives in the frontend (`budgetBadgeFor`).
        var breaches = new List<BudgetBreach>();
        var unbudgeted = new List<string>();
        foreach (var m in months)
        {
            var cost = candidate.EstimatedContributionFor(m.Competency);
            if (cost <= 0) continue;
            // No budget cell for this competency (Orcado=0): there is no ceiling to validate the spend
            // against, so BLOCK (AEL-796 product decision; previously "allow + highlight"). Still added
            // to `unbudgeted` so the panel highlights "no budget", and counted as a breach to block Approve.
            if (!m.Budgeted) { unbudgeted.Add(m.Competency); breaches.Add(new BudgetBreach(m.Competency, m.Orcado, m.Available, cost)); continue; }
            if (cost > m.Available) breaches.Add(new BudgetBreach(m.Competency, m.Orcado, m.Available, cost));
        }
        return new BudgetEvaluation(months, breaches, unbudgeted, breaches.Count > 0);
    }

    /// <summary>The competency window to evaluate: the candidate's closed range (capped at 36 months) or a
    /// 12-month horizon for open-ended recurrences.</summary>
    private static string ResolveHorizon(Requisition candidate)
    {
        var from = candidate.StartCompetency;
        if (candidate.EndCompetency is not { } end || CompetencyFormat.Compare(end, from) < 0)
            return CompetencyFormat.AddMonths(from, OpenEndedHorizonMonths - 1);
        var cap = CompetencyFormat.AddMonths(from, MaxHorizonMonths - 1);
        return CompetencyFormat.Compare(end, cap) > 0 ? cap : end;
    }

    private async Task<IReadOnlyList<BudgetMonth>> ComputeMonthsAsync(
        Guid costCenterId, Guid unitId, string from, string to, Guid? excludeRequisitionId, CancellationToken ct)
    {
        if (!CompetencyFormat.IsValid(from) || !CompetencyFormat.IsValid(to) || CompetencyFormat.Compare(to, from) < 0)
            return [];

        var cells = (await budgets.GetCellsAsync(costCenterId, unitId, from, to, ct))
            .ToDictionary(c => c.Competency, c => c.MonthlyLimit);

        // Other requisitions of the dimension whose window overlaps [from, to] (EF-translatable prefilter).
        var exclude = excludeRequisitionId ?? Guid.Empty;
        var others = await uow.ReadSet<Requisition>()
            .Where(r => r.CostCenterId == costCenterId && r.UnitId == unitId
                        && r.Status != RequisitionStatus.Cancelled
                        && r.Id != exclude
                        && string.Compare(r.StartCompetency, to) <= 0
                        && (r.EndCompetency == null || string.Compare(r.EndCompetency, from) >= 0))
            .Include(r => r.Payments)
            .ToListAsync(ct);

        var months = new List<BudgetMonth>();
        foreach (var competency in CompetencyFormat.EnumerateRange(from, to))
        {
            decimal realized = 0m, committed = 0m, forecast = 0m;
            foreach (var r in others)
            {
                var (rr, cc, ff) = ContributionOf(r, competency);
                realized += rr;
                committed += cc;
                forecast += ff;
            }
            months.Add(new BudgetMonth(competency, cells.GetValueOrDefault(competency), realized, committed, forecast));
        }
        return months;
    }

    /// <summary>Commitment→settlement split of one requisition's contribution to a competency:
    /// (realized, committed, forecast). The entire pre-payment pipeline (Triage/FinancialComplement/Complement/
    /// Approval) = forecast (does NOT reduce the real available — only projected on screens, AEL-844);
    /// InPayment/AwaitingFiscal = realized + committed (unliquidated estimate); Paid/Finalized = realized only;
    /// Draft/Cancelled = 0.</summary>
    private static (decimal Realized, decimal Committed, decimal Forecast) ContributionOf(Requisition r, string competency)
    {
        var contrib = r.EstimatedContributionFor(competency);
        var realizedHere = r.RealizedContributionFor(competency);
        return r.Status switch
        {
            RequisitionStatus.Triage or RequisitionStatus.FinancialComplement or RequisitionStatus.Approval or RequisitionStatus.Complement
                => (0m, 0m, contrib),
            RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument
                => (realizedHere, Math.Max(contrib - realizedHere, 0m), 0m),
            RequisitionStatus.Paid or RequisitionStatus.Finalized
                => (realizedHere, 0m, 0m),
            _ => (0m, 0m, 0m),
        };
    }

    public async Task<IReadOnlyList<CostCenterBudget>> SummarizeByCostCenterAsync(
        Guid companyId, Guid? unitId, string competency, CancellationToken ct)
    {
        if (!CompetencyFormat.IsValid(competency)) return [];

        var costCenters = await dimensions.ListActiveCostCentersAsync(companyId, ct);
        var orcado = (await budgets.SumOrcadoByCostCenterAsync(companyId, unitId, competency, ct))
            .ToDictionary(x => x.CostCenterId, x => x.Orcado);

        // Requisitions of the company (optionally one unit) whose window covers the competency.
        var reqs = await uow.ReadSet<Requisition>()
            .Where(r => r.CompanyId == companyId
                        && r.Status != RequisitionStatus.Cancelled
                        && (unitId == null || r.UnitId == unitId)
                        && string.Compare(r.StartCompetency, competency) <= 0
                        && (r.EndCompetency == null || string.Compare(r.EndCompetency, competency) >= 0))
            .Include(r => r.Payments)
            .ToListAsync(ct);

        var consumed = new Dictionary<Guid, (decimal Realized, decimal Committed, decimal Forecast)>();
        foreach (var r in reqs)
        {
            var (rr, cc, ff) = ContributionOf(r, competency);
            if (rr == 0m && cc == 0m && ff == 0m) continue;
            var cur = consumed.GetValueOrDefault(r.CostCenterId);
            consumed[r.CostCenterId] = (cur.Realized + rr, cur.Committed + cc, cur.Forecast + ff);
        }

        var lines = new List<CostCenterBudget>();
        foreach (var cc in costCenters)
        {
            var o = orcado.GetValueOrDefault(cc.Id);
            var c = consumed.GetValueOrDefault(cc.Id);
            // Skip cost centers with neither a ceiling nor activity in this competency.
            if (o == 0m && c.Realized == 0m && c.Committed == 0m && c.Forecast == 0m) continue;
            lines.Add(new CostCenterBudget(cc.Id, cc.Name, o, c.Realized, c.Committed, c.Forecast));
        }
        return lines;
    }

    public async Task<IReadOnlyList<CostCenterRealized>> SummarizeRealizedByCostCenterAsync(
        Guid companyId, CancellationToken ct)
    {
        // Realized = non-reversed payments, summed per cost center over ALL competencies (same axis as the
        // card's "annual budget"). Computed from the requisition side only — the Payable's payment flows back
        // as a RequisitionPayment, so there is no double count.
        //
        // Include + in-memory sum (via RealizedValue) — SAME technique as SummarizeByCostCenterAsync, for
        // parity with the detail summary strip. A correlated Sum over the Payments navigation inside a Select
        // projects 0 (the navigation isn't loaded), which zeroed the card.
        var reqs = await uow.ReadSet<Requisition>()
            .Where(r => r.CompanyId == companyId && r.Status != RequisitionStatus.Cancelled)
            .Include(r => r.Payments)
            .ToListAsync(ct);

        return reqs
            .GroupBy(r => r.CostCenterId)
            .Select(g => new CostCenterRealized(g.Key, g.Sum(r => r.RealizedValue)))
            .Where(x => x.Realized != 0m)
            .ToList();
    }

    public async Task<QueueProjection> SummarizeQueueProjectionAsync(
        Guid companyId, Guid? unitId, string from, string to, CancellationToken ct)
    {
        if (!CompetencyFormat.IsValid(from) || !CompetencyFormat.IsValid(to) || CompetencyFormat.Compare(to, from) < 0)
            return new QueueProjection([], [], 0m);

        // Bound the in-memory scan the same way ResolveHorizon does for a candidate.
        var cap = CompetencyFormat.AddMonths(from, MaxHorizonMonths - 1);
        if (CompetencyFormat.Compare(to, cap) > 0) to = cap;

        // One query: requisitions of the company (optionally one unit) whose window overlaps [from, to].
        var reqs = await uow.ReadSet<Requisition>()
            .Where(r => r.CompanyId == companyId
                        && r.Status != RequisitionStatus.Cancelled
                        && (unitId == null || r.UnitId == unitId)
                        && string.Compare(r.StartCompetency, to) <= 0
                        && (r.EndCompetency == null || string.Compare(r.EndCompetency, from) >= 0))
            .Include(r => r.Payments)
            .ToListAsync(ct);

        var months = new List<AggregateBudgetMonth>();
        var breaches = new List<string>();
        decimal queueTotal = 0m;
        foreach (var competency in CompetencyFormat.EnumerateRange(from, to))
        {
            // Ceiling = orçado of ALL cost centers in the competency (previsto/fila does not reduce it — AEL-844).
            var orcado = (await budgets.SumOrcadoByCostCenterAsync(companyId, unitId, competency, ct)).Sum(x => x.Orcado);
            decimal consumed = 0m, forecast = 0m;
            foreach (var r in reqs)
            {
                var (rr, cc, ff) = ContributionOf(r, competency);
                consumed += rr + cc;
                forecast += ff;
            }
            queueTotal += forecast;
            var m = new AggregateBudgetMonth(competency, orcado, consumed, forecast);
            months.Add(m);
            // Breach = approving the whole queue would push consumido + previsto over the ceiling.
            if (m.Budgeted && consumed + forecast > orcado) breaches.Add(competency);
        }
        return new QueueProjection(months, breaches, queueTotal);
    }
}
