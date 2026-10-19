namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>How a legacy `fornecedores` document maps onto the enum/int columns of `registry.vendors`.
/// Column storage is mixed: <c>person_type</c> is an integer (enum ordinal), while <c>tax_regime</c>
/// and <c>payment_schedule_mode</c> store the .NET enum NAME as text.</summary>
public static class VendorMapping
{
    // PersonType enum ordinals (Registry.Domain/Entities/PersonType.cs): Individual = 0 (CPF), Company = 1 (CNPJ).
    public const int PersonTypeIndividual = 0;
    public const int PersonTypeCompany = 1;

    // PaymentScheduleMode enum name (ValueObjects/PaymentSchedule.cs) — no legacy source, so the default.
    public const string PaymentScheduleModeDefault = "OnDueDate";

    /// <summary>Resolves person type (by CPF vs CNPJ) and the normalized document string, mirroring
    /// <c>VendorEntity.Apply</c> (Company → NormalizeCnpj, Individual → Normalize).</summary>
    public static VendorDocument ResolveDocument(string? rawCnpjCpf)
    {
        var isIndividual = DocumentNormalizer.Normalize(rawCnpjCpf).Length == 11;
        return isIndividual
            ? new VendorDocument(PersonTypeIndividual, DocumentNormalizer.Normalize(rawCnpjCpf))
            : new VendorDocument(PersonTypeCompany, DocumentNormalizer.NormalizeCnpj(rawCnpjCpf));
    }

    /// <summary>Maps the legacy free-text tax regime to the <c>TaxRegime</c> enum NAME. Unknown by default.
    /// Matches by keyword to tolerate formatting differences in the source.</summary>
    public static string ResolveTaxRegime(string? regimeTributario)
    {
        var s = (regimeTributario ?? string.Empty).Trim().ToUpperInvariant();
        if (s.Length == 0) return "Unknown";
        if (s.Contains("MEI")) return "Mei";
        if (s.Contains("SIMPLES")) return "SimplesNacional";
        if (s.Contains("PRESUMIDO")) return "LucroPresumido";
        if (s.Contains("REAL")) return "LucroReal";
        return "Unknown";
    }
}

/// <summary>Person type (enum ordinal, stored as int) plus the normalized document string.</summary>
public readonly record struct VendorDocument(int PersonType, string Document);
