using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankStatementImport entities (wire form <c>statement_import_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<StatementImportId>))]
public readonly record struct StatementImportId(Guid Value) : IPrefixedId, IParsable<StatementImportId>
{
    public static string Prefix => "statement_import_";

    public static StatementImportId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static StatementImportId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid StatementImportId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out StatementImportId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new StatementImportId(g);
        return true;
    }
}
