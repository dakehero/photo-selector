using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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

    private static PhotoUserMark ToUserMark(UserMarkRow row)
    {
        return new PhotoUserMark(row.Id, row.PhotoId, row.Decision, row.Stars, row.Note, ParseTimestamp(row.UpdatedAt));
    }

    private static bool IsUserDecision(string decision)
    {
        return decision is "unreviewed" or "keep" or "maybe" or "reject";
    }
}
