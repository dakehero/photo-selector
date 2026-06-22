using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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
}
