using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoSelector.Ai.Ratings;

public sealed class ChatCompletionMessageContentJsonConverter : JsonConverter<ChatCompletionMessageContentJson>
{
    public override ChatCompletionMessageContentJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ChatCompletionMessageContentJson(reader.GetString() ?? string.Empty);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return new ChatCompletionMessageContentJson(document.RootElement.GetRawText());
    }

    public override void Write(Utf8JsonWriter writer, ChatCompletionMessageContentJson value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Text);
    }
}
