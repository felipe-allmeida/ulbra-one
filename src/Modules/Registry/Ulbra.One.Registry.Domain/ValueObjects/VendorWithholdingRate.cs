namespace Ulbra.One.Registry.Domain.ValueObjects;

/// <summary>Per-tax withholding RATE override configured on the vendor cadastro. The system rules still decide
/// WHETHER a tax is withheld (and which taxes); when a tax is withheld and the vendor has an override for it,
/// this rate replaces the rule's. <see cref="TaxType"/> is the withholding tax name (e.g. "Irrf", "Inss") kept
/// as a string so Registry stays independent of Finance's enum (Finance → Registry, never the reverse).
/// <see cref="Rate"/> is a decimal fraction (0.015 = 1,5%).</summary>
public sealed record VendorWithholdingRate(string TaxType, decimal Rate);
