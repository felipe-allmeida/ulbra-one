using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Derives the default <see cref="DreLine"/> for an AELBRA "Modelo de Planos" account from its code,
/// mirroring the chart's synthetic subtrees. ULBRA-specific (like <c>AelbraChart.GroupOf</c>): it seeds a
/// sensible default that the ledger-account CRUD can override per account. Returns null for non-result accounts
/// (roots 1/2 — Ativo/Passivo/PL). The equivalent logic is replicated as raw SQL in the migration backfill;
/// keep the two in sync.</summary>
public static class DreLineClassifier
{
    public static DreLine? Classify(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();

        // Revenue (root 3)
        if (Under(code, "3"))
        {
            if (Under(code, "3.2.05")) return DreLine.GrossRevenue;         // gross operating revenue
            if (Under(code, "3.2.10")) return DreLine.RevenueDeductions;    // (−) revenue deductions
            if (Under(code, "3.2.25")) return DreLine.FinancialRevenue;     // financial revenue
            if (Under(code, "3.2.20") || Under(code, "3.2.80")) return DreLine.Other; // discontinued ops / provision reversal
            return DreLine.OtherOperatingIncome;                           // 3.2.15 and other operating revenue
        }

        // Cost (root 5) → COGS/COS
        if (Under(code, "5")) return DreLine.CostOfSales;

        // Expense (root 4)
        if (Under(code, "4"))
        {
            if (Under(code, "4.2.30")) return DreLine.FinancialExpense;     // financial expense
            if (Under(code, "4.2.35")) return IsSocialContribution(code) ? DreLine.SocialContribution : DreLine.IncomeTax;
            if (Under(code, "4.2.25")) return DreLine.Other;               // discontinued operations
            return DreLine.OperatingExpenses;                              // 4.2.05/10/15/20 and other operating expenses
        }

        // Asset/Liability/Equity are not on the DRE.
        return null;
    }

    /// <summary>Within the "DESPESA COM PROVISAO PARA CSLL E IRPJ" subtree (4.2.35), the CSLL analytical accounts;
    /// everything else in the subtree defaults to IRPJ (income tax).</summary>
    private static bool IsSocialContribution(string code) =>
        code is "4.2.35.05.020" or "4.2.35.05.060";

    /// <summary>True when <paramref name="code"/> is <paramref name="prefix"/> or a descendant of it (segment-aware,
    /// so "4.2.3" does not match the "4.2.30" prefix).</summary>
    private static bool Under(string code, string prefix) =>
        code == prefix || code.StartsWith(prefix + ".", StringComparison.Ordinal);
}
