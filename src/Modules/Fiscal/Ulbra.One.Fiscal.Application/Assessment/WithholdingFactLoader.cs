using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Assessment;

/// <summary>Loads the flattened <see cref="WithholdingFact"/> input the assessment calculator consumes, joined
/// with each payable's company / cost center / vendor. Two sources, unioned by competência:
/// emission-time retentions (IRRF/INSS/ISS) attributed by the document issue date, and the CSRF group
/// (PIS/COFINS/CSLL) attributed by the payment date (recognized per baixa via <c>PaymentWithholding</c>). See
/// WithholdingTimings.For (Finance.Domain). Reads Finance + Core + Registry via the shared context (loose Id, no
/// cross-module FK). Optionally scoped to company / cost center / tax / group.</summary>
internal static class WithholdingFactLoader
{
    public static async Task<List<WithholdingFact>> LoadAsync(IUnitOfWork uow, AssessmentFilter filter, CancellationToken ct)
    {
        var start = new DateOnly(filter.Period.Year, filter.Period.Month, 1);
        var end = start.AddMonths(1);

        // A payable is in scope when EITHER its issue date falls in the competência and it carries a withholding
        // (emission facts), OR it has a non-reversed payment in the competência that carries a CSRF withholding
        // (payment facts). Installments/Payments/Withholdings load via AutoInclude.
        var q = uow.ReadSet<Payable>()
            .Where(p => p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected)
            .Where(p =>
                (p.IssueDate != null && p.IssueDate >= start && p.IssueDate < end && p.Withholdings.Any())
                || p.Installments.Any(i => i.Payments.Any(pay =>
                       !pay.IsReversed && pay.PaymentDate >= start && pay.PaymentDate < end && pay.Withholdings.Any())));
        if (filter.CompanyId is { } cid) q = q.Where(p => p.CompanyId == cid);
        if (filter.CostCenterId is { } ccid) q = q.Where(p => p.CostCenterId == ccid);

        var payables = await q.ToListAsync(ct);
        if (payables.Count == 0) return [];

        // Resolve the display fields (loose-Id joins) in bulk.
        var companyIds = payables.Select(p => p.CompanyId).Distinct().ToList();
        var costCenterIds = payables.Select(p => p.CostCenterId).Distinct().ToList();
        var vendorIds = payables.Select(p => p.VendorId).Distinct().ToList();

        var companies = await uow.ReadSet<Company>()
            .Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.LegalName, c.Cnpj })
            .ToDictionaryAsync(c => c.Id, ct);
        var costCenters = await uow.ReadSet<CostCenter>()
            .Where(c => costCenterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, ct);
        var vendorDocs = await uow.ReadSet<VendorEntity>()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Document })
            .ToDictionaryAsync(v => v.Id, v => v.Document, ct);

        // LC116 subitem (NFS-e) per payable — drives the EFD-Reinf natureza do rendimento in the export adapter.
        var payableIds = payables.Select(p => p.Id).ToList();
        var lc116ByPayable = await uow.ReadSet<FiscalDocument>()
            .Where(d => payableIds.Contains(d.PayableId) && d.Lc116Subitem != null)
            .Select(d => new { d.PayableId, d.Lc116Subitem })
            .ToDictionaryAsync(d => d.PayableId, d => d.Lc116Subitem, ct);

        var facts = new List<WithholdingFact>();
        foreach (var p in payables)
        {
            companies.TryGetValue(p.CompanyId, out var company);
            costCenters.TryGetValue(p.CostCenterId, out var costCenter);
            vendorDocs.TryGetValue(p.VendorId, out var vendorDocument);
            var companyName = company?.LegalName ?? string.Empty;
            var companyCnpj = company?.Cnpj ?? string.Empty;
            var costCenterCode = costCenter?.Code ?? string.Empty;
            var costCenterName = costCenter?.Name ?? string.Empty;
            var vendorDoc = vendorDocument ?? string.Empty;
            var lc116 = lc116ByPayable.GetValueOrDefault(p.Id);

            WithholdingFact Fact(WithholdingTaxType tax, decimal baseAmount, decimal amount, DateOnly eventDate) =>
                new(p.Id, p.CounterpartyName, p.DocumentNumber,
                    p.CompanyId, companyName, companyCnpj,
                    p.CostCenterId, costCenterCode, costCenterName,
                    p.VendorId, vendorDoc,
                    tax, baseAmount, amount, p.TotalAmount, eventDate, lc116);

            bool Passes(WithholdingTaxType tax) =>
                (filter.TaxType is not { } tt || tax == tt)
                && (filter.Group is not { } rg || RetentionGroups.For(tax) == rg);

            // Emission facts (IRRF/INSS/ISS) — attributed to the document issue date, only when it is in the window.
            if (p.IssueDate is { } issueDate && issueDate >= start && issueDate < end)
            {
                foreach (var w in p.Withholdings)
                {
                    if (WithholdingTimings.For(w.TaxType) != WithholdingTiming.OnIssue) continue; // CSRF comes from payments
                    if (!Passes(w.TaxType)) continue;
                    facts.Add(Fact(w.TaxType, w.BaseAmount, w.Amount, issueDate));
                }
            }

            // Payment facts (CSRF) — attributed to each non-reversed payment's date within the window.
            foreach (var pay in p.Installments.SelectMany(i => i.Payments))
            {
                if (pay.IsReversed || pay.PaymentDate < start || pay.PaymentDate >= end) continue;
                foreach (var pw in pay.Withholdings)
                {
                    if (!Passes(pw.TaxType)) continue;
                    facts.Add(Fact(pw.TaxType, pw.BaseAmount, pw.Amount, pay.PaymentDate));
                }
            }
        }

        return facts;
    }
}
