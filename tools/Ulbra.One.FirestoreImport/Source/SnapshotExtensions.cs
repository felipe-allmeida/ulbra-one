using System.Globalization;
using Google.Cloud.Firestore;

namespace Ulbra.One.FirestoreImport.Source;

/// <summary>Null-tolerant field extractors for Firestore snapshots. Missing fields are common in the
/// legacy data (e.g. optional <c>regimeTributario</c>), so every getter degrades to null/empty.</summary>
internal static class SnapshotExtensions
{
    public static string? GetStringOrNull(this DocumentSnapshot snap, string field)
        => snap.TryGetValue<string>(field, out var v) ? v : null;

    public static bool? GetBoolOrNull(this DocumentSnapshot snap, string field)
        => snap.TryGetValue<bool>(field, out var v) ? v : null;

    /// <summary>Reads a numeric field as decimal regardless of whether Firestore stored it as an
    /// integer or a double (both occur for <c>limitValue</c>).</summary>
    public static decimal? GetDecimalOrNull(this DocumentSnapshot snap, string field)
    {
        if (!snap.TryGetValue<object>(field, out var o) || o is null) return null;
        return o switch
        {
            long l => l,
            double d => (decimal)d,
            _ => null,
        };
    }

    public static long? GetLongOrNull(this DocumentSnapshot snap, string field)
    {
        if (!snap.TryGetValue<object>(field, out var o) || o is null) return null;
        return o switch { long l => l, double d => (long)d, _ => null };
    }

    /// <summary>Reads a string array; returns an empty list when the field is absent.</summary>
    public static IReadOnlyList<string> GetStringList(this DocumentSnapshot snap, string field)
    {
        if (!snap.TryGetValue<List<object>>(field, out var list) || list is null) return [];
        return [.. list.Where(x => x is not null).Select(x => x.ToString()!).Where(s => s.Length > 0)];
    }

    /// <summary>Reads a map field (e.g. `competencyValues`: competency → value) as decimals, tolerating
    /// integer or double values; returns an empty map when the field is absent. Non-numeric entries dropped.</summary>
    public static IReadOnlyDictionary<string, decimal> GetDecimalMap(this DocumentSnapshot snap, string field)
    {
        if (!snap.TryGetValue<Dictionary<string, object>>(field, out var map) || map is null)
            return new Dictionary<string, decimal>();

        var result = new Dictionary<string, decimal>(map.Count);
        foreach (var (key, value) in map)
        {
            decimal? d = value switch { long l => l, double n => (decimal)n, decimal m => m, _ => null };
            if (d is { } v) result[key] = v;
        }
        return result;
    }

    /// <summary>Reads a timestamp field, tolerating both a Firestore <see cref="Timestamp"/> and an ISO-8601
    /// string (the legacy stores audit dates either way); returns null when absent/unparseable.</summary>
    public static DateTimeOffset? GetTimestampOrNull(this DocumentSnapshot snap, string field)
    {
        if (!snap.TryGetValue<object>(field, out var o) || o is null) return null;
        return o switch
        {
            Timestamp ts => ts.ToDateTimeOffset(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : null,
            _ => null,
        };
    }

    /// <summary>The document's creation instant, faithful to the legacy: the app-set <c>createdAt</c> field when
    /// present, else the Firestore document's own <see cref="DocumentSnapshot.CreateTime"/> metadata (always
    /// present). Guarantees the migrated <c>created_at</c> never falls back to "today".</summary>
    public static DateTimeOffset? GetCreatedAt(this DocumentSnapshot snap)
        => GetTimestampOrNull(snap, "createdAt") ?? snap.CreateTime?.ToDateTimeOffset();

    /// <summary>The document's last-update instant: the app-set <c>updatedAt</c> field, else the Firestore
    /// <see cref="DocumentSnapshot.UpdateTime"/> metadata.</summary>
    public static DateTimeOffset? GetUpdatedAt(this DocumentSnapshot snap)
        => GetTimestampOrNull(snap, "updatedAt") ?? snap.UpdateTime?.ToDateTimeOffset();
}
