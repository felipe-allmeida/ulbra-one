namespace Ulbra.One.Finance.Application.Seeding;

/// <summary>A neutral chart row used to seed the payable-classification (Accounting) chart.
/// Decoupled from the Accounting module — the API maps the RFB referencial into these.</summary>
public sealed record ReferentialPayableRow(string Code, string Name, bool IsSynthetic, string? Parent);
