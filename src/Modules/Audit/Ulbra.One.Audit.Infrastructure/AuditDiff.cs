using System.Text.Json;

namespace Ulbra.One.Audit.Infrastructure;

public static class AuditDiff
{
    public static (string oldJson, string newJson, string affectedJson, bool hasChange) Compute(
        IReadOnlyDictionary<string, object?> original, IReadOnlyDictionary<string, object?> current,
        IReadOnlySet<string> ignoredColumns)
    {
        var oldVals = new Dictionary<string, object?>();
        var newVals = new Dictionary<string, object?>();
        var affected = new List<string>();
        foreach (var key in current.Keys)
        {
            if (ignoredColumns.Contains(key)) continue;
            original.TryGetValue(key, out var o);
            var c = current[key];
            if (!Equals(o, c)) { affected.Add(key); oldVals[key] = o; newVals[key] = c; }
        }
        return (JsonSerializer.Serialize(oldVals), JsonSerializer.Serialize(newVals),
                JsonSerializer.Serialize(affected), affected.Count > 0);
    }
}
