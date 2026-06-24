using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace PhotoSelector.Ai.Ratings;

public sealed class OpenAiSdkRatingClient : IPhotoRatingClient, IDisposable
{
    public async Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException($"Image not found: {request.ImagePath}", request.ImagePath);
        }

        var dataUrl = await RatingRequestPayload.CreateJpegDataUrlAsync(
            request.ImagePath,
            request.Preview ?? PhotoPreviewOptions.Standard,
            cancellationToken);
        var requestJsonRedacted = RatingRequestPayload.CreateRedactedRequestJson(request);
        var bodyJson = RatingRequestPayload.CreateChatCompletionsRequestJson(request, dataUrl);

        var client = new ChatClient(
            model: request.Model,
            credential: new ApiKeyCredential(request.ApiKey),
            options: new OpenAIClientOptions
            {
                Endpoint = request.BaseUrl,
            });

        using var content = BinaryContent.Create(BinaryData.FromString(bodyJson));
        ClientResult result;
        try
        {
            result = await client.CompleteChatAsync(content, options: null);
        }
        catch (ClientResultException ex)
        {
            var raw = ex.GetRawResponse();
            return new AiRatingClientResult(
                null,
                new AiRatingAudit(
                    request.Prompt,
                    requestJsonRedacted,
                    string.Empty,
                    raw?.Content?.ToString() ?? string.Empty,
                    raw?.Status,
                    $"AI rating request failed with HTTP {raw?.Status}."));
        }

        var rawResponse = result.GetRawResponse();
        var responseBody = rawResponse.Content?.ToString() ?? string.Empty;
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
                    rawResponse.Status,
                    parseResult.Error ?? "AI rating response could not be parsed."));
        }

        return new AiRatingClientResult(
            parseResult.Rating,
            new AiRatingAudit(request.Prompt, requestJsonRedacted, messageContent, responseBody, rawResponse.Status, null));
    }

    public void Dispose()
    {
    }

}
