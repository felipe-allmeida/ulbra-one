using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Splits a result account (natureza "4") into Revenue or Expense.
/// Strategy: explicit code overrides first; then prefix-based routing for the
/// Imunes/Isentas branch (4.01 = Receitas, 4.02 = Despesas); finally name-based
/// fallback for PJ Lucro Real (class 3 cascade has no clean prefix). Synthetic
/// calculation nodes (e.g. "RESULTADO LÍQUIDO") have no journal lines so their
/// group is only cosmetic and falls back to Expense via the name fallback.</summary>
public static class ResultAccountClassifier
{
    private static readonly IReadOnlyDictionary<string, AccountGroup> Overrides =
        new Dictionary<string, AccountGroup>();

    public static AccountGroup Classify(string code, string name)
    {
        if (Overrides.TryGetValue(code, out var group))
            return group;
        // Imunes/Isentas result branch is clean by code prefix: 4.01 = Receitas, 4.02 = Despesas.
        if (code == "4.01" || code.StartsWith("4.01.", StringComparison.Ordinal))
            return AccountGroup.Revenue;
        if (code == "4.02" || code.StartsWith("4.02.", StringComparison.Ordinal))
            return AccountGroup.Expense;
        // PJ Lucro Real result (class 3) is a calculation cascade with no clean prefix — fall back to name.
        return name.Contains("RECEITA", StringComparison.OrdinalIgnoreCase)
            ? AccountGroup.Revenue
            : AccountGroup.Expense;
    }
}
