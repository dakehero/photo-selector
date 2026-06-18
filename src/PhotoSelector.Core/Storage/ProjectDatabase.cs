using Microsoft.Data.Sqlite;
using PhotoSelector.Core.Metadata;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Core.Storage;

public sealed class ProjectDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    private ProjectDatabase(SqliteConnection connection)
    {
        this.connection = connection;
    }

    public static ProjectDatabase Open(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return new ProjectDatabase(connection);
    }

    public void Migrate()
    {
        NormalizeSchemaVersionTable();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );

            INSERT INTO schema_version (id, version)
            VALUES (1, 1)
            ON CONFLICT(id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_directory TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_opened_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS photos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                base_name TEXT NOT NULL,
                jpeg_path TEXT NULL,
                raw_path TEXT NULL,
                capture_time TEXT NULL,
                import_status TEXT NOT NULL,
                jpeg_size INTEGER NULL,
                jpeg_mtime_utc TEXT NULL,
                raw_size INTEGER NULL,
                raw_mtime_utc TEXT NULL,
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ratings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                photo_id INTEGER NOT NULL,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                photo_type TEXT NOT NULL DEFAULT 'unknown',
                score REAL NOT NULL,
                category TEXT NOT NULL,
                criteria_json TEXT NOT NULL DEFAULT '[]',
                reason TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (photo_id) REFERENCES photos (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rating_jobs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                photo_id INTEGER NOT NULL UNIQUE,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE,
                FOREIGN KEY (photo_id) REFERENCES photos (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rating_audit_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                photo_id INTEGER NOT NULL,
                rating_id INTEGER NULL,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                prompt TEXT NOT NULL,
                request_json_redacted TEXT NOT NULL,
                raw_message_content TEXT NOT NULL,
                raw_response_json TEXT NOT NULL,
                http_status INTEGER NULL,
                error TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (photo_id) REFERENCES photos (id) ON DELETE CASCADE,
                FOREIGN KEY (rating_id) REFERENCES ratings (id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS arena_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                provider TEXT NOT NULL,
                models_csv TEXT NOT NULL,
                prompt TEXT NOT NULL,
                output_language TEXT NOT NULL,
                limit_count INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS arena_ratings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                arena_run_id INTEGER NOT NULL,
                photo_id INTEGER NOT NULL,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                photo_type TEXT NULL,
                score REAL NULL,
                category TEXT NULL,
                criteria_json TEXT NOT NULL DEFAULT '[]',
                reason TEXT NOT NULL DEFAULT '',
                prompt TEXT NOT NULL,
                request_json_redacted TEXT NOT NULL,
                raw_message_content TEXT NOT NULL,
                raw_response_json TEXT NOT NULL,
                http_status INTEGER NULL,
                error TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (arena_run_id) REFERENCES arena_runs (id) ON DELETE CASCADE,
                FOREIGN KEY (photo_id) REFERENCES photos (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS user_marks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                photo_id INTEGER NOT NULL UNIQUE,
                decision TEXT NOT NULL,
                stars INTEGER NOT NULL,
                note TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL,
                FOREIGN KEY (photo_id) REFERENCES photos (id) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();
        AddColumnIfMissing("ratings", "photo_type", "TEXT NOT NULL DEFAULT 'unknown'");
        AddColumnIfMissing("ratings", "criteria_json", "TEXT NOT NULL DEFAULT '[]'");
        AddColumnIfMissing("photos", "jpeg_size", "INTEGER NULL");
        AddColumnIfMissing("photos", "jpeg_mtime_utc", "TEXT NULL");
        AddColumnIfMissing("photos", "raw_size", "INTEGER NULL");
        AddColumnIfMissing("photos", "raw_mtime_utc", "TEXT NULL");
    }

    private void AddColumnIfMissing(string tableName, string columnName, string definition)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"""
            SELECT COUNT(*)
            FROM pragma_table_info('{tableName}')
            WHERE name = $column_name;
            """;
        checkCommand.Parameters.AddWithValue("$column_name", columnName);
        if ((long)checkCommand.ExecuteScalar()! > 0)
        {
            return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alterCommand.ExecuteNonQuery();
    }

    private void NormalizeSchemaVersionTable()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('schema_version')
            WHERE name = 'id';
            """;

        if ((long)command.ExecuteScalar()! > 0)
        {
            return;
        }

        using var rebuildCommand = connection.CreateCommand();
        rebuildCommand.CommandText = """
            DROP TABLE IF EXISTS schema_version;

            CREATE TABLE schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            """;
        rebuildCommand.ExecuteNonQuery();
    }

    public long CreateProject(string sourceDirectory)
    {
        var now = DateTimeOffset.UtcNow;

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (source_directory, created_at, last_opened_at)
            VALUES ($source_directory, $created_at, $last_opened_at);

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$source_directory", sourceDirectory);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(now));
        command.Parameters.AddWithValue("$last_opened_at", FormatTimestamp(now));

        return (long)command.ExecuteScalar()!;
    }

    public void ReplacePhotos(long projectId, IEnumerable<PhotoPair> pairs)
    {
        var pairList = pairs.ToArray();
        using var transaction = connection.BeginTransaction();
        var now = FormatTimestamp(DateTimeOffset.UtcNow);

        var importedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairList)
        {
            importedBaseNames.Add(pair.BaseName);
            var jpegFingerprint = GetFileFingerprint(pair.JpegPath);
            var rawFingerprint = GetFileFingerprint(pair.RawPath);
            var captureTime = PhotoMetadataReader.ReadCaptureTime(pair.JpegPath);
            using var existingCommand = connection.CreateCommand();
            existingCommand.Transaction = transaction;
            existingCommand.CommandText = """
                SELECT id, jpeg_path, raw_path, jpeg_size, jpeg_mtime_utc, raw_size, raw_mtime_utc
                FROM photos
                WHERE project_id = $project_id
                  AND base_name = $base_name COLLATE NOCASE
                ORDER BY id
                LIMIT 1;
                """;
            existingCommand.Parameters.AddWithValue("$project_id", projectId);
            existingCommand.Parameters.AddWithValue("$base_name", pair.BaseName);
            var existingId = existingCommand.ExecuteScalar();

            if (existingId is null)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO photos (
                        project_id,
                        base_name,
                        jpeg_path,
                        raw_path,
                        capture_time,
                        import_status,
                        jpeg_size,
                        jpeg_mtime_utc,
                        raw_size,
                        raw_mtime_utc
                    )
                    VALUES (
                        $project_id,
                        $base_name,
                        $jpeg_path,
                        $raw_path,
                        $capture_time,
                        $import_status,
                        $jpeg_size,
                        $jpeg_mtime_utc,
                        $raw_size,
                        $raw_mtime_utc
                    );
                    """;
                insertCommand.Parameters.AddWithValue("$project_id", projectId);
                insertCommand.Parameters.AddWithValue("$base_name", pair.BaseName);
                insertCommand.Parameters.AddWithValue("$jpeg_path", (object?)pair.JpegPath ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$raw_path", (object?)pair.RawPath ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$capture_time", (object?)FormatNullableTimestamp(captureTime) ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$import_status", "imported");
                insertCommand.Parameters.AddWithValue("$jpeg_size", (object?)jpegFingerprint.Size ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$jpeg_mtime_utc", (object?)jpegFingerprint.ModifiedAt ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$raw_size", (object?)rawFingerprint.Size ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$raw_mtime_utc", (object?)rawFingerprint.ModifiedAt ?? DBNull.Value);
                insertCommand.ExecuteNonQuery();
                continue;
            }

            using var existingReaderCommand = connection.CreateCommand();
            existingReaderCommand.Transaction = transaction;
            existingReaderCommand.CommandText = """
                SELECT id, jpeg_path, raw_path, jpeg_size, jpeg_mtime_utc, raw_size, raw_mtime_utc
                FROM photos
                WHERE id = $id;
                """;
            existingReaderCommand.Parameters.AddWithValue("$id", (long)existingId);
            using var existingReader = existingReaderCommand.ExecuteReader();
            existingReader.Read();
            var oldJpegPath = existingReader.IsDBNull(1) ? null : existingReader.GetString(1);
            var oldRawPath = existingReader.IsDBNull(2) ? null : existingReader.GetString(2);
            var oldJpegSize = existingReader.IsDBNull(3) ? (long?)null : existingReader.GetInt64(3);
            var oldJpegMtime = existingReader.IsDBNull(4) ? null : existingReader.GetString(4);
            var oldRawSize = existingReader.IsDBNull(5) ? (long?)null : existingReader.GetInt64(5);
            var oldRawMtime = existingReader.IsDBNull(6) ? null : existingReader.GetString(6);
            var hasExistingFingerprint = oldJpegSize is not null ||
                oldJpegMtime is not null ||
                oldRawSize is not null ||
                oldRawMtime is not null;
            var pathChanged = !string.Equals(oldJpegPath, pair.JpegPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(oldRawPath, pair.RawPath, StringComparison.OrdinalIgnoreCase);
            var fingerprintChanged = hasExistingFingerprint &&
                (!Nullable.Equals(oldJpegSize, jpegFingerprint.Size) ||
                    oldJpegMtime != jpegFingerprint.ModifiedAt ||
                    !Nullable.Equals(oldRawSize, rawFingerprint.Size) ||
                    oldRawMtime != rawFingerprint.ModifiedAt);
            var changed = pathChanged || fingerprintChanged;

            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE photos
                SET base_name = $base_name,
                    jpeg_path = $jpeg_path,
                    raw_path = $raw_path,
                    capture_time = $capture_time,
                    import_status = $import_status,
                    jpeg_size = $jpeg_size,
                    jpeg_mtime_utc = $jpeg_mtime_utc,
                    raw_size = $raw_size,
                    raw_mtime_utc = $raw_mtime_utc
                WHERE id = $id;
                """;
            updateCommand.Parameters.AddWithValue("$id", (long)existingId);
            updateCommand.Parameters.AddWithValue("$base_name", pair.BaseName);
            updateCommand.Parameters.AddWithValue("$jpeg_path", (object?)pair.JpegPath ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$raw_path", (object?)pair.RawPath ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$capture_time", (object?)FormatNullableTimestamp(captureTime) ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$import_status", changed ? "changed" : "imported");
            updateCommand.Parameters.AddWithValue("$jpeg_size", (object?)jpegFingerprint.Size ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$jpeg_mtime_utc", (object?)jpegFingerprint.ModifiedAt ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$raw_size", (object?)rawFingerprint.Size ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$raw_mtime_utc", (object?)rawFingerprint.ModifiedAt ?? DBNull.Value);
            updateCommand.ExecuteNonQuery();

            if (changed && !string.IsNullOrWhiteSpace(pair.JpegPath))
            {
                RequeueRatingJob(transaction, projectId, (long)existingId, now);
            }
        }

        using (var staleCommand = connection.CreateCommand())
        {
            staleCommand.Transaction = transaction;
            staleCommand.CommandText = "SELECT id, base_name FROM photos WHERE project_id = $project_id;";
            staleCommand.Parameters.AddWithValue("$project_id", projectId);
            var stalePhotoIds = new List<long>();
            using (var reader = staleCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!importedBaseNames.Contains(reader.GetString(1)))
                    {
                        stalePhotoIds.Add(reader.GetInt64(0));
                    }
                }
            }

            foreach (var stalePhotoId in stalePhotoIds)
            {
                using var deleteCommand = connection.CreateCommand();
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM photos WHERE id = $id;";
                deleteCommand.Parameters.AddWithValue("$id", stalePhotoId);
                deleteCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public IReadOnlyList<PhotoProject> ListProjects()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_directory, created_at, last_opened_at
            FROM projects
            ORDER BY id;
            """;

        var projects = new List<PhotoProject>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(new PhotoProject(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                ParseTimestamp(reader.GetString(3))));
        }

        return projects;
    }

    public IReadOnlyList<PhotoItem> ListPhotos(long projectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                project_id,
                base_name,
                jpeg_path,
                raw_path,
                capture_time,
                import_status,
                jpeg_size,
                jpeg_mtime_utc,
                raw_size,
                raw_mtime_utc
            FROM photos
            WHERE project_id = $project_id
            ORDER BY base_name COLLATE NOCASE, base_name, id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId);

        var photos = new List<PhotoItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            photos.Add(new PhotoItem(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : ParseTimestamp(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : ParseTimestamp(reader.GetString(10))));
        }

        return photos;
    }

    public PhotoItem? GetPhoto(long photoId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                project_id,
                base_name,
                jpeg_path,
                raw_path,
                capture_time,
                import_status,
                jpeg_size,
                jpeg_mtime_utc,
                raw_size,
                raw_mtime_utc
            FROM photos
            WHERE id = $photo_id;
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new PhotoItem(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : ParseTimestamp(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : ParseTimestamp(reader.GetString(10)));
    }

    public int EnqueueRatingJobs(long projectId, bool force = false)
    {
        var now = FormatTimestamp(DateTimeOffset.UtcNow);
        var enqueued = 0;
        foreach (var photo in ListPhotos(projectId))
        {
            if (string.IsNullOrWhiteSpace(photo.JpegPath))
            {
                continue;
            }

            if (!force && ListRatings(photo.Id).Count > 0)
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = force
                ? """
                    INSERT INTO rating_jobs (project_id, photo_id, status, attempts, last_error, created_at, updated_at)
                    VALUES ($project_id, $photo_id, 'pending', 0, NULL, $created_at, $updated_at)
                    ON CONFLICT(photo_id) DO UPDATE SET
                        status = 'pending',
                        attempts = 0,
                        last_error = NULL,
                        updated_at = $updated_at;
                    """
                : """
                    INSERT INTO rating_jobs (project_id, photo_id, status, attempts, last_error, created_at, updated_at)
                    VALUES ($project_id, $photo_id, 'pending', 0, NULL, $created_at, $updated_at)
                    ON CONFLICT(photo_id) DO NOTHING;
                    """;
            command.Parameters.AddWithValue("$project_id", projectId);
            command.Parameters.AddWithValue("$photo_id", photo.Id);
            command.Parameters.AddWithValue("$created_at", now);
            command.Parameters.AddWithValue("$updated_at", now);
            if (command.ExecuteNonQuery() > 0)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private void RequeueRatingJob(SqliteTransaction transaction, long projectId, long photoId, string updatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rating_jobs (project_id, photo_id, status, attempts, last_error, created_at, updated_at)
            VALUES ($project_id, $photo_id, 'pending', 0, NULL, $created_at, $updated_at)
            ON CONFLICT(photo_id) DO UPDATE SET
                status = 'pending',
                attempts = 0,
                last_error = NULL,
                updated_at = $updated_at;
            """;
        command.Parameters.AddWithValue("$project_id", projectId);
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.Parameters.AddWithValue("$created_at", updatedAt);
        command.Parameters.AddWithValue("$updated_at", updatedAt);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RatingJob> ListRatingJobs(long? projectId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = projectId is null
            ? """
                SELECT id, project_id, photo_id, status, attempts, last_error, created_at, updated_at
                FROM rating_jobs
                ORDER BY id;
                """
            : """
                SELECT id, project_id, photo_id, status, attempts, last_error, created_at, updated_at
                FROM rating_jobs
                WHERE project_id = $project_id
                ORDER BY id;
                """;
        if (projectId is not null)
        {
            command.Parameters.AddWithValue("$project_id", projectId.Value);
        }

        var jobs = new List<RatingJob>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            jobs.Add(ReadRatingJob(reader));
        }

        return jobs;
    }

    public IReadOnlyList<RatingJob> ListPendingRatingJobs(long? projectId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = projectId is null
            ? """
                SELECT id, project_id, photo_id, status, attempts, last_error, created_at, updated_at
                FROM rating_jobs
                WHERE status = 'pending'
                ORDER BY id;
                """
            : """
                SELECT id, project_id, photo_id, status, attempts, last_error, created_at, updated_at
                FROM rating_jobs
                WHERE status = 'pending' AND project_id = $project_id
                ORDER BY id;
                """;
        if (projectId is not null)
        {
            command.Parameters.AddWithValue("$project_id", projectId.Value);
        }

        var jobs = new List<RatingJob>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            jobs.Add(ReadRatingJob(reader));
        }

        return jobs;
    }

    public RatingJobSummary GetRatingJobSummary(long? projectId = null)
    {
        var jobs = ListRatingJobs(projectId);
        return new RatingJobSummary(
            jobs.Count,
            jobs.Count(job => job.Status == "pending"),
            jobs.Count(job => job.Status == "completed"),
            jobs.Count(job => job.Status == "failed"));
    }

    public void MarkRatingJobCompleted(long jobId)
    {
        UpdateRatingJob(jobId, "completed", null);
    }

    public void MarkRatingJobFailed(long jobId, string error)
    {
        UpdateRatingJob(jobId, "failed", error);
    }

    private void UpdateRatingJob(long jobId, string status, string? error)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE rating_jobs
            SET status = $status,
                attempts = attempts + 1,
                last_error = $last_error,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", jobId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$last_error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    public long SaveRating(
        long photoId,
        string provider,
        string model,
        string photoType,
        double score,
        string category,
        string criteriaJson,
        string reason)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ratings (
                photo_id,
                provider,
                model,
                photo_type,
                score,
                category,
                criteria_json,
                reason,
                created_at
            )
            VALUES (
                $photo_id,
                $provider,
                $model,
                $photo_type,
                $score,
                $category,
                $criteria_json,
                $reason,
                $created_at
            );

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$photo_type", photoType);
        command.Parameters.AddWithValue("$score", score);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$criteria_json", criteriaJson);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(DateTimeOffset.UtcNow));
        return (long)command.ExecuteScalar()!;
    }

    public void SaveUserMark(long photoId, string decision, int stars, string? note)
    {
        if (!IsUserDecision(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), "Decision must be unreviewed, keep, maybe, or reject.");
        }

        if (stars is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(stars), "Stars must be between 0 and 5.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_marks (photo_id, decision, stars, note, updated_at)
            VALUES ($photo_id, $decision, $stars, $note, $updated_at)
            ON CONFLICT(photo_id) DO UPDATE SET
                decision = excluded.decision,
                stars = excluded.stars,
                note = excluded.note,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.Parameters.AddWithValue("$decision", decision);
        command.Parameters.AddWithValue("$stars", stars);
        command.Parameters.AddWithValue("$note", note ?? string.Empty);
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    public PhotoUserMark? GetUserMark(long photoId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, photo_id, decision, stars, note, updated_at
            FROM user_marks
            WHERE photo_id = $photo_id;
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new PhotoUserMark(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            ParseTimestamp(reader.GetString(5)));
    }

    public long SaveRatingAuditLog(
        long photoId,
        long? ratingId,
        string provider,
        string model,
        string prompt,
        string requestJsonRedacted,
        string rawMessageContent,
        string rawResponseJson,
        int? httpStatus,
        string? error)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO rating_audit_logs (
                photo_id,
                rating_id,
                provider,
                model,
                prompt,
                request_json_redacted,
                raw_message_content,
                raw_response_json,
                http_status,
                error,
                created_at
            )
            VALUES (
                $photo_id,
                $rating_id,
                $provider,
                $model,
                $prompt,
                $request_json_redacted,
                $raw_message_content,
                $raw_response_json,
                $http_status,
                $error,
                $created_at
            );

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.Parameters.AddWithValue("$rating_id", (object?)ratingId ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$prompt", prompt);
        command.Parameters.AddWithValue("$request_json_redacted", requestJsonRedacted);
        command.Parameters.AddWithValue("$raw_message_content", rawMessageContent);
        command.Parameters.AddWithValue("$raw_response_json", rawResponseJson);
        command.Parameters.AddWithValue("$http_status", (object?)httpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(DateTimeOffset.UtcNow));
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<PhotoRating> ListRatings(long photoId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, photo_id, provider, model, photo_type, score, category, criteria_json, reason, created_at
            FROM ratings
            WHERE photo_id = $photo_id
            ORDER BY created_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);

        var ratings = new List<PhotoRating>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ratings.Add(new PhotoRating(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                ParseTimestamp(reader.GetString(9))));
        }

        return ratings;
    }

    public IReadOnlyList<PhotoRatingAuditLog> ListRatingAuditLogs(long photoId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                photo_id,
                rating_id,
                provider,
                model,
                prompt,
                request_json_redacted,
                raw_message_content,
                raw_response_json,
                http_status,
                error,
                created_at
            FROM rating_audit_logs
            WHERE photo_id = $photo_id
            ORDER BY created_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$photo_id", photoId);

        var logs = new List<PhotoRatingAuditLog>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(new PhotoRatingAuditLog(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                ParseTimestamp(reader.GetString(11))));
        }

        return logs;
    }

    public long CreateArenaRun(
        long projectId,
        string provider,
        string modelsCsv,
        string prompt,
        string outputLanguage,
        int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO arena_runs (
                project_id,
                provider,
                models_csv,
                prompt,
                output_language,
                limit_count,
                created_at
            )
            VALUES (
                $project_id,
                $provider,
                $models_csv,
                $prompt,
                $output_language,
                $limit_count,
                $created_at
            );

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$project_id", projectId);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$models_csv", modelsCsv);
        command.Parameters.AddWithValue("$prompt", prompt);
        command.Parameters.AddWithValue("$output_language", outputLanguage);
        command.Parameters.AddWithValue("$limit_count", limit);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(DateTimeOffset.UtcNow));
        return (long)command.ExecuteScalar()!;
    }

    public long SaveArenaRating(
        long arenaRunId,
        long photoId,
        string provider,
        string model,
        string? photoType,
        double? score,
        string? category,
        string criteriaJson,
        string reason,
        string prompt,
        string requestJsonRedacted,
        string rawMessageContent,
        string rawResponseJson,
        int? httpStatus,
        string? error)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO arena_ratings (
                arena_run_id,
                photo_id,
                provider,
                model,
                photo_type,
                score,
                category,
                criteria_json,
                reason,
                prompt,
                request_json_redacted,
                raw_message_content,
                raw_response_json,
                http_status,
                error,
                created_at
            )
            VALUES (
                $arena_run_id,
                $photo_id,
                $provider,
                $model,
                $photo_type,
                $score,
                $category,
                $criteria_json,
                $reason,
                $prompt,
                $request_json_redacted,
                $raw_message_content,
                $raw_response_json,
                $http_status,
                $error,
                $created_at
            );

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$arena_run_id", arenaRunId);
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$photo_type", (object?)photoType ?? DBNull.Value);
        command.Parameters.AddWithValue("$score", (object?)score ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
        command.Parameters.AddWithValue("$criteria_json", criteriaJson);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$prompt", prompt);
        command.Parameters.AddWithValue("$request_json_redacted", requestJsonRedacted);
        command.Parameters.AddWithValue("$raw_message_content", rawMessageContent);
        command.Parameters.AddWithValue("$raw_response_json", rawResponseJson);
        command.Parameters.AddWithValue("$http_status", (object?)httpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(DateTimeOffset.UtcNow));
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<ArenaRun> ListArenaRuns(long? projectId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = projectId is null
            ? """
                SELECT id, project_id, provider, models_csv, prompt, output_language, limit_count, created_at
                FROM arena_runs
                ORDER BY id;
                """
            : """
                SELECT id, project_id, provider, models_csv, prompt, output_language, limit_count, created_at
                FROM arena_runs
                WHERE project_id = $project_id
                ORDER BY id;
                """;
        if (projectId is not null)
        {
            command.Parameters.AddWithValue("$project_id", projectId.Value);
        }

        var runs = new List<ArenaRun>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            runs.Add(new ArenaRun(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                ParseTimestamp(reader.GetString(7))));
        }

        return runs;
    }

    public IReadOnlyList<ArenaRating> ListArenaRatings(long arenaRunId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                arena_run_id,
                photo_id,
                provider,
                model,
                photo_type,
                score,
                category,
                criteria_json,
                reason,
                prompt,
                request_json_redacted,
                raw_message_content,
                raw_response_json,
                http_status,
                error,
                created_at
            FROM arena_ratings
            WHERE arena_run_id = $arena_run_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$arena_run_id", arenaRunId);

        var ratings = new List<ArenaRating>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ratings.Add(new ArenaRating(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                ParseTimestamp(reader.GetString(16))));
        }

        return ratings;
    }

    public int ResetRatings(long projectId, bool includeAudit = false)
    {
        var photoIds = ListPhotos(projectId).Select(photo => photo.Id).ToArray();
        var deleted = 0;
        using var transaction = connection.BeginTransaction();

        foreach (var photoId in photoIds)
        {
            if (includeAudit)
            {
                using var deleteAuditCommand = connection.CreateCommand();
                deleteAuditCommand.Transaction = transaction;
                deleteAuditCommand.CommandText = "DELETE FROM rating_audit_logs WHERE photo_id = $photo_id;";
                deleteAuditCommand.Parameters.AddWithValue("$photo_id", photoId);
                deleteAuditCommand.ExecuteNonQuery();
            }

            using var deleteRatingCommand = connection.CreateCommand();
            deleteRatingCommand.Transaction = transaction;
            deleteRatingCommand.CommandText = "DELETE FROM ratings WHERE photo_id = $photo_id;";
            deleteRatingCommand.Parameters.AddWithValue("$photo_id", photoId);
            deleted += deleteRatingCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        EnqueueRatingJobs(projectId, force: true);
        return deleted;
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private static RatingJob ReadRatingJob(SqliteDataReader reader)
    {
        return new RatingJob(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            ParseTimestamp(reader.GetString(6)),
            ParseTimestamp(reader.GetString(7)));
    }

    private static (long? Size, string? ModifiedAt) GetFileFingerprint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (null, null);
        }

        var file = new FileInfo(path);
        return (file.Length, FormatTimestamp(file.LastWriteTimeUtc));
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static string? FormatNullableTimestamp(DateTimeOffset? value)
    {
        return value is null ? null : FormatTimestamp(value.Value);
    }

    private static bool IsUserDecision(string decision)
    {
        return decision is "unreviewed" or "keep" or "maybe" or "reject";
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value);
    }
}
