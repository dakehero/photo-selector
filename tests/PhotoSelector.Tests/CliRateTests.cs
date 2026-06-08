using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Cli;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class CliRateTests
{
    [Fact]
    public void Import_enqueues_rating_work_and_process_rates_pending_jobs()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var importOutput = new StringWriter();
        var importError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["import", sourceDirectory], importOutput, importError));
        Assert.Equal(string.Empty, importError.ToString());
        Assert.Contains("pending: 1", importOutput.ToString());

        var statusOutput = new StringWriter();
        var statusError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["status", sourceDirectory], statusOutput, statusError));
        Assert.Equal(string.Empty, statusError.ToString());
        Assert.Contains("pending: 1", statusOutput.ToString());
        Assert.Contains("rated: 0", statusOutput.ToString());

        var secretStore = new MemorySecretStore();
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
                "street",
                7.8,
                "maybe",
                [new AiRatingCriterion("impact", 7.7, "Good gesture.")],
                "Worth a second look."));
        var processOutput = new StringWriter();
        var processError = new StringWriter();

        var processExitCode = CliApp.Run(
            ["process", sourceDirectory],
            processOutput,
            processError,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, processExitCode);
        Assert.Equal(string.Empty, processError.ToString());
        Assert.Contains("Rated 1 photo(s)", processOutput.ToString());
        Assert.Equal(jpegPath, Assert.Single(client.Requests).ImagePath);

        var processedStatusOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["status", sourceDirectory], processedStatusOutput, TextWriter.Null));
        Assert.Contains("pending: 0", processedStatusOutput.ToString());
        Assert.Contains("rated: 1", processedStatusOutput.ToString());

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        var rating = Assert.Single(database.ListRatings(photo.Id));
        Assert.Equal(7.8, rating.Score);
    }

    [Fact]
    public void Flush_requeues_directory_without_deleting_existing_ratings()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0001.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                8.2,
                "keep",
                [new AiRatingCriterion("impact", 8.0, "Strong.")],
                "Strong keeper."));
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, client));

        var flushOutput = new StringWriter();
        var flushError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["flush", sourceDirectory], flushOutput, flushError));
        Assert.Equal(string.Empty, flushError.ToString());
        Assert.Contains("pending: 1", flushOutput.ToString());

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Single(database.ListRatings(photo.Id));
        Assert.Equal(1, database.GetRatingJobSummary(project.Id).Pending);
    }

    [Fact]
    public void Reset_ratings_deletes_ratings_preserves_audit_and_requeues()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0001.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                8.2,
                "keep",
                [new AiRatingCriterion("impact", 8.0, "Strong.")],
                "Strong keeper."));
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, client));

        var resetOutput = new StringWriter();
        var resetError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["reset", "ratings", sourceDirectory], resetOutput, resetError));
        Assert.Equal(string.Empty, resetError.ToString());
        Assert.Contains("Reset 1 rating(s)", resetOutput.ToString());

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Empty(database.ListRatings(photo.Id));
        Assert.Single(database.ListRatingAuditLogs(photo.Id));
        Assert.Equal(1, database.GetRatingJobSummary(project.Id).Pending);
    }

    [Fact]
    public void Results_summarizes_latest_ratings_for_directory()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var firstJpeg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var secondJpeg = Path.Combine(sourceDirectory, "IMG_0002.JPG");
        var thirdJpeg = Path.Combine(sourceDirectory, "IMG_0003.JPG");
        File.WriteAllBytes(firstJpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(secondJpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(thirdJpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(
                projectId,
                [
                    new PhotoPair("IMG_0001", firstJpeg, null),
                    new PhotoPair("IMG_0002", secondJpeg, null),
                    new PhotoPair("IMG_0003", thirdJpeg, null),
                ]);

            var photos = database.ListPhotos(projectId);
            database.SaveRating(photos[0].Id, "openrouter", "qwen/qwen3-vl-30b-a3b-thinking", "street", 8.4, "keep", "[]", "Strong keeper.");
            database.SaveRating(photos[1].Id, "openrouter", "qwen/qwen3-vl-30b-a3b-thinking", "portrait", 6.2, "maybe", "[]", "Useful alternate.");
            database.SaveRating(photos[2].Id, "openrouter", "qwen/qwen3-vl-30b-a3b-thinking", "landscape", 3.1, "reject", "[]", "Weak frame.");
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["results", sourceDirectory], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var text = output.ToString();
        Assert.Contains($"Project: {sourceDirectory}", text);
        Assert.Contains("photos: 3", text);
        Assert.Contains("rated: 3", text);
        Assert.Contains("unrated: 0", text);
        Assert.Contains("keep: 1", text);
        Assert.Contains("maybe: 1", text);
        Assert.Contains("reject: 1", text);
        Assert.Contains("top:", text);
        Assert.Contains("8.4 keep IMG_0001 - Strong keeper.", text);
    }

    [Fact]
    public void Export_keep_copies_latest_keep_rated_jpeg_and_raw_pairs_from_catalog()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        var targetDirectory = Path.Combine(tempDirectory.Path, "exports");
        Directory.CreateDirectory(sourceDirectory);
        var keepJpeg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var keepRaw = Path.Combine(sourceDirectory, "IMG_0001.CR3");
        var maybeJpeg = Path.Combine(sourceDirectory, "IMG_0002.JPG");
        File.WriteAllText(keepJpeg, "keep jpeg");
        File.WriteAllText(keepRaw, "keep raw");
        File.WriteAllText(maybeJpeg, "maybe jpeg");

        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(
                projectId,
                [
                    new PhotoPair("IMG_0001", keepJpeg, keepRaw),
                    new PhotoPair("IMG_0002", maybeJpeg, null),
                ]);
            var photos = database.ListPhotos(projectId);
            database.SaveRating(photos[0].Id, "openrouter", "qwen/qwen3-vl-30b-a3b-thinking", "street", 8.4, "keep", "[]", "Strong keeper.");
            database.SaveRating(photos[1].Id, "openrouter", "qwen/qwen3-vl-30b-a3b-thinking", "street", 6.4, "maybe", "[]", "Useful alternate.");
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["export", "keep", sourceDirectory, targetDirectory], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var exportDirectory = Assert.Single(Directory.EnumerateDirectories(targetDirectory));
        Assert.True(File.Exists(Path.Combine(exportDirectory, "IMG_0001.JPG")));
        Assert.True(File.Exists(Path.Combine(exportDirectory, "IMG_0001.CR3")));
        Assert.False(File.Exists(Path.Combine(exportDirectory, "IMG_0002.JPG")));
        Assert.Contains("Exported 2 file(s) from 1 photo(s)", output.ToString());
    }


    [Fact]
    public void Scan_imports_directory_and_rates_jpegs_by_default()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = new MemorySecretStore();
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
                "street",
                7.6,
                "maybe",
                [new AiRatingCriterion("impact", 7.5, "Good timing.")],
                "Useful candidate."));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(
            ["scan", sourceDirectory],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Scanned 1 photo(s)", output.ToString());
        Assert.Contains("Rated 1 photo(s)", output.ToString());
        Assert.Equal(jpegPath, Assert.Single(client.Requests).ImagePath);

        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Single(database.ListRatings(photo.Id));
    }

    [Fact]
    public void Process_sends_each_pending_jpeg_to_ai_and_saves_ratings()
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
            database.EnqueueRatingJobs(projectId);
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
            ["process", sourceDirectory],
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

        Assert.Equal(
            0,
            CliApp.Run(
                ["process", sourceDirectory],
                TextWriter.Null,
                TextWriter.Null,
                TextReader.Null,
                secretStore,
                client));
        Assert.Single(client.Requests);

        Assert.Equal(
            0,
            CliApp.Run(
                ["flush", sourceDirectory, "--now"],
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
    public void Process_accepts_openrouter_provider_from_shared_config()
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
            database.EnqueueRatingJobs(projectId);
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
        var exitCode = CliApp.Run(["process", sourceDirectory], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Provider: openrouter", output.ToString());
    }

    [Fact]
    public void Process_saves_audit_log_when_ai_result_cannot_be_parsed()
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
            database.EnqueueRatingJobs(projectId);
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
        var exitCode = CliApp.Run(["process", sourceDirectory], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed 1", output.ToString());
        Assert.Contains("Score must", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

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

    private static MemorySecretStore Login(MemorySecretStore secretStore)
    {
        Assert.Equal(
            0,
            CliApp.Run(
                ["auth", "login", "--profile", "default", "--api-key-stdin"],
                TextWriter.Null,
                TextWriter.Null,
                new StringReader("sk-test\n"),
                secretStore));
        return secretStore;
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
