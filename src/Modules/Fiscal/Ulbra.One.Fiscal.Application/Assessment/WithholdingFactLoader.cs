using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Assessment;

/// <summary>Loads the flattened <see cref="WithholdingFact"/> input the assessment calculator consumes: every
/// withholding line of a payable whose issue date falls in the competência, joined with its company / cost
/// center / vendor. Reads Finance + Core + Registry via the shared context (loose Id, no cross-module FK).
/// Optionally scoped to company / cost center / tax / group.</summary>
internal static class WithholdingFactLoader
{
    public static async Task<List<WithholdingFact>> LoadAsync(IUnitOfWork uow, AssessmentFilter filter, CancellationToken ct)
    {
        var start = new DateOnly(filter.Period.Year, filter.Period.Month, 1);
        var end = start.AddMonths(1);

        // Only payables that carry withholdings, in the competência, skipping terminal-negative states.
        // Withholdings load via AutoInclude (PayableConfiguration).
        var q = uow.ReadSet<Payable>()
            .Where(p => p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                        && p.Withholdings.Any()
                        && p.IssueDate != null && p.IssueDate >= start && p.IssueDate < end);
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

            foreach (var w in p.Withholdings)
            {
                if (filter.TaxType is { } tt && w.TaxType != tt) continue;
                if (filter.Group is { } rg && RetentionGroups.For(w.TaxType) != rg) continue;

                facts.Add(new WithholdingFact(
                    p.Id, p.CounterpartyName, p.DocumentNumber,
                    p.CompanyId, company?.LegalName ?? string.Empty, company?.Cnpj ?? string.Empty,
                    p.CostCenterId, costCenter?.Code ?? string.Empty, costCenter?.Name ?? string.Empty,
                    p.VendorId, vendorDocument ?? string.Empty,
                    w.TaxType, w.BaseAmount, w.Amount, p.TotalAmount, p.IssueDate!.Value,
                    lc116ByPayable.GetValueOrDefault(p.Id)));
            }
        }

        return facts;
    }
}
