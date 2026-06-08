using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ExpenseCategory entities (wire form <c>expense_category_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<ExpenseCategoryId>))]
public readonly record struct ExpenseCategoryId(Guid Value) : IPrefixedId, IParsable<ExpenseCategoryId>
{
    public static string Prefix => "expense_category_";

    public static ExpenseCategoryId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static ExpenseCategoryId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid ExpenseCategoryId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out ExpenseCategoryId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new ExpenseCategoryId(g);
        return true;
    }
}
