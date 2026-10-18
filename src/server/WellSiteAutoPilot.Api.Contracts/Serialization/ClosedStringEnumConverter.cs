using System.Text.Json;
using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Serialization;

/// <summary>
/// Wire format for an API-owned closed value set: the exact, case-sensitive
/// member name as a JSON string. Numbers, numeric strings, other casings,
/// whitespace, null and undefined members are rejected on read and write, so
/// a contract enum fails closed exactly like the explicit string parser it
/// replaces. Swashbuckle serializes a member through this converter, so the
/// OpenAPI document publishes the same names as a string enum.
/// </summary>
public class ClosedStringEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (raw is not null && Array.IndexOf(Enum.GetNames<TEnum>(), raw) >= 0)
                return Enum.Parse<TEnum>(raw);
        }

        throw new JsonException($"A supported {typeof(TEnum).Name} name is required.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!Enum.IsDefined(value))
            throw new JsonException($"The {typeof(TEnum).Name} value is not defined.");
        writer.WriteStringValue(value.ToString());
    }
}
