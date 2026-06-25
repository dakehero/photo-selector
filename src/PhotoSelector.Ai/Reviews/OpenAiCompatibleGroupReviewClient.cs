using System.Net.Http.Headers;
using System.Text;
using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Ai.Reviews;

public sealed class OpenAiCompatibleGroupReviewClient : IGroupReviewClient
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public OpenAiCompatibleGroupReviewClient()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public OpenAiCompatibleGroupReviewClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private OpenAiCompatibleGroupReviewClient(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<GroupReviewClientResult> ReviewGroupAsync(
        GroupReviewRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri($"{request.BaseUrl.ToString().TrimEnd('/')}/chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);

        var dataUrls = await GroupReviewRequestPayload.CreateJpegDataUrlsAsync(request, cancellationToken);
        var requestJsonRedacted = GroupReviewRequestPayload.CreateRedactedRequestJson(request);
        var bodyJson = GroupReviewRequestPayload.CreateChatCompletionsRequestJson(request, dataUrls);

        httpRequest.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = $"AI group review request failed with HTTP {(int)response.StatusCode}.";
            return new GroupReviewClientResult(
                null,
                new AiRatingAudit(request.Prompt, requestJsonRedacted, string.Empty, responseBody, (int)response.StatusCode, error));
        }

        var messageContent = ChatCompletionResponseParser.ExtractMessageContent(responseBody);
        var reviewJson = ChatCompletionResponseParser.ExtractJsonObject(messageContent);
        var parseResult = GroupReviewParser.Parse(reviewJson);
        if (!parseResult.IsSuccess || parseResult.Review is null)
        {
            return new GroupReviewClientResult(
                null,
                new AiRatingAudit(
                    request.Prompt,
                    requestJsonRedacted,
                    messageContent,
                    responseBody,
                    (int)response.StatusCode,
                    parseResult.Error ?? "AI group review response could not be parsed."));
        }

        return new GroupReviewClientResult(
            parseResult.Review,
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
