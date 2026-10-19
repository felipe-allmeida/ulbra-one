namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Faithful port of the app's <c>DocumentValidator</c>
/// (src/Common/Ulbra.One.Common.Domain/Validation/DocumentValidator.cs) so the importer writes the
/// <c>vendors.document</c> column exactly as the domain would. Kept as a local copy because the tool
/// is decoupled from the monolith.</summary>
public static class DocumentNormalizer
{
    /// <summary>Digits only — for CPF and legacy numeric documents.</summary>
    public static string Normalize(string? document) =>
        new([.. (document ?? string.Empty).Where(char.IsDigit)]);

    /// <summary>CNPJ chars only (letters + digits), uppercased — the alphanumeric CNPJ (RFB, Jul/2026).</summary>
    public static string NormalizeCnpj(string? document) =>
        new([.. (document ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
