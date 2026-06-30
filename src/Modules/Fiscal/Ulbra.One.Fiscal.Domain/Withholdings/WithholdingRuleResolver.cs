using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Fiscal.Domain.Withholdings;

/// <summary>Uma linha de serviço da nota: a base do cálculo é a linha, não o documento.
/// <paramref name="Lc116Subitem"/> nulo = não é serviço (produto) → não sofre retenção.</summary>
public sealed record ServiceLine(string? Lc116Subitem, string? MunicipalityIbge, decimal? NotaIssRate, decimal Amount);

/// <summary>Insumos da resolução: empresa, regime do fornecedor, presença da declaração do Simples e as linhas.</summary>
public sealed record ResolutionContext(
    Guid? CompanyId, TaxRegime VendorRegime, bool SimplesDeclarationProvided, IReadOnlyList<ServiceLine> Lines);

/// <summary>Uma retenção calculada (atômica por tributo), pronta para virar PayableWithholding.</summary>
public sealed record WithholdingComputation(WithholdingTaxType TaxType, decimal BaseAmount, decimal Rate, decimal Amount);

/// <summary>
/// Resolve as retenções POR TRIBUTO a partir das regras e do contexto. Puro (sem EF) e determinístico.
/// Federal robusto (chave LC116 + regime); ISS best-effort (precisa de município + alíquota).
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
        // Acumula por tributo atômico, somando as linhas de serviço.
        var perTax = new Dictionary<WithholdingTaxType, (decimal Base, decimal Amount, decimal Rate)>();

        foreach (var line in context.Lines)
        {
            // Só linha de serviço (com subitem LC116) sofre retenção; produto / sem subitem é ignorado.
            if (string.IsNullOrWhiteSpace(line.Lc116Subitem)) continue;

            foreach (var tax in WithholdingTaxes)
            {
                var rule = ResolveRule(rules, tax, line, context);
                if (rule is null || !rule.Retains) continue;

                // Fixed: alíquota da regra. FromNota: alíquota da própria nota (ISS de Simples = faixa do prestador).
                var rate = rule.RateSource == RateSource.FromNota ? line.NotaIssRate : rule.Rate;
                if (rate is null or <= 0) continue; // best-effort: sem alíquota não calcula

                var baseAmount = line.Amount; // BaseRule.Cheia (MVP)
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

    /// <summary>Escolhe a regra mais específica e elegível para (tributo × linha × contexto).
    /// Especificidade = nº de dimensões fixadas; desempate: regime &gt; município &gt; empresa &gt; Id (determinístico).</summary>
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

    // Gate da declaração do Simples: a dispensa federal do Simples (regime Simples + não-retém) só vale
    // com a declaração em arquivo. Sem ela, a regra de dispensa é inelegível → cai na regra geral (retém).
    private static bool IsEligible(WithholdingRule r, ResolutionContext ctx) =>
        !(r.VendorRegime == TaxRegime.SimplesNacional && !r.Retains && !ctx.SimplesDeclarationProvided);

    // Piso/dispensa no nível do grupo de recolhimento (ex.: CSRF sobre a soma PIS+COFINS+CSLL).
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
