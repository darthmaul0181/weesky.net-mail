using System.Text.Json;
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// Backward compatibility for POST/PUT /api/Contacts: the live frontend still posts addresses as
/// a bare string array, and no screen changes in 4a. A JSON string element becomes an address with
/// no position and no type; a JSON object element deserialises normally. Any other token is
/// refused rather than silently ignored.
/// </summary>
internal sealed class ContactLineJsonConverter : JsonConverter<List<ContactEmailPayload>>
{
    public override List<ContactEmailPayload>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of addresses");

        var result = new List<ContactEmailPayload>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            result.Add(reader.TokenType switch
            {
                JsonTokenType.String => new ContactEmailPayload { Address = reader.GetString() },
                JsonTokenType.StartObject => JsonSerializer.Deserialize<ContactEmailPayload>(ref reader, options)!,
                _ => throw new JsonException($"Unexpected {reader.TokenType} in an address list"),
            });
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, List<ContactEmailPayload>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value) JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}
