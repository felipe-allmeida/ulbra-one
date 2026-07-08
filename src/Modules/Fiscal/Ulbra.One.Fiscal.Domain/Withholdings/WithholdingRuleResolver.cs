using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Domain.Withholdings;

/// <summary>A service line of the invoice: the calculation base is the line, not the document. The caller only
/// builds a line for a service (products are filtered upstream by service-nature). A null
/// <paramref name="Lc116Subitem"/> means the specific subitem is unknown — the general federal taxes and INSS
/// (both keyed by regime) still apply; only ISS (by municipality) needs the municipality to refine.</summary>
public sealed record ServiceLine(string? Lc116Subitem, string? MunicipalityIbge, decimal? InvoiceIssRate, decimal Amount);

/// <summary>Resolution inputs: company, vendor regime and the service lines. The Simples federal exemption is
/// keyed off the regime alone — a vendor registered as SimplesNacional is assumed to have the declaration on file.
/// <para><paramref name="Cprb"/> (CPRB destacado na NF, confirmado no lançamento) e <paramref name="SimplesAnexo"/>
/// (anexo do Simples) refinam a retenção de INSS: CPRB → 3,5%; Anexo IV → 11%; demais anexos → isento.</para></summary>
public sealed record ResolutionContext(
    Guid? CompanyId, TaxRegime VendorRegime, IReadOnlyList<ServiceLine> Lines,
    bool? Cprb = null, SimplesAnexo? SimplesAnexo = null);

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
            // refinement, not a gate: federal taxes and INSS key off the vendor regime and match any line
            // (Lc116Subitem=null); only subitem-pinned rules (if seeded) need the line to carry that subitem.
            foreach (var tax in WithholdingTaxes)
            {
                var rule = ResolveRule(rules, tax, line, context);
                if (rule is null || !rule.Retains) continue;

                // Fixed: rate from the rule. FromNota: rate from the invoice itself (Simples ISS = provider's band).
                var rate = rule.RateSource == RateSource.FromNota ? line.InvoiceIssRate : rule.Rate;
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

    /// <summary>Picks the most specific rule for (tax × line × context). Specificity = number of pinned dimensions;
    /// tiebreak: regime &gt; municipality &gt; company &gt; Id (deterministic). A Simples vendor matches both the general
    /// rule (regime = any) and the Simples exemption (regime = Simples, does-not-retain); the exemption wins by
    /// specificity (its regime dimension is pinned), so a Simples service is exempt from the federal withholding.</summary>
    private static WithholdingRule? ResolveRule(
        IReadOnlyCollection<WithholdingRule> rules, WithholdingTaxType tax, ServiceLine line, ResolutionContext ctx) =>
        rules.Where(r =>
                r.Active
                && r.TaxType == tax
                && (r.CompanyId is null || r.CompanyId == ctx.CompanyId)
                && (r.Lc116Subitem is null || r.Lc116Subitem == line.Lc116Subitem)
                && (r.MunicipalityIbge is null || r.MunicipalityIbge == line.MunicipalityIbge)
                && (r.VendorRegime is null || r.VendorRegime == ctx.VendorRegime)
                && (r.Cprb is null || r.Cprb == ctx.Cprb)
                && (r.SimplesAnexo is null || r.SimplesAnexo == ctx.SimplesAnexo))
            .OrderByDescending(r => r.DimensionCount)
            .ThenByDescending(r => r.SimplesAnexo is not null)
            // VendorRegime antes de Cprb: CPRB é regime não-Simples, então num empate a regra do Simples
            // (ex.: isenção do INSS) vence a regra de CPRB — um Simples marcado como CPRB continua pelo anexo.
            .ThenByDescending(r => r.VendorRegime is not null)
            .ThenByDescending(r => r.Cprb is not null)
            .ThenByDescending(r => r.MunicipalityIbge is not null)
            .ThenByDescending(r => r.CompanyId is not null)
            .ThenBy(r => r.Id)
            .FirstOrDefault();

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
