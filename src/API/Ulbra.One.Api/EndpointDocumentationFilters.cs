using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.SwaggerGen;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Api;

/// <summary>
/// Emits <c>example</c> for members annotated with <see cref="SwaggerExampleAttribute"/>. Swashbuckle's
/// <c>[SwaggerSchema]</c> carries only a Description, so the example is the half we supply ourselves.
/// <para>
/// Prefixed typed ids need no attribute — they are sampled from the registry by
/// <see cref="PrefixedIdExampleParameterFilter"/>.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class ExampleSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return;
        }

        var declared = context.MemberInfo?.GetCustomAttribute<SwaggerExampleAttribute>()
            ?? context.ParameterInfo?.GetCustomAttribute<SwaggerExampleAttribute>();
        if (declared is not null)
        {
            concrete.Example = ToJsonNode(declared.Value, concrete.Type);
            return;
        }

    }

    /// <summary>
    /// The attribute carries the sample as text (attribute arguments must be constants), but the spec has to
    /// honour the schema's type: an <c>example: "20"</c> on an integer field is a type mismatch that strict
    /// validators and client generators reject. Anything that isn't a number or a boolean stays a string —
    /// dates, competencies and prefixed ids are strings on the wire.
    /// </summary>
    private static JsonNode? ToJsonNode(string raw, JsonSchemaType? schemaType)
    {
        // JsonSchemaType is a flags enum (a nullable field is e.g. Integer | Null), so test with a mask.
        static bool Is(JsonSchemaType? actual, JsonSchemaType expected) => actual is { } a && (a & expected) == expected;

        if (Is(schemaType, JsonSchemaType.Integer) && long.TryParse(raw, CultureInfo.InvariantCulture, out var i))
        {
            return JsonValue.Create(i);
        }

        if (Is(schemaType, JsonSchemaType.Number) && decimal.TryParse(raw, CultureInfo.InvariantCulture, out var d))
        {
            return JsonValue.Create(d);
        }

        if (Is(schemaType, JsonSchemaType.Boolean) && bool.TryParse(raw, out var b))
        {
            return JsonValue.Create(b);
        }

        return JsonValue.Create(raw);
    }
}

/// <summary>
/// Restores the description of DTO properties whose schema is a bare <c>$ref</c> — in practice every enum
/// and every nested object field. Swashbuckle emits <c>{"$ref": "#/components/schemas/ProjectStatus"}</c>,
/// and OpenAPI 3.0 ignores siblings of a <c>$ref</c>, so a <c>[SwaggerSchema]</c> on such a property is
/// dropped without warning. Wrapping the reference in a single-element <c>allOf</c> is the 3.0-legal way to
/// carry a description next to it, and client generators treat the two forms alike.
/// <para>
/// The description must NOT go on the referenced component itself: that schema is shared by every property
/// of that type across the whole API, so one field's wording would leak onto all the others.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class RefPropertyDescriptionSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema { Properties: { Count: > 0 } properties })
        {
            return;
        }

        foreach (var (name, property) in properties.ToList())
        {
            // Only a bare reference loses its siblings; an inline schema keeps the description Swashbuckle set.
            if (property is not OpenApiSchemaReference)
            {
                continue;
            }

            // Schema property names are camelCased by the JSON policy; the CLR member is PascalCase.
            var member = context.Type.GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (member?.GetCustomAttribute<SwaggerSchemaAttribute>()?.Description is not { Length: > 0 } description)
            {
                continue;
            }

            properties[name] = new OpenApiSchema
            {
                AllOf = [property],
                Description = description,
            };
        }
    }
}

/// <summary>
/// Restores the description of query parameters whose schema is a bare <c>$ref</c>. A scalar enum parameter
/// (e.g. <c>?type=Purchase</c>) renders as <c>{"$ref": "#/components/schemas/RequisitionType"}</c>, and
/// OpenAPI 3.0 forbids siblings next to a <c>$ref</c> — so the <c>[SwaggerSchema]</c> description lands on
/// the shared component (or nowhere) instead of on the parameter, and the caller sees an undocumented filter.
/// Lifting it to <c>parameter.description</c>, which sits outside the schema, is the only place it survives.
/// <para>
/// Non-<c>$ref</c> parameters already get their description copied up by Swashbuckle; this filter never
/// overwrites one that is already there.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class ParameterDescriptionFilter : IParameterFilter
{
    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter is not OpenApiParameter concrete || !string.IsNullOrEmpty(concrete.Description))
        {
            return;
        }

        var description = (context.PropertyInfo?.GetCustomAttribute<SwaggerSchemaAttribute>()
            ?? context.ParameterInfo?.GetCustomAttribute<SwaggerSchemaAttribute>())?.Description;
        if (!string.IsNullOrEmpty(description))
        {
            concrete.Description = description;
        }
    }
}

