using System.Collections.Concurrent;
using System.Reflection;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Common.Application.Normalization;

/// <summary>
/// Strips formatting from the <see cref="NormalizeCnpjAttribute"/>-annotated string properties of a
/// request object, so handlers persist and compare the bare form. Reflection metadata is cached per type.
/// </summary>
public static class CnpjNormalizer
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    public static void Normalize(object? target)
    {
        if (target is null) return;

        var props = Cache.GetOrAdd(target.GetType(), static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                            && p.CanRead && p.CanWrite
                            && p.IsDefined(typeof(NormalizeCnpjAttribute), inherit: true))
                .ToArray());

        foreach (var prop in props)
        {
            if (prop.GetValue(target) is string value)
            {
                // Letters and digits, uppercased: the CNPJ has been alphanumeric since Jul/2026,
                // so the digits-only normalizer would silently drop half of one.
                var normalized = DocumentValidator.NormalizeCnpj(value);
                if (!string.Equals(normalized, value, StringComparison.Ordinal))
                    prop.SetValue(target, normalized);
            }
        }
    }
}
