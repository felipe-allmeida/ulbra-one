using System.Text.Json;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Loads an embedded RFB referencial chart resource into rows.</summary>
public static class ReferentialChartData
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ReferentialAccountRow> Load(string resourceName)
    {
        var assembly = typeof(ReferentialChartData).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        return JsonSerializer.Deserialize<List<ReferentialAccountRow>>(stream, Options)
            ?? throw new InvalidOperationException($"Failed to parse resource: {resourceName}");
    }
}
