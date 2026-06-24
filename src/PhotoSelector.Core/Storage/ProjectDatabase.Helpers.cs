using Microsoft.Data.Sqlite;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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

    private void EnsureGroupReviewExists(long groupReviewId)
    {
        if (!GroupReviews.Any(review => review.Id == groupReviewId))
        {
            ThrowSqliteForeignKey();
        }
    }

    private static void ThrowSqliteForeignKey()
    {
        throw new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static string? FormatNullableTimestamp(DateTimeOffset? value)
    {
        return value is null ? null : FormatTimestamp(value.Value);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value);
    }
}
