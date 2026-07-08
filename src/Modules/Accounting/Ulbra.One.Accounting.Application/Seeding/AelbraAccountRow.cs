namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>One row of the AELBRA "Modelo de Planos" (Senior export, código 0200), as published.</summary>
/// <param name="Code">Hierarchical classification code, e.g. "1.2.05.05.020".</param>
/// <param name="Name">Account description (pt-BR), kept verbatim as in the sheet.</param>
/// <param name="Tipo">"S" synthetic (aggregator) or "A" analytical (postable).</param>
/// <param name="Parent">Parent classification code, null for the five roots.</param>
/// <param name="Nivel">Depth level (1-5), equal to the code's dot-depth.</param>
/// <param name="Natureza">Normal balance: "D" debit or "C" credit.</param>
/// <param name="Reduzida">Short numeric alias ("código reduzido"), unique across the chart.</param>
public sealed record AelbraAccountRow(
    string Code,
    string Name,
    string Tipo,
    string? Parent,
    int Nivel,
    string Natureza,
    string Reduzida);
