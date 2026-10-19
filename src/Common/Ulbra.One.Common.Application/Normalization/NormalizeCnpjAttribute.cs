namespace Ulbra.One.Common.Application.Normalization;

/// <summary>
/// Marks a <see cref="string"/> request property holding a CNPJ, to be stripped of formatting and
/// uppercased before the handler runs. Null-safe. Use instead of <see cref="NormalizeTextAttribute"/>,
/// not alongside it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NormalizeCnpjAttribute : Attribute;
