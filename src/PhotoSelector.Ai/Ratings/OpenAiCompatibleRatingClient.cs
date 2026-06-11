using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhotoSelector.Ai.Ratings;

public sealed class OpenAiCompatibleRatingClient : IPhotoRatingClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public OpenAiCompatibleRatingClient()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public OpenAiCompatibleRatingClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private OpenAiCompatibleRatingClient(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException($"Image not found: {request.ImagePath}", request.ImagePath);
        }

        var endpoint = new Uri($"{request.BaseUrl.ToString().TrimEnd('/')}/chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);

        var dataUrl = await RatingRequestPayload.CreateJpegDataUrlAsync(
            request.ImagePath,
            request.Preview ?? PhotoPreviewOptions.Standard,
            cancellationToken);
        var requestJsonRedacted = RatingRequestPayload.CreateRedactedRequestJson(request);
        var bodyJson = RatingRequestPayload.CreateChatCompletionsRequestJson(request, dataUrl);

        httpRequest.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = $"AI rating request failed with HTTP {(int)response.StatusCode}.";
            return new AiRatingClientResult(
                null,
                new AiRatingAudit(request.Prompt, requestJsonRedacted, string.Empty, responseBody, (int)response.StatusCode, error));
        }

        var messageContent = ExtractMessageContent(responseBody);
        var ratingJson = ExtractJsonObject(messageContent);
        var parseResult = AiRatingParser.Parse(ratingJson);
        if (!parseResult.IsSuccess || parseResult.Rating is null)
        {
            return new AiRatingClientResult(
                null,
                new AiRatingAudit(
                    request.Prompt,
                    requestJsonRedacted,
                    messageContent,
                    responseBody,
                    (int)response.StatusCode,
                    parseResult.Error ?? "AI rating response could not be parsed."));
        }

        return new AiRatingClientResult(
            parseResult.Rating,
            new AiRatingAudit(request.Prompt, requestJsonRedacted, messageContent, responseBody, (int)response.StatusCode, null));
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static string ExtractMessageContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var choices = document.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("AI response did not include choices.");
        }

        var content = choices[0].GetProperty("message").GetProperty("content");
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return content.GetRawText();
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf("\n", StringComparison.Ordinal);
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

}
