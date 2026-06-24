namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Vendor tax regime — drives withholding (retenção) downstream. Populated manually now;
/// the CNPJ lookup (Item 4) auto-fills it later.</summary>
public enum TaxRegime { Unknown, Mei, SimplesNacional, LucroPresumido, LucroReal }
