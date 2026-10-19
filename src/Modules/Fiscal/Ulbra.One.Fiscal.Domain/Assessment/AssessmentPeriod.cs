namespace Ulbra.One.Fiscal.Domain.Assessment;

/// <summary>Assessment period (competência) as year + month. Formats to the EFD-Reinf <c>perApur</c> (YYYY-MM).
/// The period source is per-tax (see WithholdingTimings.For / WithholdingFactLoader): emission-time retentions
/// (IRRF/INSS/ISS) are attributed by the invoice issue date; the CSRF group (PIS/COFINS/CSLL) by the payment
/// date, since its fato gerador is the payment. This struct just carries the resolved year/month.</summary>
public readonly record struct AssessmentPeriod(int Year, int Month)
{
    public static AssessmentPeriod From(DateOnly date) => new(date.Year, date.Month);

    /// <summary>EFD-Reinf <c>perApur</c> string (YYYY-MM).</summary>
    public string PerApur => $"{Year:D4}-{Month:D2}";

    public override string ToString() => PerApur;
}
