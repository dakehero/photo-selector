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
    public void Scan_indexes_directory_and_rates_pending_jobs()
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
                7.8,
                "maybe",
                [new AiRatingCriterion("impact", 7.7, "Good gesture.")],
                "Worth a second look."));
        var scanOutput = new StringWriter();
        var scanError = new StringWriter();

        var scanExitCode = CliApp.Run(
            ["scan", sourceDirectory],
            scanOutput,
            scanError,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, scanExitCode);
        Assert.Equal(string.Empty, scanError.ToString());
        Assert.Contains("Rated 1 photo(s)", scanOutput.ToString());
        Assert.Equal(jpegPath, Assert.Single(client.Requests).ImagePath);

        var statusOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["status", sourceDirectory], statusOutput, TextWriter.Null));
        Assert.Contains("pending: 0", statusOutput.ToString());
        Assert.Contains("rated: 1", statusOutput.ToString());

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        var rating = Assert.Single(database.ListRatings(photo.Id));
        Assert.Equal(7.8, rating.Score);
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
    public void Results_status_and_arena_support_json_output()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        long arenaRunId;
        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
            var photo = Assert.Single(database.ListPhotos(projectId));
            database.SaveRating(photo.Id, "openrouter", "model-a", "street", 8.4, "keep", "[]", "Strong keeper.");
            arenaRunId = database.CreateArenaRun(projectId, "openrouter", "model-a,model-b", "prompt", "zh-Hans", 1);
            database.SaveArenaRating(arenaRunId, photo.Id, "openrouter", "model-a", "street", 8.4, "keep", "[]", "Strong keeper.", "prompt", "{}", "{}", "{}", 200, null);
            database.SaveArenaRating(arenaRunId, photo.Id, "openrouter", "model-b", "street", 5.4, "maybe", "[]", "Flat alternate.", "prompt", "{}", "{}", "{}", 200, null);
        }

        var resultsOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["results", sourceDirectory, "--json"], resultsOutput, TextWriter.Null));
        using var resultsJson = JsonDocument.Parse(resultsOutput.ToString());
        Assert.Equal(sourceDirectory, resultsJson.RootElement.GetProperty("project").GetProperty("sourceDirectory").GetString());
        Assert.Equal(1, resultsJson.RootElement.GetProperty("summary").GetProperty("photos").GetInt32());
        Assert.Equal(1, resultsJson.RootElement.GetProperty("summary").GetProperty("keep").GetInt32());
        Assert.Equal("IMG_0001", resultsJson.RootElement.GetProperty("all")[0].GetProperty("baseName").GetString());

        var statusOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["status", sourceDirectory, "--json"], statusOutput, TextWriter.Null));
        using var statusJson = JsonDocument.Parse(statusOutput.ToString());
        Assert.Equal(1, statusJson.RootElement.GetProperty("rated").GetInt32());
        Assert.Equal(0, statusJson.RootElement.GetProperty("jobs").GetProperty("pending").GetInt32());

        var listOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["arena", "list", sourceDirectory, "--json"], listOutput, TextWriter.Null));
        using var listJson = JsonDocument.Parse(listOutput.ToString());
        Assert.Equal(arenaRunId, listJson.RootElement.GetProperty("runs")[0].GetProperty("id").GetInt64());

        var showOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["arena", "show", arenaRunId.ToString(), "--json"], showOutput, TextWriter.Null));
        using var showJson = JsonDocument.Parse(showOutput.ToString());
        Assert.Equal(arenaRunId, showJson.RootElement.GetProperty("run").GetProperty("id").GetInt64());
        Assert.Equal("IMG_0001", showJson.RootElement.GetProperty("photos")[0].GetProperty("baseName").GetString());
        Assert.Equal("model-a", showJson.RootElement.GetProperty("summary").GetProperty("models")[0].GetProperty("model").GetString());
    }

    [Fact]
    public void Results_photo_audit_json_outputs_decision_trace()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        long photoId;
        long ratingId;
        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
            var photo = Assert.Single(database.ListPhotos(projectId));
            photoId = photo.Id;
            ratingId = database.SaveRating(photo.Id, "openrouter", "model-a", "street", 8.4, "keep", "[]", "Strong keeper.");
            database.SaveRatingAuditLog(
                photo.Id,
                ratingId,
                "openrouter",
                "model-a",
                "prompt text",
                """{"model":"model-a","image_url":"[redacted-data-url]"}""",
                """{"photo_type":"street","score":8.4}""",
                """{"choices":[{"message":{"content":"{\"score\":8.4}"}}]}""",
                200,
                null);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["results", sourceDirectory, "--photo", "IMG_0001", "--audit", "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(photoId, json.RootElement.GetProperty("photo").GetProperty("photoId").GetInt64());
        Assert.Equal("IMG_0001", json.RootElement.GetProperty("photo").GetProperty("baseName").GetString());
        var audit = Assert.Single(json.RootElement.GetProperty("audit").EnumerateArray());
        Assert.Equal(ratingId, audit.GetProperty("ratingId").GetInt64());
        Assert.Equal("openrouter", audit.GetProperty("provider").GetString());
        Assert.Equal("model-a", audit.GetProperty("model").GetString());
        Assert.Equal("prompt text", audit.GetProperty("prompt").GetString());
        Assert.Contains("[redacted-data-url]", audit.GetProperty("requestJsonRedacted").GetString());
        Assert.Contains("\"score\":8.4", audit.GetProperty("rawMessageContent").GetString());
        Assert.Equal(200, audit.GetProperty("httpStatus").GetInt32());
    }

    [Fact]
    public void Results_audit_requires_photo_selector()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["results", "--audit"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("--audit requires --photo.", error.ToString());
    }

    [Fact]
    public void Results_photo_base_name_reports_ambiguous_matches_across_projects()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var firstDirectory = Path.Combine(tempDirectory.Path, "shoot-a");
        var secondDirectory = Path.Combine(tempDirectory.Path, "shoot-b");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var firstJpeg = Path.Combine(firstDirectory, "IMG_0001.JPG");
        var secondJpeg = Path.Combine(secondDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(firstJpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(secondJpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var firstProjectId = database.CreateProject(firstDirectory);
            database.ReplacePhotos(firstProjectId, [new PhotoPair("IMG_0001", firstJpeg, null)]);
            var secondProjectId = database.CreateProject(secondDirectory);
            database.ReplacePhotos(secondProjectId, [new PhotoPair("IMG_0001", secondJpeg, null)]);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["results", "--photo", "IMG_0001", "--json"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Photo selector is ambiguous: IMG_0001", error.ToString());
    }

    [Fact]
    public void Mark_saves_manual_decision_for_photo()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["mark", sourceDirectory, "IMG_0001", "--decision", "keep", "--stars", "5", "--note", "portfolio candidate", "--json"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("IMG_0001", json.RootElement.GetProperty("photo").GetProperty("baseName").GetString());
        Assert.Equal("keep", json.RootElement.GetProperty("mark").GetProperty("decision").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("mark").GetProperty("stars").GetInt32());
        Assert.Equal("portfolio candidate", json.RootElement.GetProperty("mark").GetProperty("note").GetString());

        using var reopened = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        reopened.Migrate();
        var project = Assert.Single(reopened.ListProjects());
        var photo = Assert.Single(reopened.ListPhotos(project.Id));
        var mark = reopened.GetUserMark(photo.Id);
        Assert.NotNull(mark);
        Assert.Equal("keep", mark.Decision);
        Assert.Equal(5, mark.Stars);
        Assert.Equal("portfolio candidate", mark.Note);
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
        Assert.Contains("rating 1/1: IMG_0001", output.ToString());
        Assert.Contains("Rated 1 photo(s)", output.ToString());
        Assert.Contains("Results:", output.ToString());
        Assert.Contains("photos: 1", output.ToString());
        Assert.Contains("maybe: 1", output.ToString());
        Assert.Contains("7.6 maybe IMG_0001 - Useful candidate.", output.ToString());
        Assert.Contains("all:", output.ToString());
        Assert.Contains("  7.6 maybe IMG_0001 - Useful candidate.", output.ToString());
        Assert.Equal(jpegPath, Assert.Single(client.Requests).ImagePath);

        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Single(database.ListRatings(photo.Id));
    }

    [Fact]
    public void Scan_json_outputs_only_final_machine_readable_summary()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = Login(new MemorySecretStore());
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
            ["scan", sourceDirectory, "--json"],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var text = output.ToString();
        Assert.DoesNotContain("Scanned", text);
        Assert.DoesNotContain("rating 1/1", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(sourceDirectory, json.RootElement.GetProperty("project").GetProperty("sourceDirectory").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("scan").GetProperty("photos").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("processing").GetProperty("rated").GetInt32());
        Assert.Equal("maybe", json.RootElement.GetProperty("results").GetProperty("all")[0].GetProperty("category").GetString());
        Assert.Equal(jpegPath, Assert.Single(client.Requests).ImagePath);
    }

    [Fact]
    public void Scan_retries_failed_jobs_and_outputs_full_results()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var projectId = database.CreateProject(sourceDirectory);
            database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
            database.EnqueueRatingJobs(projectId);
            var job = Assert.Single(database.ListPendingRatingJobs(projectId));
            database.MarkRatingJobFailed(job.Id, "temporary failure");
        }

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                8.4,
                "keep",
                [new AiRatingCriterion("impact", 8.2, "Strong moment.")],
                "Strong keeper."));
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
        var text = output.ToString();
        Assert.Contains("rating 1/1: IMG_0001", text);
        Assert.Contains("Rated 1 photo(s)", text);
        Assert.Contains("failed: 0", text);
        Assert.Contains("all:", text);
        Assert.Contains("  8.4 keep IMG_0001 - Strong keeper.", text);
    }

    [Fact]
    public void Scan_requeues_photo_when_same_file_changes()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                7.1,
                "maybe",
                [new AiRatingCriterion("impact", 7.0, "Useful.")],
                "Useful candidate."));

        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, client));
        Assert.Single(client.Requests);

        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, client));
        Assert.Single(client.Requests);

        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9 });
        File.SetLastWriteTimeUtc(jpegPath, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, client));
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public void Scan_sends_each_jpeg_to_ai_and_saves_ratings()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

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
            ["scan", sourceDirectory],
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
        var project = Assert.Single(reopened.ListProjects());
        var photoId = Assert.Single(reopened.ListPhotos(project.Id)).Id;
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
                ["scan", sourceDirectory],
                TextWriter.Null,
                TextWriter.Null,
                TextReader.Null,
                secretStore,
                client));
        Assert.Single(client.Requests);
    }

    [Fact]
    public void Scan_model_option_overrides_config_without_saving_it()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.Equal(0, CliApp.Run(["config", "set", "model", "configured-model"], TextWriter.Null, TextWriter.Null));
        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                7.1,
                "maybe",
                [new AiRatingCriterion("impact", 7.0, "Useful.")],
                "Useful candidate."));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(
            ["scan", sourceDirectory, "--model", "override-model"],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("override-model", Assert.Single(client.Requests).Model);
        Assert.Contains("model: override-model", output.ToString());

        var configOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["config", "list"], configOutput, TextWriter.Null));
        Assert.Contains("model: configured-model", configOutput.ToString());
    }

    [Fact]
    public void Pick_uses_configured_concurrency_for_ai_requests()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0001.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0002.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0003.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.Equal(0, CliApp.Run(["config", "set", "concurrency", "2"], TextWriter.Null, TextWriter.Null));
        var secretStore = Login(new MemorySecretStore());

        var client = new DelayedRatingClient(
            new AiRating(
                "street",
                7.1,
                "maybe",
                [new AiRatingCriterion("impact", 7.0, "Useful.")],
                "Useful candidate."));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["pick", sourceDirectory], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(3, client.RequestCount);
        Assert.Equal(2, client.MaxInFlight);
        Assert.Contains("Rated 3 photo(s)", output.ToString());
    }

    [Fact]
    public void Pick_rates_directory_with_selection_prompt_preview_quality_and_concurrency_override()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0001.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0002.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0003.JPG"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.Equal(0, CliApp.Run(["config", "set", "concurrency", "1"], TextWriter.Null, TextWriter.Null));
        var secretStore = Login(new MemorySecretStore());
        var client = new DelayedRatingClient(
            new AiRating(
                "street",
                7.1,
                "maybe",
                [new AiRatingCriterion("impact", 7.0, "Useful.")],
                "Useful candidate."));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(
            ["pick", sourceDirectory, "--quality", "standard", "--preview-jpeg-quality", "77", "--concurrency", "2"],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(3, client.RequestCount);
        Assert.Equal(2, client.MaxInFlight);
        Assert.All(client.Requests, request =>
        {
            Assert.Contains("fast photo culling", request.Prompt);
            Assert.NotNull(request.Preview);
            Assert.Equal(1600, request.Preview!.MaxEdge);
            Assert.Equal(77, request.Preview.JpegQuality);
        });
        Assert.Contains("Picked 3 photo(s)", output.ToString());
        Assert.Contains("maybe: 3", output.ToString());
    }

    [Fact]
    public void Rate_and_coach_accept_one_photo_with_high_quality_defaults()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient(
            new AiRating(
                "street",
                8.1,
                "keep",
                [new AiRatingCriterion("impact", 8.0, "Strong.")],
                "Strong keeper."));

        var rateOutput = new StringWriter();
        var rateError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["rate", jpegPath], rateOutput, rateError, TextReader.Null, secretStore, client));
        Assert.Equal(string.Empty, rateError.ToString());
        var rateRequest = Assert.Single(client.Requests);
        Assert.Contains("detailed photographic critique", rateRequest.Prompt);
        Assert.NotNull(rateRequest.Preview);
        Assert.Equal(2048, rateRequest.Preview!.MaxEdge);
        Assert.Equal(90, rateRequest.Preview.JpegQuality);
        Assert.Contains("8.1 keep", rateOutput.ToString());

        client.Requests.Clear();
        var coachOutput = new StringWriter();
        var coachError = new StringWriter();
        Assert.Equal(0, CliApp.Run(["coach", jpegPath, "--quality", "detail"], coachOutput, coachError, TextReader.Null, secretStore, client));
        Assert.Equal(string.Empty, coachError.ToString());
        var coachRequest = Assert.Single(client.Requests);
        Assert.Contains("photography coach", coachRequest.Prompt);
        Assert.NotNull(coachRequest.Preview);
        Assert.Equal(3072, coachRequest.Preview!.MaxEdge);
        Assert.Equal(92, coachRequest.Preview.JpegQuality);

        var directoryError = new StringWriter();
        Assert.Equal(1, CliApp.Run(["rate", sourceDirectory], TextWriter.Null, directoryError, TextReader.Null, secretStore, client));
        Assert.Contains("requires one image file", directoryError.ToString());
    }

    [Fact]
    public void Arena_rates_limited_photos_with_multiple_models_without_saving_primary_ratings()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.Equal(0, CliApp.Run(["config", "set", "provider", "openrouter"], TextWriter.Null, TextWriter.Null));
        var secretStore = Login(new MemorySecretStore());
        var client = new ModelSwitchingRatingClient(new Dictionary<string, AiRating>
        {
            ["model-a"] = new(
                "landscape",
                8.2,
                "keep",
                [new AiRatingCriterion("impact", 8.0, "Strong.")],
                "Strong keeper."),
            ["model-b"] = new(
                "landscape",
                5.4,
                "maybe",
                [new AiRatingCriterion("impact", 5.0, "Flat.")],
                "Flat alternate."),
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(
            ["arena", sourceDirectory, "--models", "model-a,model-b", "--limit", "1"],
            output,
            error,
            TextReader.Null,
            secretStore,
            client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var text = output.ToString();
        Assert.Contains("Arena:", text);
        Assert.Contains("1 photo(s) x 2 model(s)", text);
        Assert.Contains("model-a", text);
        Assert.Contains("avg: 8.2", text);
        Assert.Contains("model-b", text);
        Assert.Contains("avg: 5.4", text);
        Assert.Contains("largest disagreements:", text);
        Assert.Contains("IMG_0001", text);
        Assert.Equal(["model-a", "model-b"], client.Requests.Select(request => request.Model).ToArray());

        long runId;
        using (var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db")))
        {
            database.Migrate();
            var project = Assert.Single(database.ListProjects());
            var photo = Assert.Single(database.ListPhotos(project.Id));
            Assert.Empty(database.ListRatings(photo.Id));
            var run = Assert.Single(database.ListArenaRuns(project.Id));
            runId = run.Id;
            Assert.Equal(2, database.ListArenaRatings(run.Id).Count);
        }

        var listOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["arena", "list", sourceDirectory], listOutput, TextWriter.Null));
        Assert.Contains($"Run: {runId}", listOutput.ToString());
        Assert.Contains("model-a,model-b", listOutput.ToString());

        var showOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["arena", "show", runId.ToString()], showOutput, TextWriter.Null));
        var showText = showOutput.ToString();
        Assert.Contains($"Arena: {runId}", showText);
        Assert.Contains("photos:", showText);
        Assert.Contains("IMG_0001", showText);
        Assert.Contains("model-a 8.2 keep - Strong keeper.", showText);
        Assert.Contains("model-b 5.4 maybe - Flat alternate.", showText);
    }

    [Fact]
    public void Scan_accepts_openrouter_provider_from_shared_config()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
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
        var exitCode = CliApp.Run(["scan", sourceDirectory], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Provider: openrouter", output.ToString());
    }

    [Fact]
    public void Scan_saves_audit_log_when_ai_result_cannot_be_parsed()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

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
        var exitCode = CliApp.Run(["scan", sourceDirectory], output, error, TextReader.Null, secretStore, client);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed 1", output.ToString());
        Assert.Contains("Score must", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        using var reopened = ProjectDatabase.Open(databasePath);
        reopened.Migrate();
        var project = Assert.Single(reopened.ListProjects());
        var photoId = Assert.Single(reopened.ListPhotos(project.Id)).Id;
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

    private sealed class ModelSwitchingRatingClient(IReadOnlyDictionary<string, AiRating> ratingsByModel) : IPhotoRatingClient
    {
        public List<PhotoRatingRequest> Requests { get; } = [];

        public Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var rating = ratingsByModel[request.Model];
            return Task.FromResult(new AiRatingClientResult(
                rating,
                new AiRatingAudit(
                    request.Prompt,
                    """{"image_url":"[redacted-data-url]"}""",
                    $$"""{"photo_type":"{{rating.PhotoType}}","score":{{rating.Score}}}""",
                    """{"choices":[{"message":{"content":"{}"}}]}""",
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

    private sealed class DelayedRatingClient(AiRating rating) : IPhotoRatingClient
    {
        private int inFlight;
        private int maxInFlight;
        private int requestCount;
        private readonly object requestsLock = new();

        public List<PhotoRatingRequest> Requests { get; } = [];

        public int MaxInFlight => maxInFlight;

        public int RequestCount => requestCount;

        public async Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            lock (requestsLock)
            {
                Requests.Add(request);
            }

            Interlocked.Increment(ref requestCount);
            var current = Interlocked.Increment(ref inFlight);
            while (true)
            {
                var observed = maxInFlight;
                if (current <= observed ||
                    Interlocked.CompareExchange(ref maxInFlight, current, observed) == observed)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(100, cancellationToken);
                return new AiRatingClientResult(
                    rating,
                    new AiRatingAudit(
                        request.Prompt,
                        """{"image_url":"[redacted-data-url]"}""",
                        $$"""{"photo_type":"{{rating.PhotoType}}","score":{{rating.Score}}}""",
                        """{"choices":[{"message":{"content":"{}"}}]}""",
                        200,
                        null));
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
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
