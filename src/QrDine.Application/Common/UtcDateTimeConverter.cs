using System.Text.Json;
using System.Text.Json.Serialization;

namespace QrDine.Application.Common;

/// <summary>
/// Every time we keep is UTC, but the database hands it back without saying so. A browser then reads it as
/// local time, five hours out in Pakistan. This says so on the way out, for every time the API sends.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime());
}
