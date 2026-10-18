namespace Ulbra.One.Common.Application.OpenApi;

/// <summary>
/// A sample value for the annotated member, emitted as <c>example</c> in the OpenAPI schema.
/// <para>
/// Swashbuckle's own <c>[SwaggerSchema]</c> carries a Description but has NO Example property, so this
/// attribute is the other half of the endpoint-documentation pair: <c>[SwaggerSchema]</c> says what the
/// value means, <c>[SwaggerExample]</c> shows one. Applied by <c>ExampleSchemaFilter</c> in the API project.
/// </para>
/// <para>
/// Prefixed typed ids (<see cref="Ids.IPrefixedId"/>) do NOT need this — the filter falls back to the
/// canonical sample in <c>PrefixedIdRegistry</c>. Booleans and enums rarely need it either (the schema
/// already enumerates the legal values). Reach for it on strings, dates and numbers, where the format
/// is the thing a caller gets wrong.
/// </para>
/// </summary>
/// <param name="value">The sample, written exactly as it travels on the wire (e.g. <c>"2026-08"</c>).</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class SwaggerExampleAttribute(string value) : Attribute
{
    /// <summary>The sample value, as it appears on the wire.</summary>
    public string Value { get; } = value;
}
