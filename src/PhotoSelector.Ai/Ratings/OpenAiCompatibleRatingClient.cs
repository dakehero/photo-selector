using System.Net.Http.Headers;
using System.Text;

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

        var messageContent = ChatCompletionResponseParser.ExtractMessageContent(responseBody);
        var ratingJson = ChatCompletionResponseParser.ExtractJsonObject(messageContent);
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

}
