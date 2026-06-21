using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using PhotoSelector.Core.Metadata;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Core.Storage;

public sealed class ProjectDatabase : IDisposable
{
    private readonly DataConnection database;

    private ProjectDatabase(DataConnection database)
    {
        this.database = database;
    }

    private ITable<SchemaVersionRow> SchemaVersions => database.GetTable<SchemaVersionRow>();

    private ITable<ProjectRow> Projects => database.GetTable<ProjectRow>();

    private ITable<PhotoRow> Photos => database.GetTable<PhotoRow>();

    private ITable<RatingRow> Ratings => database.GetTable<RatingRow>();

    private ITable<RatingJobRow> RatingJobs => database.GetTable<RatingJobRow>();

    private ITable<RatingAuditLogRow> RatingAuditLogs => database.GetTable<RatingAuditLogRow>();

    private ITable<ArenaRunRow> ArenaRuns => database.GetTable<ArenaRunRow>();

    private ITable<ArenaRatingRow> ArenaRatings => database.GetTable<ArenaRatingRow>();

    private ITable<UserMarkRow> UserMarks => database.GetTable<UserMarkRow>();

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

        var options = new DataOptions().UseSQLite(builder.ToString(), SQLiteProvider.Microsoft);
        return new ProjectDatabase(new DataConnection(options));
    }

    public void Migrate()
    {
        NormalizeSchemaVersionTable();

        database.CreateTable<SchemaVersionRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ProjectRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<PhotoRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingJobRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingAuditLogRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ArenaRunRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ArenaRatingRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<UserMarkRow>(tableOptions: TableOptions.CreateIfNotExists);

        if (!SchemaVersions.Any(row => row.Id == 1))
        {
            database.Insert(new SchemaVersionRow { Id = 1, Version = 1 });
        }
    }

    private void NormalizeSchemaVersionTable()
    {
        if (!TableExists("schema_version") || TableHasColumn("schema_version", "id"))
        {
            return;
        }

        database.DropTable<SchemaVersionRow>(throwExceptionIfNotExists: false);
        database.CreateTable<SchemaVersionRow>(tableOptions: TableOptions.CreateIfNotExists);
    }

    private bool TableExists(string tableName)
    {
        var schema = database.DataProvider.GetSchemaProvider().GetSchema(database);
        return schema.Tables.Any(table => table.TableName == tableName);
    }

    private bool TableHasColumn(string tableName, string columnName)
    {
        var schema = database.DataProvider.GetSchemaProvider().GetSchema(database);
        var table = schema.Tables.FirstOrDefault(item => item.TableName == tableName);
        return table?.Columns.Any(column => column.ColumnName == columnName) == true;
    }

    public long CreateProject(string sourceDirectory)
    {
        var now = FormatTimestamp(DateTimeOffset.UtcNow);
        return database.InsertWithInt64Identity(new ProjectRow
        {
            SourceDirectory = sourceDirectory,
            CreatedAt = now,
            LastOpenedAt = now,
        });
    }

    public void ReplacePhotos(long projectId, IEnumerable<PhotoPair> pairs)
    {
        if (!Projects.Any(project => project.Id == projectId))
        {
            ThrowSqliteForeignKey();
        }

        var pairList = pairs.ToArray();
        using var transaction = database.BeginTransaction();
        var now = FormatTimestamp(DateTimeOffset.UtcNow);

        var importedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairList)
        {
            importedBaseNames.Add(pair.BaseName);
            var jpegFingerprint = GetFileFingerprint(pair.JpegPath);
            var rawFingerprint = GetFileFingerprint(pair.RawPath);
            var captureTime = PhotoMetadataReader.ReadCaptureTime(pair.JpegPath);
            var existing = Photos
                .Where(photo => photo.ProjectId == projectId)
                .AsEnumerable()
                .Where(photo => string.Equals(photo.BaseName, pair.BaseName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(photo => photo.Id)
                .FirstOrDefault();

            if (existing is null)
            {
                database.Insert(new PhotoRow
                {
                    ProjectId = projectId,
                    BaseName = pair.BaseName,
                    JpegPath = pair.JpegPath,
                    RawPath = pair.RawPath,
                    CaptureTime = FormatNullableTimestamp(captureTime),
                    ImportStatus = "imported",
                    JpegSize = jpegFingerprint.Size,
                    JpegModifiedAt = jpegFingerprint.ModifiedAt,
                    RawSize = rawFingerprint.Size,
                    RawModifiedAt = rawFingerprint.ModifiedAt,
                });
                continue;
            }

            var hasExistingFingerprint = existing.JpegSize is not null ||
                existing.JpegModifiedAt is not null ||
                existing.RawSize is not null ||
                existing.RawModifiedAt is not null;
            var pathChanged = !string.Equals(existing.JpegPath, pair.JpegPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.RawPath, pair.RawPath, StringComparison.OrdinalIgnoreCase);
            var fingerprintChanged = hasExistingFingerprint &&
                (!Nullable.Equals(existing.JpegSize, jpegFingerprint.Size) ||
                    existing.JpegModifiedAt != jpegFingerprint.ModifiedAt ||
                    !Nullable.Equals(existing.RawSize, rawFingerprint.Size) ||
                    existing.RawModifiedAt != rawFingerprint.ModifiedAt);
            var changed = pathChanged || fingerprintChanged;

            Photos
                .Where(photo => photo.Id == existing.Id)
                .Set(photo => photo.BaseName, pair.BaseName)
                .Set(photo => photo.JpegPath, pair.JpegPath)
                .Set(photo => photo.RawPath, pair.RawPath)
                .Set(photo => photo.CaptureTime, FormatNullableTimestamp(captureTime))
                .Set(photo => photo.ImportStatus, changed ? "changed" : "imported")
                .Set(photo => photo.JpegSize, jpegFingerprint.Size)
                .Set(photo => photo.JpegModifiedAt, jpegFingerprint.ModifiedAt)
                .Set(photo => photo.RawSize, rawFingerprint.Size)
                .Set(photo => photo.RawModifiedAt, rawFingerprint.ModifiedAt)
                .Update();

            if (changed && !string.IsNullOrWhiteSpace(pair.JpegPath))
            {
                RequeueRatingJob(projectId, existing.Id, now);
            }
        }

        var stalePhotoIds = Photos
            .Where(photo => photo.ProjectId == projectId)
            .AsEnumerable()
            .Where(photo => !importedBaseNames.Contains(photo.BaseName))
            .Select(photo => photo.Id)
            .ToArray();

        foreach (var stalePhotoId in stalePhotoIds)
        {
            DeletePhotoCascade(stalePhotoId);
        }

        transaction.Commit();
    }

    public IReadOnlyList<PhotoProject> ListProjects()
    {
        return Projects
            .OrderBy(project => project.Id)
            .AsEnumerable()
            .Select(ToProject)
            .ToArray();
    }

    public IReadOnlyList<PhotoItem> ListPhotos(long projectId)
    {
        return Photos
            .Where(photo => photo.ProjectId == projectId)
            .AsEnumerable()
            .OrderBy(photo => photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(photo => photo.BaseName, StringComparer.Ordinal)
            .ThenBy(photo => photo.Id)
            .Select(ToPhoto)
            .ToArray();
    }

    public PhotoItem? GetPhoto(long photoId)
    {
        var photo = Photos.FirstOrDefault(item => item.Id == photoId);
        return photo is null ? null : ToPhoto(photo);
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

            var existing = RatingJobs.FirstOrDefault(job => job.PhotoId == photo.Id);
            if (existing is null)
            {
                database.Insert(new RatingJobRow
                {
                    ProjectId = projectId,
                    PhotoId = photo.Id,
                    Status = "pending",
                    Attempts = 0,
                    LastError = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                enqueued++;
                continue;
            }

            if (!force)
            {
                continue;
            }

            UpdateRatingJobForRetry(existing.Id, now);
            enqueued++;
        }

        return enqueued;
    }

    private void RequeueRatingJob(long projectId, long photoId, string updatedAt)
    {
        var existing = RatingJobs.FirstOrDefault(job => job.PhotoId == photoId);
        if (existing is null)
        {
            database.Insert(new RatingJobRow
            {
                ProjectId = projectId,
                PhotoId = photoId,
                Status = "pending",
                Attempts = 0,
                LastError = null,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
            });
            return;
        }

        UpdateRatingJobForRetry(existing.Id, updatedAt);
    }

    private void UpdateRatingJobForRetry(long jobId, string updatedAt)
    {
        RatingJobs
            .Where(job => job.Id == jobId)
            .Set(job => job.Status, "pending")
            .Set(job => job.Attempts, 0)
            .Set(job => job.LastError, (string?)null)
            .Set(job => job.UpdatedAt, updatedAt)
            .Update();
    }

    public IReadOnlyList<RatingJob> ListRatingJobs(long? projectId = null)
    {
        var query = projectId is null ? RatingJobs : RatingJobs.Where(job => job.ProjectId == projectId.Value);
        return query
            .OrderBy(job => job.Id)
            .AsEnumerable()
            .Select(ToRatingJob)
            .ToArray();
    }

    public IReadOnlyList<RatingJob> ListPendingRatingJobs(long? projectId = null)
    {
        var query = RatingJobs.Where(job => job.Status == "pending");
        if (projectId is not null)
        {
            query = query.Where(job => job.ProjectId == projectId.Value);
        }

        return query
            .OrderBy(job => job.Id)
            .AsEnumerable()
            .Select(ToRatingJob)
            .ToArray();
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
        var existing = RatingJobs.FirstOrDefault(job => job.Id == jobId);
        if (existing is null)
        {
            return;
        }

        RatingJobs
            .Where(job => job.Id == jobId)
            .Set(job => job.Status, status)
            .Set(job => job.Attempts, existing.Attempts + 1)
            .Set(job => job.LastError, error)
            .Set(job => job.UpdatedAt, FormatTimestamp(DateTimeOffset.UtcNow))
            .Update();
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
        EnsurePhotoExists(photoId);

        return database.InsertWithInt64Identity(new RatingRow
        {
            PhotoId = photoId,
            Provider = provider,
            Model = model,
            PhotoType = photoType,
            Score = score,
            Category = category,
            CriteriaJson = criteriaJson,
            Reason = reason,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });
    }

    public void SaveUserMark(long photoId, string decision, int stars, string? note)
    {
        EnsurePhotoExists(photoId);

        if (!IsUserDecision(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), "Decision must be unreviewed, keep, maybe, or reject.");
        }

        if (stars is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(stars), "Stars must be between 0 and 5.");
        }

        var updatedAt = FormatTimestamp(DateTimeOffset.UtcNow);
        var existing = UserMarks.FirstOrDefault(mark => mark.PhotoId == photoId);
        if (existing is null)
        {
            database.Insert(new UserMarkRow
            {
                PhotoId = photoId,
                Decision = decision,
                Stars = stars,
                Note = note ?? string.Empty,
                UpdatedAt = updatedAt,
            });
            return;
        }

        UserMarks
            .Where(mark => mark.Id == existing.Id)
            .Set(mark => mark.Decision, decision)
            .Set(mark => mark.Stars, stars)
            .Set(mark => mark.Note, note ?? string.Empty)
            .Set(mark => mark.UpdatedAt, updatedAt)
            .Update();
    }

    public PhotoUserMark? GetUserMark(long photoId)
    {
        var mark = UserMarks.FirstOrDefault(item => item.PhotoId == photoId);
        return mark is null ? null : ToUserMark(mark);
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
        EnsurePhotoExists(photoId);
        if (ratingId is not null)
        {
            EnsureRatingExists(ratingId.Value);
        }

        return database.InsertWithInt64Identity(new RatingAuditLogRow
        {
            PhotoId = photoId,
            RatingId = ratingId,
            Provider = provider,
            Model = model,
            Prompt = prompt,
            RequestJsonRedacted = requestJsonRedacted,
            RawMessageContent = rawMessageContent,
            RawResponseJson = rawResponseJson,
            HttpStatus = httpStatus,
            Error = error,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });
    }

    public IReadOnlyList<PhotoRating> ListRatings(long photoId)
    {
        return Ratings
            .Where(rating => rating.PhotoId == photoId)
            .OrderByDescending(rating => rating.CreatedAt)
            .ThenByDescending(rating => rating.Id)
            .AsEnumerable()
            .Select(ToRating)
            .ToArray();
    }

    public IReadOnlyList<PhotoRatingAuditLog> ListRatingAuditLogs(long photoId)
    {
        return RatingAuditLogs
            .Where(log => log.PhotoId == photoId)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .AsEnumerable()
            .Select(ToRatingAuditLog)
            .ToArray();
    }

    public long CreateArenaRun(
        long projectId,
        string provider,
        string modelsCsv,
        string prompt,
        string outputLanguage,
        int limit)
    {
        EnsureProjectExists(projectId);

        return database.InsertWithInt64Identity(new ArenaRunRow
        {
            ProjectId = projectId,
            Provider = provider,
            ModelsCsv = modelsCsv,
            Prompt = prompt,
            OutputLanguage = outputLanguage,
            LimitCount = limit,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });
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
        EnsureArenaRunExists(arenaRunId);
        EnsurePhotoExists(photoId);

        return database.InsertWithInt64Identity(new ArenaRatingRow
        {
            ArenaRunId = arenaRunId,
            PhotoId = photoId,
            Provider = provider,
            Model = model,
            PhotoType = photoType,
            Score = score,
            Category = category,
            CriteriaJson = criteriaJson,
            Reason = reason,
            Prompt = prompt,
            RequestJsonRedacted = requestJsonRedacted,
            RawMessageContent = rawMessageContent,
            RawResponseJson = rawResponseJson,
            HttpStatus = httpStatus,
            Error = error,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });
    }

    public IReadOnlyList<ArenaRun> ListArenaRuns(long? projectId = null)
    {
        var query = projectId is null ? ArenaRuns : ArenaRuns.Where(run => run.ProjectId == projectId.Value);
        return query
            .OrderBy(run => run.Id)
            .AsEnumerable()
            .Select(ToArenaRun)
            .ToArray();
    }

    public IReadOnlyList<ArenaRating> ListArenaRatings(long arenaRunId)
    {
        return ArenaRatings
            .Where(rating => rating.ArenaRunId == arenaRunId)
            .OrderBy(rating => rating.Id)
            .AsEnumerable()
            .Select(ToArenaRating)
            .ToArray();
    }

    public int ResetRatings(long projectId, bool includeAudit = false)
    {
        var photoIds = ListPhotos(projectId).Select(photo => photo.Id).ToArray();
        var deleted = 0;
        using var transaction = database.BeginTransaction();

        foreach (var photoId in photoIds)
        {
            if (includeAudit)
            {
                RatingAuditLogs.Where(log => log.PhotoId == photoId).Delete();
            }
            else
            {
                RatingAuditLogs
                    .Where(log => log.PhotoId == photoId)
                    .Set(log => log.RatingId, (long?)null)
                    .Update();
            }

            deleted += Ratings.Where(rating => rating.PhotoId == photoId).Delete();
        }

        transaction.Commit();
        EnqueueRatingJobs(projectId, force: true);
        return deleted;
    }

    public void Dispose()
    {
        database.Dispose();
    }

    private void DeletePhotoCascade(long photoId)
    {
        UserMarks.Where(mark => mark.PhotoId == photoId).Delete();
        RatingAuditLogs.Where(log => log.PhotoId == photoId).Delete();
        Ratings.Where(rating => rating.PhotoId == photoId).Delete();
        RatingJobs.Where(job => job.PhotoId == photoId).Delete();
        ArenaRatings.Where(rating => rating.PhotoId == photoId).Delete();
        Photos.Where(photo => photo.Id == photoId).Delete();
    }

    private void EnsureProjectExists(long projectId)
    {
        if (!Projects.Any(project => project.Id == projectId))
        {
            ThrowSqliteForeignKey();
        }
    }

    private void EnsurePhotoExists(long photoId)
    {
        if (!Photos.Any(photo => photo.Id == photoId))
        {
            ThrowSqliteForeignKey();
        }
    }

    private void EnsureRatingExists(long ratingId)
    {
        if (!Ratings.Any(rating => rating.Id == ratingId))
        {
            ThrowSqliteForeignKey();
        }
    }

    private void EnsureArenaRunExists(long arenaRunId)
    {
        if (!ArenaRuns.Any(run => run.Id == arenaRunId))
        {
            ThrowSqliteForeignKey();
        }
    }

    private static void ThrowSqliteForeignKey()
    {
        throw new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787);
    }

    private static PhotoProject ToProject(ProjectRow row)
    {
        return new PhotoProject(row.Id, row.SourceDirectory, ParseTimestamp(row.CreatedAt), ParseTimestamp(row.LastOpenedAt));
    }

    private static PhotoItem ToPhoto(PhotoRow row)
    {
        return new PhotoItem(
            row.Id,
            row.ProjectId,
            row.BaseName,
            row.JpegPath,
            row.RawPath,
            row.CaptureTime is null ? null : ParseTimestamp(row.CaptureTime),
            row.ImportStatus,
            row.JpegSize,
            row.JpegModifiedAt is null ? null : ParseTimestamp(row.JpegModifiedAt),
            row.RawSize,
            row.RawModifiedAt is null ? null : ParseTimestamp(row.RawModifiedAt));
    }

    private static RatingJob ToRatingJob(RatingJobRow row)
    {
        return new RatingJob(
            row.Id,
            row.ProjectId,
            row.PhotoId,
            row.Status,
            row.Attempts,
            row.LastError,
            ParseTimestamp(row.CreatedAt),
            ParseTimestamp(row.UpdatedAt));
    }

    private static PhotoRating ToRating(RatingRow row)
    {
        return new PhotoRating(
            row.Id,
            row.PhotoId,
            row.Provider,
            row.Model,
            row.PhotoType,
            row.Score,
            row.Category,
            row.CriteriaJson,
            row.Reason,
            ParseTimestamp(row.CreatedAt));
    }

    private static PhotoUserMark ToUserMark(UserMarkRow row)
    {
        return new PhotoUserMark(row.Id, row.PhotoId, row.Decision, row.Stars, row.Note, ParseTimestamp(row.UpdatedAt));
    }

    private static PhotoRatingAuditLog ToRatingAuditLog(RatingAuditLogRow row)
    {
        return new PhotoRatingAuditLog(
            row.Id,
            row.PhotoId,
            row.RatingId,
            row.Provider,
            row.Model,
            row.Prompt,
            row.RequestJsonRedacted,
            row.RawMessageContent,
            row.RawResponseJson,
            row.HttpStatus,
            row.Error,
            ParseTimestamp(row.CreatedAt));
    }

    private static ArenaRun ToArenaRun(ArenaRunRow row)
    {
        return new ArenaRun(
            row.Id,
            row.ProjectId,
            row.Provider,
            row.ModelsCsv,
            row.Prompt,
            row.OutputLanguage,
            row.LimitCount,
            ParseTimestamp(row.CreatedAt));
    }

    private static ArenaRating ToArenaRating(ArenaRatingRow row)
    {
        return new ArenaRating(
            row.Id,
            row.ArenaRunId,
            row.PhotoId,
            row.Provider,
            row.Model,
            row.PhotoType,
            row.Score,
            row.Category,
            row.CriteriaJson,
            row.Reason,
            row.Prompt,
            row.RequestJsonRedacted,
            row.RawMessageContent,
            row.RawResponseJson,
            row.HttpStatus,
            row.Error,
            ParseTimestamp(row.CreatedAt));
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

    [Table(Name = "schema_version")]
    private sealed class SchemaVersionRow
    {
        [Column(Name = "id"), PrimaryKey]
        public int Id { get; set; }

        [Column(Name = "version"), NotNull]
        public int Version { get; set; }
    }

    [Table(Name = "projects")]
    private sealed class ProjectRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "source_directory"), NotNull]
        public string SourceDirectory { get; set; } = string.Empty;

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;

        [Column(Name = "last_opened_at"), NotNull]
        public string LastOpenedAt { get; set; } = string.Empty;
    }

    [Table(Name = "photos")]
    private sealed class PhotoRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "project_id"), NotNull]
        public long ProjectId { get; set; }

        [Column(Name = "base_name"), NotNull]
        public string BaseName { get; set; } = string.Empty;

        [Column(Name = "jpeg_path"), Nullable]
        public string? JpegPath { get; set; }

        [Column(Name = "raw_path"), Nullable]
        public string? RawPath { get; set; }

        [Column(Name = "capture_time"), Nullable]
        public string? CaptureTime { get; set; }

        [Column(Name = "import_status"), NotNull]
        public string ImportStatus { get; set; } = string.Empty;

        [Column(Name = "jpeg_size"), Nullable]
        public long? JpegSize { get; set; }

        [Column(Name = "jpeg_mtime_utc"), Nullable]
        public string? JpegModifiedAt { get; set; }

        [Column(Name = "raw_size"), Nullable]
        public long? RawSize { get; set; }

        [Column(Name = "raw_mtime_utc"), Nullable]
        public string? RawModifiedAt { get; set; }
    }

    [Table(Name = "ratings")]
    private sealed class RatingRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

        [Column(Name = "provider"), NotNull]
        public string Provider { get; set; } = string.Empty;

        [Column(Name = "model"), NotNull]
        public string Model { get; set; } = string.Empty;

        [Column(Name = "photo_type"), NotNull]
        public string PhotoType { get; set; } = "unknown";

        [Column(Name = "score"), NotNull]
        public double Score { get; set; }

        [Column(Name = "category"), NotNull]
        public string Category { get; set; } = string.Empty;

        [Column(Name = "criteria_json"), NotNull]
        public string CriteriaJson { get; set; } = "[]";

        [Column(Name = "reason"), NotNull]
        public string Reason { get; set; } = string.Empty;

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;
    }

    [Table(Name = "rating_jobs")]
    private sealed class RatingJobRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "project_id"), NotNull]
        public long ProjectId { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

        [Column(Name = "status"), NotNull]
        public string Status { get; set; } = string.Empty;

        [Column(Name = "attempts"), NotNull]
        public int Attempts { get; set; }

        [Column(Name = "last_error"), Nullable]
        public string? LastError { get; set; }

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;

        [Column(Name = "updated_at"), NotNull]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    [Table(Name = "rating_audit_logs")]
    private sealed class RatingAuditLogRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

        [Column(Name = "rating_id"), Nullable]
        public long? RatingId { get; set; }

        [Column(Name = "provider"), NotNull]
        public string Provider { get; set; } = string.Empty;

        [Column(Name = "model"), NotNull]
        public string Model { get; set; } = string.Empty;

        [Column(Name = "prompt"), NotNull]
        public string Prompt { get; set; } = string.Empty;

        [Column(Name = "request_json_redacted"), NotNull]
        public string RequestJsonRedacted { get; set; } = string.Empty;

        [Column(Name = "raw_message_content"), NotNull]
        public string RawMessageContent { get; set; } = string.Empty;

        [Column(Name = "raw_response_json"), NotNull]
        public string RawResponseJson { get; set; } = string.Empty;

        [Column(Name = "http_status"), Nullable]
        public int? HttpStatus { get; set; }

        [Column(Name = "error"), Nullable]
        public string? Error { get; set; }

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;
    }

    [Table(Name = "arena_runs")]
    private sealed class ArenaRunRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "project_id"), NotNull]
        public long ProjectId { get; set; }

        [Column(Name = "provider"), NotNull]
        public string Provider { get; set; } = string.Empty;

        [Column(Name = "models_csv"), NotNull]
        public string ModelsCsv { get; set; } = string.Empty;

        [Column(Name = "prompt"), NotNull]
        public string Prompt { get; set; } = string.Empty;

        [Column(Name = "output_language"), NotNull]
        public string OutputLanguage { get; set; } = string.Empty;

        [Column(Name = "limit_count"), NotNull]
        public int LimitCount { get; set; }

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;
    }

    [Table(Name = "arena_ratings")]
    private sealed class ArenaRatingRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "arena_run_id"), NotNull]
        public long ArenaRunId { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

        [Column(Name = "provider"), NotNull]
        public string Provider { get; set; } = string.Empty;

        [Column(Name = "model"), NotNull]
        public string Model { get; set; } = string.Empty;

        [Column(Name = "photo_type"), Nullable]
        public string? PhotoType { get; set; }

        [Column(Name = "score"), Nullable]
        public double? Score { get; set; }

        [Column(Name = "category"), Nullable]
        public string? Category { get; set; }

        [Column(Name = "criteria_json"), NotNull]
        public string CriteriaJson { get; set; } = "[]";

        [Column(Name = "reason"), NotNull]
        public string Reason { get; set; } = string.Empty;

        [Column(Name = "prompt"), NotNull]
        public string Prompt { get; set; } = string.Empty;

        [Column(Name = "request_json_redacted"), NotNull]
        public string RequestJsonRedacted { get; set; } = string.Empty;

        [Column(Name = "raw_message_content"), NotNull]
        public string RawMessageContent { get; set; } = string.Empty;

        [Column(Name = "raw_response_json"), NotNull]
        public string RawResponseJson { get; set; } = string.Empty;

        [Column(Name = "http_status"), Nullable]
        public int? HttpStatus { get; set; }

        [Column(Name = "error"), Nullable]
        public string? Error { get; set; }

        [Column(Name = "created_at"), NotNull]
        public string CreatedAt { get; set; } = string.Empty;
    }

    [Table(Name = "user_marks")]
    private sealed class UserMarkRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

        [Column(Name = "decision"), NotNull]
        public string Decision { get; set; } = string.Empty;

        [Column(Name = "stars"), NotNull]
        public int Stars { get; set; }

        [Column(Name = "note"), NotNull]
        public string Note { get; set; } = string.Empty;

        [Column(Name = "updated_at"), NotNull]
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
