using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>Builds the incurral payload events (<see cref="PayableScheduled"/> / <see cref="PayableReclassified"/>)
/// from a payable + vendor snapshot. Shared by the CompleteAccounting endpoint, the auto-schedule consumer and the
/// reclassify endpoint so they can't drift. Only OnIssue retentions (IRRF/INSS/ISS) enter here; CSRF is recognized
/// at each baixa.</summary>
public static class PayableScheduledFactory
{
    public static PayableScheduled Build(Payable p, string? vendorName, string? vendorDocument) =>
        new(p.Id, p.CompanyId, p.TotalAmount, p.AccountingAccountId, OnIssueWithholdings(p),
            p.IssueDate, p.CostCenterId, p.DocumentNumber,
            CounterpartyName: vendorName ?? p.CounterpartyName,
            CounterpartyDocument: vendorDocument,
            UnitId: p.OriginUnitId);

    public static PayableReclassified BuildReclassified(Payable p, string? vendorName, string? vendorDocument) =>
        new(p.Id, p.CompanyId, p.TotalAmount, p.AccountingAccountId, OnIssueWithholdings(p),
            p.IssueDate, p.CostCenterId, p.DocumentNumber,
            CounterpartyName: vendorName ?? p.CounterpartyName,
            CounterpartyDocument: vendorDocument,
            UnitId: p.OriginUnitId);

    private static List<ScheduledWithholding> OnIssueWithholdings(Payable p) =>
        p.Withholdings
            .Where(w => WithholdingTimings.For(w.TaxType) == WithholdingTiming.OnIssue)
            .Select(w => new ScheduledWithholding(w.TaxType.ToString(), w.Amount))
            .ToList();
}
