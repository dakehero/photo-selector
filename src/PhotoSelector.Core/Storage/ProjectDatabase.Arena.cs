using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
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
}
