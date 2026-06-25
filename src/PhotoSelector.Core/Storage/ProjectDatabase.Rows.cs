using LinqToDB.Mapping;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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

    [Table(Name = "group_reviews")]
    private sealed class GroupReviewRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "project_id"), NotNull]
        public long ProjectId { get; set; }

        [Column(Name = "group_id"), NotNull]
        public string GroupId { get; set; } = string.Empty;

        [Column(Name = "group_type"), NotNull]
        public string GroupType { get; set; } = string.Empty;

        [Column(Name = "group_key"), NotNull]
        public string GroupKey { get; set; } = string.Empty;

        [Column(Name = "group_reason"), NotNull]
        public string GroupReason { get; set; } = string.Empty;

        [Column(Name = "winner_photo_id"), NotNull]
        public long WinnerPhotoId { get; set; }

        [Column(Name = "winner_base_name"), NotNull]
        public string WinnerBaseName { get; set; } = string.Empty;

        [Column(Name = "reason"), NotNull]
        public string Reason { get; set; } = string.Empty;

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

    [Table(Name = "group_review_items")]
    private sealed class GroupReviewItemRow
    {
        [Column(Name = "id"), PrimaryKey, Identity]
        public long Id { get; set; }

        [Column(Name = "group_review_id"), NotNull]
        public long GroupReviewId { get; set; }

        [Column(Name = "photo_id"), NotNull]
        public long PhotoId { get; set; }

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

        [Column(Name = "order_index"), NotNull]
        public int OrderIndex { get; set; }

        [Column(Name = "sequence_number"), NotNull]
        public long SequenceNumber { get; set; }
    }
}
