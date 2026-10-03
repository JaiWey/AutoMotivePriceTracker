using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoMotivePriceTracker.Web.Json;

/// <summary>
/// All stored times are UTC, so always emit a "Z" suffix (even for a DateTime of Unspecified kind)
/// so browsers convert them to local time correctly.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"));
}
