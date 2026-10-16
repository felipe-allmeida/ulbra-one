using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.CashFlow;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.CashFlow;

/// <summary>
/// Shared EF queries that feed the pure <see cref="CashFlowCalculator"/>. Keeps the data-access
/// rules (which statuses count, how the anchored balance is computed) in one place so the report
/// endpoint, the drill endpoint and the reconciled-balance endpoint stay consistent.
/// </summary>
internal static class CashFlowQueries
{
    /// <summary>Default reporting currency. Generic per-tenant currency config is deferred (see spec).</summary>
    public const string DefaultCurrency = "BRL";

    /// <summary>Outstanding payable installments (forecast outflows) in scope, as dated amounts.</summary>
    public static async Task<List<DatedAmount>> ForecastOutflowsAsync(IUnitOfWork uow, Guid? companyId, CancellationToken ct)
    {
        var q = from p in uow.ReadSet<Payable>()
                where (companyId == null || p.CompanyId == companyId)
                      && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                from i in p.Installments
                where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
                select new DatedAmount(i.DueDate, i.Amount - i.PaidAmount);
        return await q.ToListAsync(ct);
    }

    /// <summary>Outstanding receivable installments (forecast inflows) in scope, as dated amounts.</summary>
    public static async Task<List<DatedAmount>> ForecastInflowsAsync(IUnitOfWork uow, Guid? companyId, CancellationToken ct)
    {
        var q = from r in uow.ReadSet<Receivable>()
                where (companyId == null || r.CompanyId == companyId)
                      && r.Status != ReceivableStatus.Cancelled && r.Status != ReceivableStatus.WrittenOff
                from i in r.Installments
                where i.Status == ReceivableInstallmentStatus.Open || i.Status == ReceivableInstallmentStatus.PartiallyReceived
                select new DatedAmount(i.DueDate, i.Amount - i.ReceivedAmount);
        return await q.ToListAsync(ct);
    }

    /// <summary>Realized outflows (non-reversed payments) in scope, by effective payment date.</summary>
    public static async Task<List<DatedAmount>> ActualOutflowsAsync(IUnitOfWork uow, Guid? companyId, CancellationToken ct)
    {
        var q = from p in uow.ReadSet<Payable>()
                where companyId == null || p.CompanyId == companyId
                from i in p.Installments
                from pay in i.Payments
                where !pay.IsReversed
                select new DatedAmount(pay.PaymentDate, pay.Amount);
        return await q.ToListAsync(ct);
    }

    /// <summary>Realized inflows (non-reversed receipts) in scope, by effective receipt date.</summary>
    public static async Task<List<DatedAmount>> ActualInflowsAsync(IUnitOfWork uow, Guid? companyId, CancellationToken ct)
    {
        var q = from r in uow.ReadSet<Receivable>()
                where companyId == null || r.CompanyId == companyId
                from i in r.Installments
                from rec in i.Receipts
                where !rec.IsReversed
                select new DatedAmount(rec.ReceiptDate, rec.Amount);
        return await q.ToListAsync(ct);
    }

    /// <summary>
    /// Current balance per active account in scope: the reconciliation anchor plus net movements
    /// (receipts − payments) after <c>ReconciledAt</c>; falls back to <c>OpeningBalance</c> when there
    /// is no anchor. Returns the per-account balances (for the report's account panel) and their sum.
    /// </summary>
    public static async Task<(List<CashFlowAccountBalance> Accounts, decimal Total)> AccountBalancesAsync(
        IUnitOfWork uow, Guid? companyId, CancellationToken ct)
    {
        var accounts = await uow.ReadSet<BankAccount>()
            .Where(a => a.Active && (companyId == null || a.CompanyId == companyId))
            .OrderBy(a => a.Alias)
            .Select(a => new { a.Id, a.Alias, a.OpeningBalance, a.ReconciledBalance, a.ReconciledAt })
            .ToListAsync(ct);

        var ids = accounts.Select(a => a.Id).ToHashSet();

        // Movements grouped per bank account (only non-reversed). Computed in memory after a flat pull;
        // the row counts here are modest and this keeps the date-vs-anchor comparison straightforward.
        var payments = await (from p in uow.ReadSet<Payable>()
                              from i in p.Installments
                              from pay in i.Payments
                              where !pay.IsReversed && ids.Contains(pay.BankAccountId)
                              select new { pay.BankAccountId, pay.PaymentDate, pay.Amount }).ToListAsync(ct);
        var receipts = await (from r in uow.ReadSet<Receivable>()
                              from i in r.Installments
                              from rec in i.Receipts
                              where !rec.IsReversed && ids.Contains(rec.BankAccountId)
                              select new { rec.BankAccountId, rec.ReceiptDate, rec.Amount }).ToListAsync(ct);

        var result = new List<CashFlowAccountBalance>(accounts.Count);
        decimal total = 0;
        foreach (var a in accounts)
        {
            decimal current;
            if (a.ReconciledBalance is { } anchor && a.ReconciledAt is { } anchorDate)
            {
                var inAfter = receipts.Where(x => x.BankAccountId == a.Id && x.ReceiptDate > anchorDate).Sum(x => x.Amount);
                var outAfter = payments.Where(x => x.BankAccountId == a.Id && x.PaymentDate > anchorDate).Sum(x => x.Amount);
                current = anchor + inAfter - outAfter;
            }
            else
            {
                current = a.OpeningBalance ?? 0m;
            }

            total += current;
            result.Add(new CashFlowAccountBalance(new Common.Application.Ids.BankAccountId(a.Id), a.Alias, current, a.ReconciledAt));
        }

        return (result, total);
    }
}
