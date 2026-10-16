namespace Ulbra.One.Finance.Domain.Withholdings;

/// <summary>Pure arithmetic for spreading a payment-time retention (CSRF) across a payable's payments.
/// Running-sum method: each payment gets (ideal cumulative withheld through it) − (already withheld before it),
/// so per-payment rounding never drifts and the payment that settles the payable lands exactly on the total.</summary>
public static class WithholdingProration
{
    /// <summary>This payment's share of a single payment-time retention.</summary>
    /// <param name="total">The document-level retention total for the tax (Σ over the whole payable).</param>
    /// <param name="cumulativePrincipal">Net principal applied by all non-reversed payments up to and including this one.</param>
    /// <param name="netTotal">The payable's total net cash (bruto − all retentions); Σ principal over the life.</param>
    /// <param name="alreadyWithheld">Amount of this tax already withheld on prior non-reversed payments.</param>
    /// <param name="fullyPaid">Whether this payment settles the payable — it then absorbs the rounding residual.</param>
    /// <returns>The amount to withhold now (never negative).</returns>
    public static decimal ForPayment(decimal total, decimal cumulativePrincipal, decimal netTotal, decimal alreadyWithheld, bool fullyPaid)
    {
        if (total <= 0 || netTotal <= 0) return 0m;
        var idealCumulative = fullyPaid
            ? total
            : Math.Round(total * cumulativePrincipal / netTotal, 2, MidpointRounding.AwayFromZero);
        var amount = idealCumulative - alreadyWithheld;
        return amount > 0 ? amount : 0m;
    }
}
