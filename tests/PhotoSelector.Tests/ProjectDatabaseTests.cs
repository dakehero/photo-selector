using Microsoft.Data.Sqlite;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class ProjectDatabaseTests
{
    [Fact]
    public void ProjectDatabase_uses_linq2db_instead_of_hand_written_sql_commands()
    {
        var sourcePath = FindRepositoryFile("src/PhotoSelector.Core/Storage/ProjectDatabase.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("CommandText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteScalar", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteNonQuery", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_database_saves_projects_and_replaces_photos()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);

        database.ReplacePhotos(
            projectId,
            new[]
            {
                new PhotoPair("IMG_0002", Path.Combine(sourceDirectory, "IMG_0002.JPG"), null),
                new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), Path.Combine(sourceDirectory, "IMG_0001.CR3")),
            });

        database.ReplacePhotos(
            projectId,
            new[]
            {
                new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), Path.Combine(sourceDirectory, "IMG_0001.CR3")),
                new PhotoPair("IMG_0003", Path.Combine(sourceDirectory, "IMG_0003.JPG"), Path.Combine(sourceDirectory, "IMG_0003.CR3")),
            });

        var projects = database.ListProjects();
        var photos = database.ListPhotos(projectId);

        Assert.Single(projects);
        Assert.Equal(sourceDirectory, projects[0].SourceDirectory);
        Assert.Equal(3, photos.Count);
        Assert.Equal("IMG_0001", photos[0].BaseName);
        Assert.NotNull(photos[0].JpegPath);
        Assert.NotNull(photos[0].RawPath);
        Assert.Equal("missing", photos.Single(photo => photo.BaseName == "IMG_0002").ImportStatus);
    }

    [Fact]
    public void ReplacePhotos_stores_jpeg_exif_capture_time_when_available()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, PhotoMetadataReaderTests.CreateExifJpeg("2026:06:18 10:11:12"));

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);

        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);

        var photo = Assert.Single(database.ListPhotos(projectId));
        Assert.Equal(new DateTimeOffset(2026, 6, 18, 10, 11, 12, TimeSpan.Zero), photo.CaptureTime);
    }

    [Fact]
    public void ReplacePhotos_marks_unseen_photos_missing_and_preserves_history()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        var firstJpeg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var secondJpeg = Path.Combine(sourceDirectory, "IMG_0002.JPG");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);
        database.ReplacePhotos(
            projectId,
            [
                new PhotoPair("IMG_0001", firstJpeg, null),
                new PhotoPair("IMG_0002", secondJpeg, null),
            ]);
        var missingPhoto = database.ListPhotos(projectId).Single(photo => photo.BaseName == "IMG_0002");
        var ratingId = database.SaveRating(missingPhoto.Id, "openrouter", "model-a", "street", 8.4, "keep", "[]", "Strong keeper.");
        database.SaveRatingAuditLog(missingPhoto.Id, ratingId, "openrouter", "model-a", "prompt", "{}", "{}", "{}", 200, null);
        database.SaveUserMark(missingPhoto.Id, "keep", 5, "portfolio candidate");

        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", firstJpeg, null)]);

        var photos = database.ListPhotos(projectId);
        var retainedMissingPhoto = photos.Single(photo => photo.BaseName == "IMG_0002");
        Assert.Equal("missing", retainedMissingPhoto.ImportStatus);
        Assert.Single(database.ListRatings(retainedMissingPhoto.Id));
        Assert.Single(database.ListRatingAuditLogs(retainedMissingPhoto.Id));
        Assert.NotNull(database.GetUserMark(retainedMissingPhoto.Id));
    }

    [Fact]
    public void ReplacePhotos_restores_missing_photo_to_imported_when_it_reappears_unchanged()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllText(jpegPath, "jpeg");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
        database.ReplacePhotos(projectId, []);

        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);

        var photo = Assert.Single(database.ListPhotos(projectId));
        Assert.Equal("imported", photo.ImportStatus);
    }

    [Fact]
    public void ReplacePhotos_restores_missing_photo_as_changed_when_it_reappears_with_new_fingerprint()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        File.WriteAllText(jpegPath, "before");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);
        database.ReplacePhotos(projectId, []);
        File.WriteAllText(jpegPath, "after");
        File.SetLastWriteTimeUtc(jpegPath, DateTime.UtcNow.AddMinutes(5));

        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", jpegPath, null)]);

        var photo = Assert.Single(database.ListPhotos(projectId));
        Assert.Equal("changed", photo.ImportStatus);
        Assert.Single(database.ListPendingRatingJobs(projectId));
    }

    [Fact]
    public void ReplacePhotos_fails_for_missing_project()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();

        Assert.Throws<SqliteException>(() =>
            database.ReplacePhotos(
                999,
                new[]
                {
                    new PhotoPair("IMG_0001", "IMG_0001.JPG", "IMG_0001.CR3"),
                }));
    }

    [Fact]
    public void User_mark_is_saved_and_updated_per_photo()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), null)]);
        var photo = Assert.Single(database.ListPhotos(projectId));

        database.SaveUserMark(photo.Id, "keep", 5, "portfolio candidate");
        var first = database.GetUserMark(photo.Id);

        Assert.NotNull(first);
        Assert.Equal(photo.Id, first.PhotoId);
        Assert.Equal("keep", first.Decision);
        Assert.Equal(5, first.Stars);
        Assert.Equal("portfolio candidate", first.Note);

        database.SaveUserMark(photo.Id, "maybe", 3, "compare with next frame");
        var updated = database.GetUserMark(photo.Id);

        Assert.NotNull(updated);
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal("maybe", updated.Decision);
        Assert.Equal(3, updated.Stars);
        Assert.Equal("compare with next frame", updated.Note);
        Assert.True(updated.UpdatedAt >= first.UpdatedAt);
    }

    [Fact]
    public void Shoot_review_snapshot_is_saved_and_listed()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);

        var reviewId = database.SaveShootReview(
            projectId,
            "Reviewed 2 current photo(s).",
            """{"currentPhotos":2,"keep":1}""",
            """[{"baseName":"IMG_0001"}]""",
            """[{"groupId":"filename-sequence:IMG_:0001-0002"}]""",
            """["soft focus"]""",
            """["review similar frames"]""");

        var review = Assert.Single(database.ListShootReviews(projectId));
        Assert.Equal(reviewId, review.Id);
        Assert.Equal(projectId, review.ProjectId);
        Assert.Equal("Reviewed 2 current photo(s).", review.SummaryText);
        Assert.Contains("currentPhotos", review.SummaryJson);
        Assert.Contains("IMG_0001", review.TopCandidatesJson);
        Assert.Contains("filename-sequence", review.GroupReviewsJson);
        Assert.Contains("soft focus", review.WeakPatternsJson);
        Assert.Contains("review similar frames", review.NextShootNotesJson);
    }

    [Fact]
    public void ResetRatings_preserves_audit_logs_with_cleared_rating_reference_by_default()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, [new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), null)]);
        var photo = Assert.Single(database.ListPhotos(projectId));
        var ratingId = database.SaveRating(photo.Id, "openrouter", "model-a", "street", 8.4, "keep", "[]", "Strong keeper.");
        database.SaveRatingAuditLog(photo.Id, ratingId, "openrouter", "model-a", "prompt", "{}", "{}", "{}", 200, null);

        var deleted = database.ResetRatings(projectId);

        Assert.Equal(1, deleted);
        Assert.Empty(database.ListRatings(photo.Id));
        var audit = Assert.Single(database.ListRatingAuditLogs(photo.Id));
        Assert.Null(audit.RatingId);
    }

    [Fact]
    public void Migrate_keeps_one_schema_version_row()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
            database.Migrate();
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(id), MAX(id), MIN(version), MAX(version) FROM schema_version;";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
        Assert.Equal(1, reader.GetInt64(4));
    }

    [Fact]
    public void Migrate_normalizes_legacy_schema_version_table()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_version (
                    version INTEGER NOT NULL
                );

                INSERT INTO schema_version (version)
                VALUES (1);
                """;
            command.ExecuteNonQuery();
        }

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
        }

        using var migratedConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        migratedConnection.Open();
        using var migratedCommand = migratedConnection.CreateCommand();
        migratedCommand.CommandText = "SELECT COUNT(*), MIN(id), MAX(id), MIN(version), MAX(version) FROM schema_version;";

        using var reader = migratedCommand.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
        Assert.Equal(1, reader.GetInt64(4));
    }

    [Fact]
    public void Migrate_adds_group_review_audit_columns_to_existing_table()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE group_reviews (
                    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    project_id INTEGER NOT NULL,
                    group_id TEXT NOT NULL,
                    group_type TEXT NOT NULL,
                    group_key TEXT NOT NULL,
                    group_reason TEXT NOT NULL,
                    winner_photo_id INTEGER NOT NULL,
                    winner_base_name TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    model TEXT NOT NULL,
                    prompt TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
        }

        using var migratedConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        migratedConnection.Open();
        using var migratedCommand = migratedConnection.CreateCommand();
        migratedCommand.CommandText = "PRAGMA table_info(group_reviews);";

        using var reader = migratedCommand.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("request_json_redacted", columns);
        Assert.Contains("raw_message_content", columns);
        Assert.Contains("raw_response_json", columns);
        Assert.Contains("http_status", columns);
        Assert.Contains("error", columns);
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

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
