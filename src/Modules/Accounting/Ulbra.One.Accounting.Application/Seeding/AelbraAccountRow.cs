namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>One row of the AELBRA "Modelo de Planos" (Senior export, código 0200), as published.</summary>
/// <param name="Code">Hierarchical classification code, e.g. "1.2.05.05.020".</param>
/// <param name="ReducedCode">Short numeric alias ("código reduzido"), unique across the chart.</param>
/// <param name="Name">Account description (pt-BR), kept verbatim as in the sheet.</param>
/// <param name="Tipo">"S" synthetic (aggregator) or "A" analytical (postable).</param>
/// <param name="Parent">Parent classification code, null for the five roots.</param>
/// <param name="Natureza">Chart "Natureza" classification (1-5); independent of the group and of the normal balance.</param>
/// <param name="NormalBalance">Débito/crédito: "D" debit or "C" credit.</param>
public sealed record AelbraAccountRow(
    string Code,
    string ReducedCode,
    string Name,
    string Tipo,
    string? Parent,
    int Natureza,
    string NormalBalance);
