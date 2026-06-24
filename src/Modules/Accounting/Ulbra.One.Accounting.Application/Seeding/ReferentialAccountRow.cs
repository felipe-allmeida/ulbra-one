namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>One row of the RFB Plano de Contas Referencial, as published.</summary>
/// <param name="Code">Hierarchical code, e.g. "4.02.02.01.05".</param>
/// <param name="Name">Official account description (pt-BR).</param>
/// <param name="Tipo">"S" synthetic (aggregator) or "A" analytical (postable).</param>
/// <param name="Parent">Parent code ("conta superior"), null for roots.</param>
/// <param name="Nivel">Depth level (1-7).</param>
/// <param name="Natureza">"1" Asset, "2" Liability, "3" Equity, "4" Result.</param>
public sealed record ReferentialAccountRow(
    string Code,
    string Name,
    string Tipo,
    string? Parent,
    int Nivel,
    string Natureza);
