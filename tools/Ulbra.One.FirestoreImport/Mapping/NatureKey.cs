using System.Globalization;
using System.Text;

namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Canonical match key for a natureza / cost-center name: uppercased, diacritics stripped,
/// internal whitespace collapsed. The legacy sends names in upper case with accents
/// ("TECNOLOGIA E INOVAÇÃO") while the One stores title case ("Tecnologia e Inovação"), so a raw
/// equality check fails — both sides must be reduced through this key before matching.</summary>
public static class NatureKey
{
    public static string Of(string? name)
    {
        var s = (name ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;

        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToUpperInvariant(ch));
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
