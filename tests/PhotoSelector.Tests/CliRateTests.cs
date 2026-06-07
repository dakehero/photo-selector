using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Cli;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class CliRateTests
{
    [Fact]
    public void Rate_sends_each_jpeg_to_ai_and_saves_ratings()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

        long photoId;
        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, Path.Combine(sourceDirectory, "IMG_0001.CR3"))]);
            photoId = Assert.Single(database.ListPhotos(projectId)).Id;
        }

        var secretStore = new MemorySecretStore();
        secretStore.Set("photo-selector/default", "sk-test");
        var loginOutput = new StringWriter();
        Assert.Equal(
            0,
            CliApp.Run(
                ["auth", "login", "--profile", "default", "--api-key-stdin"],
                loginOutput,
                TextWriter.Null,
                new StringReader("sk-test\n"),
                secretStore));

        var client = new RecordingRatingClient(
            new AiRating(
                "portrait",
                7.3,
                "maybe",
                [new AiRatingCriterion("impact", 7.2, "Good expression.")],
                "Good moment, minor focus issue."));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(
            ["rate", databasePath],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Rated 1 photo(s)", output.ToString());
        var request = Assert.Single(client.Requests);
        Assert.Equal(new Uri("https://api.openai.com/v1"), request.BaseUrl);
        Assert.Equal("sk-test", request.ApiKey);
        Assert.Equal("gpt-4.1-mini", request.Model);
        Assert.Contains(DefaultPhotoRatingPrompt.Text, request.Prompt);
        Assert.Contains("Output all human-readable comments and verdicts in English.", request.Prompt);
        Assert.Equal(jpegPath, request.ImagePath);

        using var reopened = ProjectDatabase.Open(databasePath);
        reopened.Migrate();
        var rating = Assert.Single(reopened.ListRatings(photoId));
        Assert.Equal("portrait", rating.PhotoType);
        Assert.Equal(7.3, rating.Score);
        Assert.Equal("maybe", rating.Category);
        Assert.Equal("""[{"name":"impact","score":7.2,"comment":"Good expression."}]""", rating.CriteriaJson);
        Assert.Equal("Good moment, minor focus issue.", rating.Reason);
        Assert.Equal("openai-compatible", rating.Provider);
        Assert.Equal("gpt-4.1-mini", rating.Model);
        var auditLog = Assert.Single(reopened.ListRatingAuditLogs(photoId));
        Assert.Equal(rating.Id, auditLog.RatingId);
        Assert.Equal(photoId, auditLog.PhotoId);
        Assert.Equal("openai-compatible", auditLog.Provider);
        Assert.Equal("gpt-4.1-mini", auditLog.Model);
        Assert.Contains("Output all human-readable comments", auditLog.Prompt);
        Assert.Contains("\"image_url\":\"[redacted-data-url]\"", auditLog.RequestJsonRedacted);
        Assert.DoesNotContain("sk-test", auditLog.RequestJsonRedacted);
        Assert.DoesNotContain("data:image", auditLog.RequestJsonRedacted);
        Assert.Contains("\"photo_type\":\"portrait\"", auditLog.RawMessageContent);
        Assert.Contains("\"choices\"", auditLog.RawResponseJson);
        Assert.Equal(200, auditLog.HttpStatus);
        Assert.Null(auditLog.Error);

        var listOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["list", databasePath, "--json"], listOutput, TextWriter.Null));
        using var document = JsonDocument.Parse(listOutput.ToString());
        var latestRating = document.RootElement
            .GetProperty("projects")[0]
            .GetProperty("photos")[0]
            .GetProperty("latestRating");
        Assert.Equal("portrait", latestRating.GetProperty("photoType").GetString());
        Assert.Equal(7.3, latestRating.GetProperty("score").GetDouble());
        Assert.Equal("maybe", latestRating.GetProperty("category").GetString());
        Assert.Equal(7.2, latestRating.GetProperty("criteria")[0].GetProperty("score").GetDouble());

        var auditOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["audit", databasePath, "--photo-id", photoId.ToString(), "--json"], auditOutput, TextWriter.Null));
        using var auditDocument = JsonDocument.Parse(auditOutput.ToString());
        var audit = Assert.Single(auditDocument.RootElement.GetProperty("logs").EnumerateArray());
        Assert.Equal(rating.Id, audit.GetProperty("ratingId").GetInt64());
        Assert.Contains("Output all human-readable comments", audit.GetProperty("prompt").GetString());
        Assert.Contains("[redacted-data-url]", audit.GetProperty("requestJsonRedacted").GetString());
        Assert.DoesNotContain("sk-test", audit.GetProperty("requestJsonRedacted").GetString());
        Assert.Equal(200, audit.GetProperty("httpStatus").GetInt32());

        Assert.Equal(
            0,
            CliApp.Run(
                ["rate", databasePath],
                TextWriter.Null,
                TextWriter.Null,
                TextReader.Null,
                secretStore,
                client));
        Assert.Single(client.Requests);

        Assert.Equal(
            0,
            CliApp.Run(
                ["rate", databasePath, "--force"],
                TextWriter.Null,
                TextWriter.Null,
                TextReader.Null,
                secretStore,
                client));
        Assert.Equal(2, client.Requests.Count);

        using var rerated = ProjectDatabase.Open(databasePath);
        rerated.Migrate();
        Assert.Equal(2, rerated.ListRatings(photoId).Count);
    }

    [Fact]
    public void Rate_accepts_openrouter_provider_from_shared_config()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
        }

        var secretStore = new MemorySecretStore();
        Assert.Equal(0, CliApp.Run(["config", "set", "provider", "openrouter"], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore));
        Assert.Equal(
            0,
            CliApp.Run(
                ["auth", "login", "--profile", "default", "--api-key-stdin"],
                TextWriter.Null,
                TextWriter.Null,
                new StringReader("sk-test\n"),
                secretStore));

        var client = new RecordingRatingClient(
            new AiRating(
                "landscape",
                8.1,
                "keep",
                [new AiRatingCriterion("impact", 8.0, "Strong light.")],
                "Strong keeper."));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["rate", databasePath], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Provider: openrouter", output.ToString());
    }

    [Fact]
    public void Rate_saves_audit_log_when_ai_result_cannot_be_parsed()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

        long photoId;
        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
            photoId = Assert.Single(database.ListPhotos(projectId)).Id;
        }

        var secretStore = new MemorySecretStore();
        Assert.Equal(
            0,
            CliApp.Run(
                ["auth", "login", "--profile", "default", "--api-key-stdin"],
                TextWriter.Null,
                TextWriter.Null,
                new StringReader("sk-test\n"),
                secretStore));

        var client = new FixedResultRatingClient(new AiRatingClientResult(
            null,
            new AiRatingAudit(
                "prompt",
                """{"model":"gpt-4.1-mini","image_url":"[redacted-data-url]"}""",
                """{"score":8}""",
                """{"choices":[{"message":{"content":"{\"score\":8}"}}]}""",
                200,
                "Score must be a number with exactly one decimal place.")));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["rate", databasePath], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed 1", output.ToString());
        Assert.Contains("Score must", error.ToString());

        using var reopened = ProjectDatabase.Open(databasePath);
        reopened.Migrate();
        Assert.Empty(reopened.ListRatings(photoId));
        var audit = Assert.Single(reopened.ListRatingAuditLogs(photoId));
        Assert.Null(audit.RatingId);
        Assert.Equal("Score must be a number with exactly one decimal place.", audit.Error);
        Assert.Contains("\"score\":8", audit.RawMessageContent);
        Assert.DoesNotContain("sk-test", audit.RequestJsonRedacted);
    }

    private sealed class RecordingRatingClient(AiRating rating) : IPhotoRatingClient
    {
        public List<PhotoRatingRequest> Requests { get; } = [];

        public Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AiRatingClientResult(
                rating,
                new AiRatingAudit(
                    request.Prompt,
                    """{"model":"gpt-4.1-mini","image_url":"[redacted-data-url]"}""",
                    """{"photo_type":"portrait","score":7.3}""",
                    """{"choices":[{"message":{"content":"{\"photo_type\":\"portrait\",\"score\":7.3}"}}]}""",
                    200,
                    null)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedResultRatingClient(AiRatingClientResult result) : IPhotoRatingClient
    {
        public Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly string name;
        private readonly string? previousValue;

        public ScopedEnvironment(string name, string value)
        {
            this.name = name;
            previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(name, previousValue);
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
