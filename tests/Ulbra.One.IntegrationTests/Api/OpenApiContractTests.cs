using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Xunit;

namespace Ulbra.One.IntegrationTests.Api;

/// <summary>
/// Guards the API documentation against silent decay. The documentation effort left every operation with a
/// summary and a description and every input field described, but nothing stopped the next endpoint from
/// landing bare — which is exactly what happened once before these tests existed.
/// <para>
/// The spec is read from the running test host (the factory runs in Development, where Swagger is mapped),
/// so these assert the real generated document, not a snapshot of it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class OpenApiContractTests(PostgresFixture fixture)
{
    private const string SpecPath = "/swagger/v1/swagger.json";

    /// <summary>The committed spec the frontend generates its types from (`pnpm --dir web gen:api`).</summary>
    private static string CommittedSpecPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ulbra.One.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate the repository root from the test output directory.");
        return Path.Combine(dir!.FullName, "web", "openapi.json");
    }

    private async Task<JsonNode> LiveSpecAsync()
    {
        using var client = fixture.Api.CreateClient();
        var json = await client.GetStringAsync(SpecPath);
        return JsonNode.Parse(json)!;
    }

    /// <summary>Every operation carries the tag-bearing shape the modules use; untagged infra routes are skipped.</summary>
    private static IEnumerable<(string Route, string Verb, JsonObject Operation)> Operations(JsonNode spec)
    {
        foreach (var (route, item) in spec["paths"]!.AsObject())
        {
            foreach (var (verb, operation) in item!.AsObject())
            {
                if (operation is JsonObject op && op.ContainsKey("tags"))
                {
                    yield return (route, verb, op);
                }
            }
        }
    }

    [Fact]
    public async Task Every_operation_has_a_summary_and_a_description()
    {
        var spec = await LiveSpecAsync();

        var bare = Operations(spec)
            .Where(o => string.IsNullOrWhiteSpace(o.Operation["summary"]?.GetValue<string>())
                || string.IsNullOrWhiteSpace(o.Operation["description"]?.GetValue<string>()))
            .Select(o => $"{o.Verb.ToUpperInvariant()} {o.Route}")
            .ToList();

        Assert.True(bare.Count == 0,
            "Every endpoint must call .WithSummary() and .WithDescription() — see docs/api-design.md "
            + $"\"Endpoint documentation\". Missing on:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", bare)}");
    }

    [Fact]
    public async Task Every_query_parameter_is_described()
    {
        var spec = await LiveSpecAsync();

        var bare = (from o in Operations(spec)
                    from p in o.Operation["parameters"]?.AsArray() ?? []
                    where p!["in"]?.GetValue<string>() == "query"
                       && string.IsNullOrWhiteSpace(p["description"]?.GetValue<string>())
                    select $"{o.Verb.ToUpperInvariant()} {o.Route} → {p["name"]}")
            .ToList();

        Assert.True(bare.Count == 0,
            "Every query parameter needs a [SwaggerSchema] description. A scalar enum renders as a bare $ref, "
            + "so its description is lifted to the parameter by ParameterDescriptionFilter — if one shows up "
            + $"here, check that filter first. Missing on:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", bare)}");
    }

    /// <summary>
    /// Every property in the spec is described — sent OR received.
    /// <para>
    /// This deliberately walks all schemas instead of filtering by name. The previous version only checked
    /// schemas whose name ended in "Request", which left any type reached only through a <c>$ref</c>
    /// unverified on both sides — that is how a value object went undocumented in the request payload
    /// without anything noticing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_schema_property_is_described()
    {
        var spec = await LiveSpecAsync();
        var schemas = spec["components"]!["schemas"]!.AsObject();

        var bare = (from kv in schemas
                    where !Exempt.Contains(kv.Key)
                    from prop in kv.Value?["properties"]?.AsObject() ?? []
                    where !IsDocumented(prop.Value)
                    select $"{kv.Key}.{prop.Key}")
            .ToList();

        Assert.True(bare.Count == 0,
            "Every property in the spec — sent or received — needs a description. On a positional record "
            + "the attribute must be written as [property: SwaggerSchema(...)]: without the target it lands "
            + "on the constructor parameter, compiles clean, and never reaches the schema. Missing on:"
            + $"{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", bare)}");
    }

    /// <summary>
    /// Schemas allowed to stay undocumented, each for a reason that cannot be fixed by adding an attribute.
    /// Keep this list short and justified — it is an escape hatch, not a backlog.
    /// </summary>
    private static readonly HashSet<string> Exempt =
    [
        // A value object in Registry.Domain that goes straight onto the wire. Domain may not reference
        // Swashbuckle (see the module's dependency rule), so it cannot carry the attribute. The fix is to
        // wrap it in an Application-level DTO, the way Address and BankInfo already are.
        "VendorWithholdingRate",
    ];

    /// <summary>
    /// A property counts as documented when it carries a description, wraps a <c>$ref</c> in an
    /// <c>allOf</c> (where the description lives on the wrapper), or is a prefixed typed id — whose
    /// canonical example already names the entity, the same rule route parameters follow.
    /// </summary>
    private static bool IsDocumented(JsonNode? property)
    {
        if (!string.IsNullOrWhiteSpace(property?["description"]?.GetValue<string>())) return true;
        if (property?["allOf"] is not null) return true;

        var example = property?["example"]?.GetValue<string>();
        return example is not null && PrefixedIdExample.IsMatch(example);
    }

    private static readonly Regex PrefixedIdExample = new("^[a-z][a-z0-9_]*_[0-9a-f]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// The frontend generates its TypeScript types from the committed <c>web/openapi.json</c>, and that file has
    /// no other guard: it went a whole documentation series out of date before anyone noticed. Comparing the
    /// live document against it turns "someone forgot to regenerate" into a failing test.
    /// </summary>
    [Fact]
    public async Task Committed_openapi_json_matches_the_generated_spec()
    {
        var live = await LiveSpecAsync();
        var committedPath = CommittedSpecPath();
        Assert.True(File.Exists(committedPath), $"Expected the committed spec at {committedPath}.");

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(committedPath))!;

        // Compare the canonical serialization: property order is stable for a given build, and normalizing
        // through the same writer keeps trailing-whitespace differences out of the comparison.
        var options = new JsonSerializerOptions { WriteIndented = false };
        Assert.True(live.ToJsonString(options) == committed.ToJsonString(options),
            "web/openapi.json is out of date with the API. Regenerate it and the TypeScript types:"
            + $"{Environment.NewLine}  1. run the API and save {SpecPath} over web/openapi.json"
            + $"{Environment.NewLine}  2. pnpm --dir web gen:api");
    }
}
