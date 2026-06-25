using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Ai.Reviews;
using PhotoSelector.Cli;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class CliSmokeTests
{
    [Fact]
    public void Scan_indexes_directory_then_catalog_commands_open_project_and_photos()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = CreateSourceDirectory(tempDirectory.Path);
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

        var secretStore = Login(new MemorySecretStore());
        var client = new RecordingRatingClient();
        var scanOutput = new StringWriter();
        var scanError = new StringWriter();
        var scanExitCode = CliApp.Run(["scan", sourceDirectory], scanOutput, scanError, TextReader.Null, secretStore, client);

        Assert.Equal(0, scanExitCode);
        Assert.Equal(string.Empty, scanError.ToString());
        Assert.True(File.Exists(databasePath));
        Assert.Contains("Scanned 2 photo(s)", scanOutput.ToString());

        var projectsOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["projects", "list", "--json"], projectsOutput, TextWriter.Null));
        using var projectsDocument = JsonDocument.Parse(projectsOutput.ToString());
        var project = Assert.Single(projectsDocument.RootElement.GetProperty("projects").EnumerateArray());
        var projectId = project.GetProperty("id").GetInt64();
        Assert.Equal(sourceDirectory, project.GetProperty("sourceDirectory").GetString());
        Assert.Equal(2, project.GetProperty("photoCount").GetInt32());

        var openOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["open", projectId.ToString(), "--json"], openOutput, TextWriter.Null));
        using var openDocument = JsonDocument.Parse(openOutput.ToString());
        var openedProject = openDocument.RootElement.GetProperty("project");
        Assert.Equal(projectId, openedProject.GetProperty("id").GetInt64());
        Assert.Equal(sourceDirectory, openedProject.GetProperty("sourceDirectory").GetString());
        Assert.Equal(2, openedProject.GetProperty("photos").GetArrayLength());

        var photosOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["photos", "list", "--project", projectId.ToString(), "--json"], photosOutput, TextWriter.Null));
        using var photosDocument = JsonDocument.Parse(photosOutput.ToString());
        var photos = photosDocument.RootElement.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        Assert.Equal("IMG_0001", photos[0].GetProperty("baseName").GetString());
        Assert.Equal("IMG_0002", photos[1].GetProperty("baseName").GetString());
    }

    [Fact]
    public void Groups_json_outputs_in_memory_filename_sequence_groups_for_indexed_project()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0004.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0010.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0011.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "cover.JPG"), "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["groups", sourceDirectory, "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("filename-sequence", document.RootElement.GetProperty("method").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("maxFilenameGap").GetInt32());
        Assert.Equal(10, document.RootElement.GetProperty("maxCaptureTimeGapSeconds").GetInt32());
        var stages = document.RootElement.GetProperty("stages").EnumerateArray().ToArray();
        Assert.Equal("filename-sequence", stages[0].GetProperty("name").GetString());
        Assert.Equal("applied", stages[0].GetProperty("status").GetString());
        Assert.Equal("capture-time-window", stages[1].GetProperty("name").GetString());
        Assert.Equal("applied-when-present", stages[1].GetProperty("status").GetString());
        Assert.Equal("ai-encoder", stages[2].GetProperty("name").GetString());
        Assert.Equal("reserved", stages[2].GetProperty("status").GetString());
        Assert.Equal(sourceDirectory, document.RootElement.GetProperty("project").GetProperty("sourceDirectory").GetString());
        var groups = document.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal("filename-sequence:IMG_:0001-0004", groups[0].GetProperty("id").GetString());
        Assert.Equal("sequence", groups[0].GetProperty("type").GetString());
        Assert.Equal("IMG_", groups[0].GetProperty("key").GetString());
        Assert.Equal("filename sequence within gap 2; capture time gap <= 10s when available", groups[0].GetProperty("reason").GetString());
        Assert.Equal(["IMG_0001", "IMG_0002", "IMG_0004"], groups[0].GetProperty("items").EnumerateArray().Select(item => item.GetProperty("baseName").GetString() ?? string.Empty).ToArray());
        Assert.Equal([0, 1, 2], groups[0].GetProperty("items").EnumerateArray().Select(item => item.GetProperty("order").GetInt32()).ToArray());
        Assert.Equal("filename-sequence:IMG_:0010-0011", groups[1].GetProperty("id").GetString());
        Assert.Equal(["IMG_0010", "IMG_0011"], groups[1].GetProperty("items").EnumerateArray().Select(item => item.GetProperty("baseName").GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    public void Groups_json_uses_exif_capture_time_to_split_filename_sequences()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0001.JPG"), PhotoMetadataReaderTests.CreateExifJpeg("2026:06:18 10:00:00"));
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0002.JPG"), PhotoMetadataReaderTests.CreateExifJpeg("2026:06:18 10:00:02"));
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0003.JPG"), PhotoMetadataReaderTests.CreateExifJpeg("2026:06:18 10:05:00"));
        File.WriteAllBytes(Path.Combine(sourceDirectory, "IMG_0004.JPG"), PhotoMetadataReaderTests.CreateExifJpeg("2026:06:18 10:05:02"));

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(["groups", sourceDirectory, "--json"], output, TextWriter.Null));

        using var document = JsonDocument.Parse(output.ToString());
        var groups = document.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(["IMG_0001", "IMG_0002"], groups[0].GetProperty("items").EnumerateArray().Select(item => item.GetProperty("baseName").GetString() ?? string.Empty).ToArray());
        Assert.Equal(["IMG_0003", "IMG_0004"], groups[1].GetProperty("items").EnumerateArray().Select(item => item.GetProperty("baseName").GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    public void Groups_json_ignores_photos_marked_missing_after_rescan()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var firstJpeg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var secondJpeg = Path.Combine(sourceDirectory, "IMG_0002.JPG");
        File.WriteAllText(firstJpeg, "jpeg");
        File.WriteAllText(secondJpeg, "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));
        File.Delete(secondJpeg);
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        var photosOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["photos", "list", "--project", "1", "--json"], photosOutput, TextWriter.Null));
        using var photosDocument = JsonDocument.Parse(photosOutput.ToString());
        var missingPhoto = photosDocument.RootElement.GetProperty("photos").EnumerateArray().Single(photo => photo.GetProperty("baseName").GetString() == "IMG_0002");
        Assert.Equal("missing", missingPhoto.GetProperty("importStatus").GetString());

        var groupsOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["groups", sourceDirectory, "--json"], groupsOutput, TextWriter.Null));
        using var groupsDocument = JsonDocument.Parse(groupsOutput.ToString());
        Assert.Empty(groupsDocument.RootElement.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public void Review_group_json_saves_snapshot_for_computed_group()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var firstJpeg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var secondJpeg = Path.Combine(sourceDirectory, "IMG_0002.JPG");
        File.WriteAllText(firstJpeg, "jpeg");
        File.WriteAllText(secondJpeg, "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            [
                "review",
                "group",
                sourceDirectory,
                "filename-sequence:IMG_:0001-0002",
                "--winner",
                "IMG_0002",
                "--reason",
                "Sharper expression.",
                "--json",
            ],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var reviewJson = document.RootElement.GetProperty("review");
        var reviewId = reviewJson.GetProperty("id").GetInt64();
        Assert.Equal("filename-sequence:IMG_:0001-0002", reviewJson.GetProperty("groupId").GetString());
        Assert.Equal("IMG_0002", reviewJson.GetProperty("winnerBaseName").GetString());
        Assert.Equal("Sharper expression.", reviewJson.GetProperty("reason").GetString());
        Assert.Equal(["IMG_0001", "IMG_0002"], reviewJson.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("baseName").GetString() ?? string.Empty).ToArray());

        File.Delete(firstJpeg);
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var review = Assert.Single(database.ListGroupReviews(project.Id));
        Assert.Equal(reviewId, review.Id);
        Assert.Equal("manual", review.Provider);
        Assert.Equal("manual", review.Model);
        Assert.Equal("IMG_0002", review.WinnerBaseName);
        var items = database.ListGroupReviewItems(review.Id);
        Assert.Equal(["IMG_0001", "IMG_0002"], items.Select(item => item.BaseName).ToArray());
        Assert.Equal(["imported", "imported"], items.Select(item => item.ImportStatus).ToArray());
    }

    [Fact]
    public void Review_group_json_uses_ai_when_winner_is_not_provided()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["config", "set", "provider", "openrouter"], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["config", "set", "model", "test-group-review-model"], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new RecordingRatingClient()));

        var reviewClient = new RecordingGroupReviewClient("IMG_0002");
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            [
                "review",
                "group",
                sourceDirectory,
                "filename-sequence:IMG_:0001-0002",
                "--json",
            ],
            output,
            error,
            TextReader.Null,
            secretStore,
            ratingClient: null,
            groupReviewClient: reviewClient);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(reviewClient.Request);
        Assert.Equal("test-group-review-model", reviewClient.Request.Model);
        Assert.Equal(["IMG_0001", "IMG_0002"], reviewClient.Request.Items.Select(item => item.BaseName).ToArray());
        using var document = JsonDocument.Parse(output.ToString());
        var reviewJson = document.RootElement.GetProperty("review");
        Assert.Equal("IMG_0002", reviewJson.GetProperty("winnerBaseName").GetString());
        Assert.Equal("Best expression and sharpest frame.", reviewJson.GetProperty("reason").GetString());

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var project = Assert.Single(database.ListProjects());
        var review = Assert.Single(database.ListGroupReviews(project.Id));
        Assert.Equal("openrouter", review.Provider);
        Assert.Equal("test-group-review-model", review.Model);
        Assert.Equal("IMG_0002", review.WinnerBaseName);
        Assert.Contains("[redacted-data-url]", review.RequestJsonRedacted);
        Assert.DoesNotContain("data:image", review.RequestJsonRedacted);
        Assert.Contains("winner_base_name", review.RawMessageContent);
        Assert.Equal(200, review.HttpStatus);
        Assert.Null(review.Error);
    }

    [Fact]
    public void Review_json_outputs_shoot_review_draft_from_catalog_state()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0003.JPG"), "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new PerPhotoRatingClient()));
        Assert.Equal(
            0,
            CliApp.Run(
                [
                    "review",
                    "group",
                    sourceDirectory,
                    "filename-sequence:IMG_:0001-0003",
                    "--winner",
                    "IMG_0001",
                    "--reason",
                    "Strongest timing.",
                ],
                TextWriter.Null,
                TextWriter.Null));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["review", sourceDirectory, "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var review = document.RootElement.GetProperty("review");
        Assert.Equal(sourceDirectory, review.GetProperty("project").GetProperty("sourceDirectory").GetString());
        var summary = review.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("currentPhotos").GetInt32());
        Assert.Equal(3, summary.GetProperty("ratedPhotos").GetInt32());
        Assert.Equal(1, summary.GetProperty("keep").GetInt32());
        Assert.Equal(1, summary.GetProperty("maybe").GetInt32());
        Assert.Equal(1, summary.GetProperty("reject").GetInt32());
        Assert.Equal(1, summary.GetProperty("groups").GetInt32());
        Assert.Equal(1, summary.GetProperty("reviewedGroups").GetInt32());
        var groupReview = Assert.Single(review.GetProperty("groupReviews").EnumerateArray());
        Assert.Equal("filename-sequence:IMG_:0001-0003", groupReview.GetProperty("groupId").GetString());
        Assert.Equal("IMG_0001", groupReview.GetProperty("winnerBaseName").GetString());
        var topCandidate = Assert.Single(review.GetProperty("topCandidates").EnumerateArray());
        Assert.Equal("IMG_0001", topCandidate.GetProperty("baseName").GetString());
        Assert.Contains("IMG_0001", review.GetProperty("summaryText").GetString());
        Assert.NotEmpty(review.GetProperty("nextShootNotes").EnumerateArray());
    }

    [Fact]
    public void Review_save_json_persists_shoot_review_snapshot()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");

        var secretStore = Login(new MemorySecretStore());
        Assert.Equal(0, CliApp.Run(["scan", sourceDirectory], TextWriter.Null, TextWriter.Null, TextReader.Null, secretStore, new PerPhotoRatingClient()));
        Assert.Equal(
            0,
            CliApp.Run(
                [
                    "review",
                    "group",
                    sourceDirectory,
                    "filename-sequence:IMG_:0001-0002",
                    "--winner",
                    "IMG_0001",
                    "--reason",
                    "Best frame.",
                ],
                TextWriter.Null,
                TextWriter.Null));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["review", sourceDirectory, "--save", "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var review = document.RootElement.GetProperty("review");
        var reviewId = review.GetProperty("reviewId").GetInt64();
        Assert.True(reviewId > 0);

        using var database = ProjectDatabase.Open(Path.Combine(tempDirectory.Path, "photo-selector.db"));
        database.Migrate();
        var saved = Assert.Single(database.ListShootReviews());
        Assert.Equal(reviewId, saved.Id);
        Assert.Contains("IMG_0001", saved.SummaryText);
        Assert.Contains("\"keep\":", saved.SummaryJson);
        Assert.Contains("IMG_0001", saved.TopCandidatesJson);
        Assert.Contains("filename-sequence:IMG_:0001-0002", saved.GroupReviewsJson);
        Assert.Contains("group-level", saved.NextShootNotesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_creates_shared_catalog_database_with_jpg_and_raw_files()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var rawPath = Path.Combine(sourceDirectory, "IMG_0001.CR3");
        File.WriteAllText(jpegPath, "jpeg");
        File.WriteAllText(rawPath, "raw");

        var secretStore = Login(new MemorySecretStore());
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(["scan", sourceDirectory], output, error, TextReader.Null, secretStore, new RecordingRatingClient());

        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        var sidecarDatabasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(databasePath));
        Assert.False(File.Exists(sidecarDatabasePath));
        Assert.Contains("Scanned 1 photo(s)", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        using var database = ProjectDatabase.Open(databasePath);
        var project = Assert.Single(database.ListProjects());
        Assert.Equal(sourceDirectory, project.SourceDirectory);
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Equal("IMG_0001", photo.BaseName);
        Assert.Equal(jpegPath, photo.JpegPath);
        Assert.Equal(rawPath, photo.RawPath);
    }

    [Fact]
    public void Removed_catalog_worker_commands_show_usage()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        foreach (var command in new[] { "import", "process", "flush" })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = CliApp.Run([command, tempDirectory.Path], output, error);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Usage:", error.ToString());
            Assert.DoesNotContain($"photo-selector {command}", error.ToString());
        }
    }

    [Fact]
    public void Help_outputs_human_friendly_command_overview()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Photo Selector", output.ToString());
        Assert.Contains("photo-selector pick <directory>", output.ToString());
        Assert.Contains("photo-selector help --json", output.ToString());
        Assert.DoesNotContain("photo-selector import", output.ToString());
        Assert.DoesNotContain("photo-selector process", output.ToString());
        Assert.DoesNotContain("photo-selector flush", output.ToString());
    }

    [Fact]
    public void Help_json_outputs_machine_readable_command_schema()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["help", "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("photo-selector", document.RootElement.GetProperty("name").GetString());
        var commands = document.RootElement.GetProperty("commands").EnumerateArray().ToArray();
        var pick = commands.Single(command => command.GetProperty("name").GetString() == "pick");
        Assert.Equal("photo-selector pick <directory>", pick.GetProperty("usage").GetString());
        Assert.True(pick.GetProperty("output").GetProperty("json").GetBoolean());
        Assert.Equal("directory", pick.GetProperty("arguments")[0].GetProperty("kind").GetString());
        Assert.Contains(commands, command => command.GetProperty("name").GetString() == "projects list");
        Assert.Contains(commands, command => command.GetProperty("name").GetString() == "groups");
        var review = commands.Single(command => command.GetProperty("name").GetString() == "review");
        Assert.Equal("photo-selector review <directory> [--save] [--json]", review.GetProperty("usage").GetString());
        Assert.Contains(
            review.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("name").GetString() == "--save" && !option.GetProperty("required").GetBoolean());
        var reviewGroup = commands.Single(command => command.GetProperty("name").GetString() == "review group");
        Assert.Equal(
            "photo-selector review group <directory> <group-id> [--winner <photo-id|base-name> --reason <text>] [--json]",
            reviewGroup.GetProperty("usage").GetString());
        var reviewGroupOptions = reviewGroup.GetProperty("options").EnumerateArray().ToArray();
        Assert.False(reviewGroupOptions.Single(option => option.GetProperty("name").GetString() == "--winner").GetProperty("required").GetBoolean());
        Assert.False(reviewGroupOptions.Single(option => option.GetProperty("name").GetString() == "--reason").GetProperty("required").GetBoolean());
        Assert.DoesNotContain(commands, command => command.GetProperty("name").GetString() == "process");
    }

    [Fact]
    public void Help_command_json_outputs_one_command_schema()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["help", "pick", "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("pick", document.RootElement.GetProperty("name").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("name").GetString() == "--concurrency");
    }

    [Fact]
    public void Help_auth_status_json_includes_verbose_secret_store_diagnostics_option()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["help", "auth", "status", "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("auth status", document.RootElement.GetProperty("name").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("name").GetString() == "--verbose");
        Assert.Contains(
            document.RootElement.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("name").GetString() == "--json");
    }

    [Fact]
    public void Help_unknown_command_returns_usage_error()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApp.Run(["help", "process"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Unknown command: process", error.ToString());
        Assert.Contains("Usage:", error.ToString());
        Assert.DoesNotContain("photo-selector process", error.ToString());
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

    private sealed class RecordingRatingClient : IPhotoRatingClient
    {
        public Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            var rating = new AiRating(
                "street",
                7.1,
                "maybe",
                [new AiRatingCriterion("impact", 7.0, "Useful.")],
                "Useful candidate.");
            return Task.FromResult(new AiRatingClientResult(
                rating,
                new AiRatingAudit(
                    request.Prompt,
                    """{"image_url":"[redacted-data-url]"}""",
                    """{"photo_type":"street","score":7.1}""",
                    """{"choices":[{"message":{"content":"{\"photo_type\":\"street\",\"score\":7.1}"}}]}""",
                    200,
                    null)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingGroupReviewClient(string winnerBaseName) : IGroupReviewClient
    {
        public GroupReviewRequest? Request { get; private set; }

        public Task<GroupReviewClientResult> ReviewGroupAsync(GroupReviewRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            var review = new GroupReviewDecision(
                winnerBaseName,
                "Best expression and sharpest frame.",
                [
                    new GroupReviewItemDecision(winnerBaseName, "winner", "Best expression."),
                ]);
            return Task.FromResult(new GroupReviewClientResult(
                review,
                new AiRatingAudit(
                    request.Prompt,
                    """{"image_urls":["[redacted-data-url]"]}""",
                    """{"winner_base_name":"IMG_0002","reason":"Best expression and sharpest frame."}""",
                    """{"choices":[{"message":{"content":"{\"winner_base_name\":\"IMG_0002\",\"reason\":\"Best expression and sharpest frame.\"}"}}]}""",
                    200,
                    null)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class PerPhotoRatingClient : IPhotoRatingClient
    {
        public Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken)
        {
            var baseName = Path.GetFileNameWithoutExtension(request.ImagePath);
            var (score, category) = baseName switch
            {
                "IMG_0001" => (9.0, "keep"),
                "IMG_0002" => (6.8, "maybe"),
                "IMG_0003" => (3.2, "reject"),
                _ => (5.0, "maybe"),
            };
            var rating = new AiRating(
                "street",
                score,
                category,
                [new AiRatingCriterion("impact", score, "Useful.")],
                $"{baseName} rating.");
            return Task.FromResult(new AiRatingClientResult(
                rating,
                new AiRatingAudit(
                    request.Prompt,
                    """{"image_url":"[redacted-data-url]"}""",
                    $$"""{"photo_type":"street","score":{{score}},"category":"{{category}}"}""",
                    """{"choices":[{"message":{"content":"{\"photo_type\":\"street\"}"}}]}""",
                    200,
                    null)));
        }

        public void Dispose()
        {
        }
    }

    private static string CreateSourceDirectory(string rootDirectory)
    {
        var sourceDirectory = Path.Combine(rootDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.CR3"), "raw");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");
        return sourceDirectory;
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
}
