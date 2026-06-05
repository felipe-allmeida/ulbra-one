namespace Ulbra.One.Common.Infrastructure.Normalization;

/// <summary>
/// Marks a <see cref="string"/> request property to be trimmed and have its internal
/// whitespace collapsed to single spaces before the handler runs. Null-safe.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NormalizeTextAttribute : Attribute;
