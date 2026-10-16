using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ulbra.One.Api;

/// <summary>
/// The API uses a global <c>JsonStringEnumConverter</c>, so enums serialize as STRINGS at runtime
/// (e.g. <c>"Payable"</c>, <c>"Pending"</c>, <c>"Company"</c>). Swashbuckle, however, describes enums
/// as INTEGERS in the OpenAPI schema. This filter rewrites every enum schema to a string enum whose
/// members are the enum member NAMES, so the spec matches the wire and openapi-typescript emits string
/// unions instead of numeric ones. Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class StringEnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        var type = context.Type;
        if (!type.IsEnum || schema is not OpenApiSchema concrete)
        {
            return;
        }

        concrete.Type = JsonSchemaType.String;
        concrete.Format = null;
        concrete.Enum = Enum.GetNames(type)
            .Select(name => (JsonNode)JsonValue.Create(name)!)
            .ToList();
    }
}

/// <summary>
/// Our response/request DTOs are positional C# records: every member is always present on the wire
/// (nullable members serialize as JSON <c>null</c>, never absent). Swashbuckle omits the <c>required</c>
/// array, so openapi-typescript would emit every field as optional (<c>?</c>). This filter marks every
/// property of every object schema as <c>required</c>, so generated TS fields are non-optional
/// (genuinely nullable members stay <c>T | null</c>). Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class RequiredPropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is not { Count: > 0 } properties)
        {
            return;
        }

        concrete.Required = new HashSet<string>(properties.Keys);
    }
}
