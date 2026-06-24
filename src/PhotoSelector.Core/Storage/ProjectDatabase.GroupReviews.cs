using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public long SaveGroupReview(
        long projectId,
        string groupId,
        string groupType,
        string groupKey,
        string groupReason,
        long winnerPhotoId,
        string winnerBaseName,
        string reason,
        string provider,
        string model,
        string prompt,
        IEnumerable<GroupReviewItemSnapshot> items)
    {
        EnsureProjectExists(projectId);
        EnsurePhotoExists(winnerPhotoId);

        using var transaction = database.BeginTransaction();
        var groupReviewId = database.InsertWithInt64Identity(new GroupReviewRow
        {
            ProjectId = projectId,
            GroupId = groupId,
            GroupType = groupType,
            GroupKey = groupKey,
            GroupReason = groupReason,
            WinnerPhotoId = winnerPhotoId,
            WinnerBaseName = winnerBaseName,
            Reason = reason,
            Provider = provider,
            Model = model,
            Prompt = prompt,
            CreatedAt = FormatTimestamp(DateTimeOffset.UtcNow),
        });

        foreach (var item in items)
        {
            EnsurePhotoExists(item.PhotoId);
            database.Insert(new GroupReviewItemRow
            {
                GroupReviewId = groupReviewId,
                PhotoId = item.PhotoId,
                BaseName = item.BaseName,
                JpegPath = item.JpegPath,
                RawPath = item.RawPath,
                CaptureTime = FormatNullableTimestamp(item.CaptureTime),
                ImportStatus = item.ImportStatus,
                JpegSize = item.JpegSize,
                JpegModifiedAt = FormatNullableTimestamp(item.JpegModifiedAt),
                RawSize = item.RawSize,
                RawModifiedAt = FormatNullableTimestamp(item.RawModifiedAt),
                OrderIndex = item.Order,
                SequenceNumber = item.SequenceNumber,
            });
        }

        transaction.Commit();
        return groupReviewId;
    }

    public IReadOnlyList<GroupReview> ListGroupReviews(long? projectId = null)
    {
        var query = projectId is null ? GroupReviews : GroupReviews.Where(review => review.ProjectId == projectId.Value);
        return query
            .OrderBy(review => review.Id)
            .AsEnumerable()
            .Select(ToGroupReview)
            .ToArray();
    }

    public IReadOnlyList<GroupReviewItem> ListGroupReviewItems(long groupReviewId)
    {
        EnsureGroupReviewExists(groupReviewId);

        return GroupReviewItems
            .Where(item => item.GroupReviewId == groupReviewId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .AsEnumerable()
            .Select(ToGroupReviewItem)
            .ToArray();
    }

    private static GroupReview ToGroupReview(GroupReviewRow row)
    {
        return new GroupReview(
            row.Id,
            row.ProjectId,
            row.GroupId,
            row.GroupType,
            row.GroupKey,
            row.GroupReason,
            row.WinnerPhotoId,
            row.WinnerBaseName,
            row.Reason,
            row.Provider,
            row.Model,
            row.Prompt,
            ParseTimestamp(row.CreatedAt));
    }

    private static GroupReviewItem ToGroupReviewItem(GroupReviewItemRow row)
    {
        return new GroupReviewItem(
            row.Id,
            row.GroupReviewId,
            row.PhotoId,
            row.BaseName,
            row.JpegPath,
            row.RawPath,
            row.CaptureTime is null ? null : ParseTimestamp(row.CaptureTime),
            row.ImportStatus,
            row.JpegSize,
            row.JpegModifiedAt is null ? null : ParseTimestamp(row.JpegModifiedAt),
            row.RawSize,
            row.RawModifiedAt is null ? null : ParseTimestamp(row.RawModifiedAt),
            row.OrderIndex,
            row.SequenceNumber);
    }
}
