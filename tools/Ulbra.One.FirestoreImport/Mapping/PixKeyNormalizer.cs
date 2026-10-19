namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Faithful port of the app's <c>PixKeyNormalizer</c>
/// (src/Common/Ulbra.One.Common.Domain/Validation/PixKeyNormalizer.cs): e-mail keys are only trimmed,
/// a random (EVP/UUID) key is kept intact, and CPF/CNPJ/phone keys are reduced to digits.</summary>
public static class PixKeyNormalizer
{
    public static string Normalize(string? key)
    {
        var s = (key ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;
        if (s.Contains('@')) return s;                    // e-mail → trim only
        if (Guid.TryParseExact(s, "D", out _)) return s;  // random EVP key → intact
        return DocumentNormalizer.Normalize(s);           // phone/CPF/CNPJ → digits only
    }
}
