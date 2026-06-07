using System.Net;
using System.Text.Json;
using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Tests;

public sealed class OpenAiCompatibleRatingClientTests
{
    [Fact]
    public async Task RatePhotoAsync_posts_openai_compatible_vision_request_and_parses_rating()
    {
        using var tempDirectory = new TempDirectory();
        var imagePath = Path.Combine(tempDirectory.Path, "IMG_0001.JPG");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAH/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAEFAqf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/ASP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/ASP/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAY/Al//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/IV//2gAMAwEAAgADAAAAEP/EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8QH//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8QH//EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEAAT8QH//Z"));

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new DelegateHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"photo_type\":\"portrait\",\"score\":8.4,\"category\":\"keep\",\"criteria\":[{\"name\":\"impact\",\"score\":8.2,\"comment\":\"Expressive subject.\"}],\"reason\":\"Sharp subject and strong composition.\"}"
                          }
                        }
                      ]
                    }
                    """),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleRatingClient(httpClient);

        var result = await client.RatePhotoAsync(
            new PhotoRatingRequest(
                new Uri("https://openrouter.ai/api/v1"),
                "sk-test",
                "qwen/qwen3.7-max",
                DefaultPhotoRatingPrompt.Text,
                imagePath),
            CancellationToken.None);

        Assert.NotNull(result.Rating);
        Assert.Equal("portrait", result.Rating.PhotoType);
        Assert.Equal(8.4, result.Rating.Score);
        Assert.Equal("keep", result.Rating.Category);
        Assert.Equal("Sharp subject and strong composition.", result.Rating.Reason);
        Assert.Single(result.Rating.Criteria);
        Assert.Equal(8.2, result.Rating.Criteria[0].Score);
        Assert.Equal(200, result.Audit.HttpStatus);
        Assert.Contains("\"choices\"", result.Audit.RawResponseJson);
        Assert.Contains("\"photo_type\":\"portrait\"", result.Audit.RawMessageContent);
        Assert.Contains("\"model\":\"qwen/qwen3.7-max\"", result.Audit.RequestJsonRedacted);
        Assert.Contains("\"image_url\":\"[redacted-data-url]\"", result.Audit.RequestJsonRedacted);
        Assert.DoesNotContain("sk-test", result.Audit.RequestJsonRedacted);
        Assert.DoesNotContain("data:image", result.Audit.RequestJsonRedacted);
        Assert.Null(result.Audit.Error);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", capturedRequest.Headers.Authorization.Parameter);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"model\":\"qwen/qwen3.7-max\"", capturedBody);
        Assert.Contains("Classify the photographic genre first", capturedBody);
        Assert.Contains("data:image/jpeg;base64,", capturedBody);

        using var document = JsonDocument.Parse(capturedBody);
        var content = document.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Contains(content.EnumerateArray(), item => item.GetProperty("type").GetString() == "image_url");
    }

    [Fact]
    public async Task RatePhotoAsync_returns_audit_when_response_cannot_be_parsed()
    {
        using var tempDirectory = new TempDirectory();
        var imagePath = Path.Combine(tempDirectory.Path, "IMG_0001.JPG");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAH/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAEFAqf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/ASP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/ASP/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAY/Al//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/IV//2gAMAwEAAgADAAAAEP/EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8QH//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8QH//EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEAAT8QH//Z"));

        var handler = new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"photo_type\":\"portrait\",\"score\":8,\"category\":\"keep\",\"criteria\":[],\"reason\":\"Integer score should fail.\"}"
                      }
                    }
                  ]
                }
                """),
        }));

        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleRatingClient(httpClient);

        var result = await client.RatePhotoAsync(
            new PhotoRatingRequest(
                new Uri("https://openrouter.ai/api/v1"),
                "sk-test",
                "qwen/qwen3.7-max",
                DefaultPhotoRatingPrompt.Text,
                imagePath),
            CancellationToken.None);

        Assert.Null(result.Rating);
        Assert.Contains("Score", result.Audit.Error);
        Assert.Contains("Integer score should fail", result.Audit.RawMessageContent);
        Assert.Contains("\"choices\"", result.Audit.RawResponseJson);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return sendAsync(request);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