/// <summary>
/// Samples prefixed typed ids (<c>?companyId=company_3fa8…</c>) in the spec. Program.cs registers a schema
/// WITH an example for every <c>[PrefixedId]</c> struct via <c>MapType</c>, and body DTOs pick it up — but
/// the parameter-generation path never carries it over, so every typed-id query parameter in the API reads
/// as a bare <c>{"type": "string"}</c> and gives the caller no hint that a raw GUID will not do.
/// <para>
/// Read from the CLR type rather than an attribute: the registry is source-generated from the ids
/// themselves, so a new <c>[PrefixedId]</c> is documented the moment it is declared.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class PrefixedIdExampleParameterFilter : IParameterFilter
{
    private static readonly IReadOnlyDictionary<Type, string> Examples =
        PrefixedIdRegistry.All.ToDictionary(e => e.Type, e => e.Example);

    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        // A declared [SwaggerExample] already ran in the schema filter; never overwrite it.
        if (parameter.Schema is not OpenApiSchema { Example: null } schema)
        {
            return;
        }

        var declaredType = context.PropertyInfo?.PropertyType ?? context.ParameterInfo?.ParameterType;
        if (declaredType is null)
        {
            return;
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (Examples.TryGetValue(type, out var sample))
        {
            schema.Example = JsonValue.Create(sample);
        }
    }
}

/// <summary>
/// Publishes query parameters in camelCase. Handlers that bind filters through an <c>[AsParameters]</c>
/// request object take the parameter names from the object's PROPERTIES, so the spec would advertise
/// <c>CompanyId</c> while every caller sends <c>companyId</c>. Model binding is case-insensitive, so this
/// is cosmetic at runtime — but the spec is the contract we hand to client generators, and it should show
/// the casing the API actually documents everywhere else.
/// <para>
/// Query only: route parameter names must keep matching their <c>/{id}</c> template placeholders, and
/// header names are case-defined by their own conventions.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class CamelCaseQueryParameterFilter : IParameterFilter
{
    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter is OpenApiParameter { In: ParameterLocation.Query, Name: { Length: > 0 } name } concrete)
        {
            concrete.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
        }
    }
}

/// <summary>
/// Documents <c>ProblemDetails</c> — the one schema every consumer of this API meets, and the only one we
/// cannot annotate at the source because the type belongs to the framework.
/// <para>
/// It carries the two extension members <c>ErrorResults</c> adds to every error (<c>trace_id</c> and
/// <c>error_code</c>). Swashbuckle cannot infer them: they live in the extensions dictionary, so without
/// this filter the spec advertises an error envelope that is missing exactly the two fields a client is
/// supposed to key off.
/// </para>
/// Doc/schema-only — no runtime behavior change.
/// </summary>
public sealed class ProblemDetailsSchemaFilter : ISchemaFilter
{
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["type"] = "URI identifying the problem type. Rarely set by this API — read `error_code` instead.",
        ["title"] = "Short human summary of the problem class (\"Invalid request\", \"Resource not found\"). "
            + "Prose for a person, deliberately NOT the machine value — that is `error_code`.",
        ["status"] = "The HTTP status code, repeated in the body.",
        ["detail"] = "What went wrong in this specific case, in English. Useful in logs; not a message to "
            + "show a user, since it is not translated.",
        ["instance"] = "The request path, when the error was raised outside a handler.",
    };

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ProblemDetails) || schema is not OpenApiSchema concrete)
        {
            return;
        }

        concrete.Description =
            "RFC 9457 error envelope. Every error raised by a handler carries `trace_id` and `error_code` "
            + "— the pair that makes an error diagnosable and translatable. The few errors emitted by "
            + "middleware before a handler runs (a 401 from authentication, a bare 404) keep this shape "
            + "but omit them, so read both defensively.";

        foreach (var (name, description) in Descriptions)
        {
            if (concrete.Properties?.TryGetValue(name, out var property) == true && property is OpenApiSchema p)
            {
                p.Description = description;
            }
        }

        // The two extension members. They travel in ProblemDetails.Extensions, which Swashbuckle does not
        // reflect over, so they have to be declared here or they never reach the spec at all.
        concrete.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        // Nullable on purpose: an error raised by middleware before any handler runs carries neither.
        concrete.Properties[ErrorResults.TraceIdKey] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description = "Trace id of the request, as 32 hex characters. Quote it in a support report and "
                + "the exact request can be found in telemetry. Absent on an error raised before a handler "
                + "runs.",
            Example = "4bf92f3577b34da6a3ce929d0e0e4736",
        };
        concrete.Properties[ErrorResults.ErrorCodeKey] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description = "Stable machine-readable code in `aggregate.reason` form. This is what a client "
                + "keys its own message off — never `title` or `detail`, which are prose and may change. "
                + "Absent on an error raised before a handler runs, so fall back rather than assuming it.",
            Example = "payable.not_found",
        };
    }
}
