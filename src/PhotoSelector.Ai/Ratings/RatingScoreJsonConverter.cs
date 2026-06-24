using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoSelector.Ai.Ratings;

public sealed class RatingScoreJsonConverter : JsonConverter<RatingScore>
{
    public override RatingScore Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rawText = reader.TokenType switch
        {
            JsonTokenType.Number => GetNumberText(reader),
            JsonTokenType.String => reader.GetString(),
            _ => throw new JsonException("Rating score must be a number or numeric string."),
        };

        if (string.IsNullOrWhiteSpace(rawText) ||
            !double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException("Rating score must be a number or numeric string.");
        }

        return new RatingScore(value, rawText);
    }

    public override void Write(Utf8JsonWriter writer, RatingScore value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }

    private static string GetNumberText(Utf8JsonReader reader)
    {
        var bytes = reader.HasValueSequence
            ? reader.ValueSequence.ToArray()
            : reader.ValueSpan.ToArray();
        return Encoding.UTF8.GetString(bytes);
    }
}
