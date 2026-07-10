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

        // Product: no source withholding. Clear stale rows (a payable corrected from Service to Product,
        // or an NF-e uploaded over an NFS-e) so each installment's NetAmount returns to gross.
        if (payable.Type == PayableType.Product)
        {
            if (payable.Withholdings.Count == 0) return;
            var stale = payable.Withholdings.ToList();
            try { payable.SetWithholdings([]); }
            catch (InvalidOperationException) { return; } // domain guard (registered payments) — best-effort
            uow.RemoveRange(stale);
            return;
        }

        // Withholding is a service obligation of the paying source. The trigger is service-nature — NOT the
        // LC116 subitem, which only refines specific taxes (INSS on labor supply, ISS by municipality). Skip
        // undetermined (null); a service still withholds the general federal taxes even without a subitem.
        if (payable.Type != PayableType.Service) return;

        // The fiscal document refines the calculation when present (LC116 subitem, municipality, ISS rate), but a
        // service may have none of it (doc-less manual payable, or a PDF/NFS-e without those fields).
        var doc = await uow.ReadSet<FiscalDocument>().FirstOrDefaultAsync(d => d.PayableId == payable.Id, ct);

        var vendorTax = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId && v.CompanyId == payable.CompanyId)
            .Select(v => new { v.TaxRegime, v.SimplesAnexo, v.CprbForeseen })
            .FirstOrDefaultAsync(ct);
        var regime = vendorTax?.TaxRegime ?? TaxRegime.Unknown;
        // INSS: usa o CPRB confirmado na NF quando houver; senão cai na sugestão do cadastro (CprbForeseen).
        var cprb = payable.CprbOnInvoice ?? vendorTax?.CprbForeseen;

        // Base = the payable's gross (BaseRule.Full / MVP). Single service line today; refinement signals are
        // null when there is no document or the document didn't carry them. INSS: o anexo do Simples vem do cadastro.
        var line = new ServiceLineInput(doc?.Lc116Subitem, doc?.ServiceMunicipalityIbge, doc?.InvoiceIssRate, payable.TotalAmount);
        var context = new WithholdingContext(
            payable.CompanyId, payable.VendorId, regime, [line], cprb, vendorTax?.SimplesAnexo);

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
