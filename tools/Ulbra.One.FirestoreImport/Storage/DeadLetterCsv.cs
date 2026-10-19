using System.Text;

namespace Ulbra.One.FirestoreImport.Storage;

/// <summary>Renders dead-letter rows as CSV for diagnosis (RFC 4180 escaping: fields with comma/quote/newline
/// are quoted and internal quotes doubled). The caller prepends a UTF-8 BOM when the target is Excel.</summary>
public static class DeadLetterCsv
{
    public static string Build(IReadOnlyList<DeadLetterRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("entity_type,legacy_id,at,error,payload\r\n");
        foreach (var r in rows)
            sb.Append(Field(r.EntityType)).Append(',')
              .Append(Field(r.LegacyId)).Append(',')
              .Append(Field(r.At.ToString("O"))).Append(',')
              .Append(Field(r.Error)).Append(',')
              .Append(Field(r.Payload ?? string.Empty)).Append("\r\n");
        return sb.ToString();
    }

    private static string Field(string value)
        => value.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
