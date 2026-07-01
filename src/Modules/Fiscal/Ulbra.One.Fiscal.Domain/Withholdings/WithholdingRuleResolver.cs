using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Domain.Withholdings;

/// <summary>A service line of the invoice: the calculation base is the line, not the document. The caller only
/// builds a line for a service (products are filtered upstream by service-nature). A null
/// <paramref name="Lc116Subitem"/> means the specific subitem is unknown — the general federal taxes still apply;
/// only INSS (labor supply) and ISS (by municipality) need the subitem/municipality to refine.</summary>
public sealed record ServiceLine(string? Lc116Subitem, string? MunicipalityIbge, decimal? NotaIssRate, decimal Amount);

/// <summary>Resolution inputs: company, vendor regime, whether the Simples declaration is on file, and the lines.</summary>
public sealed record ResolutionContext(
    Guid? CompanyId, TaxRegime VendorRegime, bool SimplesDeclarationProvided, IReadOnlyList<ServiceLine> Lines);

/// <summary>A computed withholding (atomic per tax), ready to become a PayableWithholding.</summary>
public sealed record WithholdingComputation(WithholdingTaxType TaxType, decimal BaseAmount, decimal Rate, decimal Amount);

/// <summary>
/// Resolves withholdings PER TAX from the rules and the context. Pure (no EF) and deterministic.
/// Federal is robust (key = LC116 + regime); ISS is best-effort (needs municipality + rate).
/// </summary>
public static class WithholdingRuleResolver
{
    private static readonly WithholdingTaxType[] WithholdingTaxes =
    [
        WithholdingTaxType.Irrf, WithholdingTaxType.Pis, WithholdingTaxType.Cofins,
        WithholdingTaxType.Csll, WithholdingTaxType.Inss, WithholdingTaxType.Iss,
    ];

    public static IReadOnlyList<WithholdingComputation> Resolve(
        IReadOnlyCollection<WithholdingRule> rules, ResolutionContext context)
    {
        // Accumulate per atomic tax, summing across the service lines.
        var perTax = new Dictionary<WithholdingTaxType, (decimal Base, decimal Amount, decimal Rate)>();

        foreach (var line in context.Lines)
        {
            // Every line here is a service (the caller gates on service-nature). The LC116 subitem is a
            // refinement, not a gate: a null subitem still matches the general federal rules (Lc116Subitem=null),
            // while subitem-specific rules (INSS 11.02) only match when the line carries that subitem.
            foreach (var tax in WithholdingTaxes)
            {
                var rule = ResolveRule(rules, tax, line, context);
                if (rule is null || !rule.Retains) continue;

                // Fixed: rate from the rule. FromNota: rate from the invoice itself (Simples ISS = provider's band).
                var rate = rule.RateSource == RateSource.FromNota ? line.NotaIssRate : rule.Rate;
                if (rate is null or <= 0) continue; // best-effort: without a rate we cannot compute

                var baseAmount = line.Amount; // BaseRule.Full (MVP)
                var amount = Math.Round(baseAmount * rate.Value, 2, MidpointRounding.AwayFromZero);
                if (amount <= 0) continue;

                var acc = perTax.GetValueOrDefault(tax);
                perTax[tax] = (acc.Base + baseAmount, acc.Amount + amount, rate.Value);
            }
        }

        ApplyGroupMinimum(rules, perTax);

        return perTax
            .Select(kv => new WithholdingComputation(kv.Key, kv.Value.Base, kv.Value.Rate, kv.Value.Amount))
            .OrderBy(c => c.TaxType)
            .ToList();
    }

    /// <summary>Picks the most specific eligible rule for (tax × line × context).
    /// Specificity = number of pinned dimensions; tiebreak: regime &gt; municipality &gt; company &gt; Id (deterministic).</summary>
    private static WithholdingRule? ResolveRule(
        IReadOnlyCollection<WithholdingRule> rules, WithholdingTaxType tax, ServiceLine line, ResolutionContext ctx) =>
        rules.Where(r =>
                r.Active
                && r.TaxType == tax
                && (r.CompanyId is null || r.CompanyId == ctx.CompanyId)
                && (r.Lc116Subitem is null || r.Lc116Subitem == line.Lc116Subitem)
                && (r.MunicipalityIbge is null || r.MunicipalityIbge == line.MunicipalityIbge)
                && (r.VendorRegime is null || r.VendorRegime == ctx.VendorRegime)
                && IsEligible(r, ctx))
            .OrderByDescending(r => r.DimensionCount)
            .ThenByDescending(r => r.VendorRegime is not null)
            .ThenByDescending(r => r.MunicipalityIbge is not null)
            .ThenByDescending(r => r.CompanyId is not null)
            .ThenBy(r => r.Id)
            .FirstOrDefault();

    // Simples declaration gate: the Simples federal exemption (Simples regime + does-not-retain) only holds
    // when the declaration is on file. Without it, the exemption rule is ineligible → the general rule wins (withholds).
    private static bool IsEligible(WithholdingRule r, ResolutionContext ctx) =>
        !(r.VendorRegime == TaxRegime.SimplesNacional && !r.Retains && !ctx.SimplesDeclarationProvided);

    // Minimum/exemption at the collection-group level (e.g., CSRF over the PIS+COFINS+CSLL sum).
    private static void ApplyGroupMinimum(
        IReadOnlyCollection<WithholdingRule> rules,
        Dictionary<WithholdingTaxType, (decimal Base, decimal Amount, decimal Rate)> perTax)
    {
        var minByGroup = new Dictionary<RetentionGroup, decimal>();
        foreach (var r in rules)
            if (r.MinimumAmount is { } min)
                minByGroup[r.Group] = minByGroup.TryGetValue(r.Group, out var cur) ? Math.Max(cur, min) : min;

        foreach (var group in perTax.Keys.ToList().GroupBy(RetentionGroups.For))
        {
            if (minByGroup.TryGetValue(group.Key, out var min) && min > 0
                && group.Sum(t => perTax[t].Amount) < min)
                foreach (var t in group) perTax.Remove(t);
        }
    }
}
