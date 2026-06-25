using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public long SaveShootReview(
        long projectId,
        string summaryText,
        string summaryJson,
        string topCandidatesJson,
        string groupReviewsJson,
        string weakPatternsJson,
        string nextShootNotesJson)
    {
        EnsureProjectExists(projectId);

        return database.InsertWithInt64Identity(new ShootReviewRow
        {
            ProjectId = projectId,
            SummaryText = summaryText,
            SummaryJson = summaryJson,
            TopCandidatesJson = topCandidatesJson,
            GroupReviewsJson = groupReviewsJson,
            WeakPatternsJson = weakPatternsJson,
            NextShootNotesJson = nextShootNotesJson,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });
    }

    public IReadOnlyList<ShootReview> ListShootReviews(long? projectId = null)
    {
        var query = projectId is null ? ShootReviews : ShootReviews.Where(review => review.ProjectId == projectId.Value);
        return query
            .OrderBy(review => review.Id)
            .AsEnumerable()
            .Select(ToShootReview)
            .ToArray();
    }

    private static ShootReview ToShootReview(ShootReviewRow row)
    {
        return new ShootReview(
            row.Id,
            row.ProjectId,
            row.SummaryText,
            row.SummaryJson,
            row.TopCandidatesJson,
            row.GroupReviewsJson,
            row.WeakPatternsJson,
            row.NextShootNotesJson,
            ParseTimestamp(row.CreatedAt));
    }
}
