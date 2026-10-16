namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>Remittance knobs bound from the <c>Cnab</c> config section.</summary>
public sealed class RemittanceOptions
{
    /// <summary>
    /// Emit production NSAs (file sequence ≥ 11). Santander treats NSA 1-10 as a TEST file (manual note
    /// G010): it passes the crítica but is never executed. Null falls back to <c>IsProduction()</c>.
    /// </summary>
    public bool? ProductionRemittance { get; init; }
}
