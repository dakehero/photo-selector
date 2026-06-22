using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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
}
