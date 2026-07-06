namespace Ulbra.One.Fiscal.Domain.Assessment;

/// <summary>Assessment period (competência) as year + month. Formats to the EFD-Reinf <c>perApur</c> (YYYY-MM).
/// Today all withholdings are on PJ suppliers → regime de competência → the period comes from the invoice
/// issue date. When PF/IRRF (regime de caixa) enters with the payroll module, the period source becomes
/// per-tax and this is resolved from the payment date instead.</summary>
public readonly record struct AssessmentPeriod(int Year, int Month)
{
    public static AssessmentPeriod From(DateOnly date) => new(date.Year, date.Month);

    /// <summary>EFD-Reinf <c>perApur</c> string (YYYY-MM).</summary>
    public string PerApur => $"{Year:D4}-{Month:D2}";

    public override string ToString() => PerApur;
}
