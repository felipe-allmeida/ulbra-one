namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Small string helpers. Target columns have fixed max lengths (e.g. address_state varchar(2));
/// a value longer than its column would make Postgres reject the whole insert, so source strings are
/// truncated to the column limit before writing.</summary>
public static class Text
{
    public static string Trunc(string? s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    public static string TruncUpper(string? s, int max)
        => Trunc((s ?? string.Empty).ToUpperInvariant(), max);
}
