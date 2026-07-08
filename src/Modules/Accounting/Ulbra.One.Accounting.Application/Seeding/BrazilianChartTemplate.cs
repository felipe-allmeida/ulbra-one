using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>A generic Brazilian accounting chart of accounts (plano de contas) template:
/// synthetic roots for the five account groups plus a few analytical children each.
/// Generic/multi-tenant — no business- or tenant-specific accounts. Idempotent application is the
/// caller's responsibility (skip when the company already has accounts).</summary>
public static class BrazilianChartTemplate
{
    /// <summary>One node in the template: a synthetic root with its analytical children.</summary>
    public sealed record TemplateRoot(string Code, string Name, AccountGroup Group, IReadOnlyList<TemplateLeaf> Children);

    public sealed record TemplateLeaf(string Code, string Name);

    /// <summary>The standard template, ordered by code. Five synthetic roots (1..5) + analytical leaves.</summary>
    public static readonly IReadOnlyList<TemplateRoot> Roots =
    [
        new("1", "Ativo", AccountGroup.Asset,
        [
            new("1.01.01", "Caixa"),
            new("1.01.02", "Bancos conta movimento"),
            new("1.01.03", "Clientes a receber"),
            new("1.02.01", "Imobilizado"),
        ]),
        new("2", "Passivo", AccountGroup.Liability,
        [
            new("2.01.01", "Fornecedores a pagar"),
            new("2.01.02", "Salários a pagar"),
            new("2.01.03", "Impostos a recolher"),
            new("2.02.01", "Empréstimos a pagar"),
        ]),
        new("3", "Patrimônio Líquido", AccountGroup.Equity,
        [
            new("3.01.01", "Capital social"),
            new("3.02.01", "Lucros ou prejuízos acumulados"),
        ]),
        new("4", "Receitas", AccountGroup.Revenue,
        [
            new("4.01.01", "Receita de serviços"),
            new("4.01.02", "Receita de vendas"),
            new("4.02.01", "Receitas financeiras"),
        ]),
        new("5", "Despesas", AccountGroup.Expense,
        [
            new("5.01.01", "Despesas com pessoal"),
            new("5.01.02", "Energia elétrica"),
            new("5.01.03", "Aluguéis"),
            new("5.01.04", "Serviços de terceiros"),
            new("5.02.01", "Despesas financeiras"),
        ]),
    ];

    /// <summary>Materializes the template into ledger accounts (global). Returns roots followed by
    /// their children (parent ids wired). The caller persists and audits them.</summary>
    public static IReadOnlyList<LedgerAccount> Build()
    {
        var accounts = new List<LedgerAccount>();
        foreach (var root in Roots)
        {
            var rootAccount = new LedgerAccount(root.Code, root.Name, root.Group, LedgerAccountKind.Synthetic);
            accounts.Add(rootAccount);
            foreach (var leaf in root.Children)
            {
                var child = new LedgerAccount(leaf.Code, leaf.Name, root.Group, LedgerAccountKind.Analytical);
                child.Reparent(rootAccount);
                accounts.Add(child);
            }
        }
        return accounts;
    }
}
