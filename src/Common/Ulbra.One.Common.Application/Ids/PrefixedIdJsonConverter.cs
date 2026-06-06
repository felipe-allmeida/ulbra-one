using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>
/// System.Text.Json converter for any <see cref="IPrefixedId"/> struct. Reads/writes the prefixed
/// string form (e.g. <c>company_3fa85f64...</c>); referenced via <c>[JsonConverter(...)]</c> on each id type.
/// </summary>
public sealed class PrefixedIdJsonConverter<T> : JsonConverter<T>
    where T : struct, IPrefixedId, IParsable<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        T.Parse(reader.GetString()!, null);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
