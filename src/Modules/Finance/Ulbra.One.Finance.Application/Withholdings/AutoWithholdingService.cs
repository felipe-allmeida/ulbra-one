using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Withholdings;

/// <summary>Computes the withholdings for a payable (via the Fiscal calculator) and applies them, keeping
/// the editable-until-scheduled contract. Does NOT call SaveChanges — the caller's transaction commits it.</summary>
public interface IAutoWithholdingService
{
    Task RecalculateAndApplyAsync(Payable payable, CancellationToken ct);
}

public sealed class AutoWithholdingService(IUnitOfWork uow, IWithholdingCalculator calculator) : IAutoWithholdingService
{
    public async Task RecalculateAndApplyAsync(Payable payable, CancellationToken ct)
    {
        // Never recompute once locked: the accounting incurral already booked the withholdings.
        if (payable.Status is PayableStatus.Scheduled or PayableStatus.Paid) return;

        // Need the service signals from the fiscal document; without an LC116 subitem (no doc, or NF-e product)
        // we don't fabricate withholdings — leave it for manual entry.
        var doc = await uow.ReadSet<FiscalDocument>().FirstOrDefaultAsync(d => d.PayableId == payable.Id, ct);
        if (doc?.Lc116Subitem is null) return;

        var regime = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId && v.CompanyId == payable.CompanyId)
            .Select(v => (TaxRegime?)v.TaxRegime)
            .FirstOrDefaultAsync(ct) ?? TaxRegime.Unknown;

        // Base = the line amount (BaseRule.Full / MVP). Single service line per NFS-e today.
        var line = new ServiceLineInput(doc.Lc116Subitem, doc.ServiceMunicipalityIbge, doc.NotaIssRate, payable.TotalAmount);
        var context = new WithholdingContext(
            payable.CompanyId, payable.VendorId, regime, payable.SimplesDeclarationProvided, [line]);

        var results = await calculator.CalculateAsync(context, ct);

        var old = payable.Withholdings.ToList();
        try
        {
            payable.SetWithholdings(results.Select(r => (r.TaxType, r.BaseAmount, (decimal?)r.Rate, r.Amount)));
        }
        catch (InvalidOperationException)
        {
            // Domain guard (e.g., total withheld >= gross, or registered payments). Best-effort: leave as-is.
            return;
        }

        // EF doesn't track the private withholding list: remove the stale rows, add the new ones. No SaveChanges here.
        uow.RemoveRange(old);
        foreach (var w in payable.Withholdings)
            await uow.AddAsync(w, ct);
    }
}
