using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Ulbra.One.Common.Application.Normalization;

/// <summary>
/// Trims and collapses internal whitespace on the <see cref="NormalizeTextAttribute"/>-annotated
/// string properties of a request object. Reflection metadata is cached per type.
/// </summary>
public static class TextNormalizer
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    /// <summary>Normalizes the annotated string properties of <paramref name="target"/> in place.</summary>
    public static void Normalize(object? target)
    {
        if (target is null) return;

        var props = Cache.GetOrAdd(target.GetType(), static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                            && p.CanRead && p.CanWrite
                            && p.IsDefined(typeof(NormalizeTextAttribute), inherit: true))
                .ToArray());

        foreach (var prop in props)
        {
            if (prop.GetValue(target) is string value)
            {
                var normalized = Collapse(value);
                if (!ReferenceEquals(normalized, value))
                    prop.SetValue(target, normalized);
            }
        }
    }

    /// <summary>Trims and collapses runs of whitespace to a single space. Null-safe.</summary>
    public static string? Collapse(string? value)
    {
        if (value is null) return null;

        var span = value.AsSpan().Trim();
        if (span.IsEmpty) return string.Empty;

        // Fast path: no internal whitespace runs and already trimmed -> return original.
        var hasRun = false;
        for (var i = 1; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]) && char.IsWhiteSpace(span[i - 1]))
            {
                hasRun = true;
                break;
            }
        }
        if (!hasRun && span.Length == value.Length) return value;

        var sb = new StringBuilder(span.Length);
        var previousWhitespace = false;
        foreach (var ch in span)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace) sb.Append(' ');
                previousWhitespace = true;
            }
            else
            {
                sb.Append(ch);
                previousWhitespace = false;
            }
        }

        return sb.ToString();
    }
}
