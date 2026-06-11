using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Config;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Agent.Workers;

public sealed class RatingWorker(IPhotoRatingClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RatingWorkerResult ProcessPending(
        ProjectDatabase database,
        IEnumerable<RatingJob> jobs,
        RatingWorkerOptions options,
        Action<RatingWorkerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var rated = 0;
        var skipped = 0;
        var failed = 0;
        var jobList = jobs.ToArray();
        var total = jobList.Length;
        var current = 0;

        foreach (var job in jobList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;

            var photo = database.GetPhoto(job.PhotoId);
            progress?.Invoke(new RatingWorkerProgress(current, total, photo?.BaseName ?? $"photo:{job.PhotoId}"));
            if (photo is null)
            {
                skipped++;
                database.MarkRatingJobFailed(job.Id, "Photo not found.");
                continue;
            }

            if (!options.Force &&
                !string.Equals(photo.ImportStatus, "changed", StringComparison.OrdinalIgnoreCase) &&
                database.ListRatings(photo.Id).Count > 0)
            {
                skipped++;
                database.MarkRatingJobCompleted(job.Id);
                continue;
            }

            if (string.IsNullOrWhiteSpace(photo.JpegPath) || !File.Exists(photo.JpegPath))
            {
                skipped++;
                database.MarkRatingJobFailed(job.Id, "JPEG file not found.");
                continue;
            }

            try
            {
                var result = client
                    .RatePhotoAsync(
                        new PhotoRatingRequest(
                            options.BaseUrl,
                            options.ApiKey,
                            options.Profile.Model,
                            options.Prompt,
                            photo.JpegPath),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                var rating = result.Rating;
                if (rating is null)
                {
                    database.SaveRatingAuditLog(
                        photo.Id,
                        null,
                        options.Profile.Provider,
                        options.Profile.Model,
                        result.Audit.Prompt,
                        result.Audit.RequestJsonRedacted,
                        result.Audit.RawMessageContent,
                        result.Audit.RawResponseJson,
                        result.Audit.HttpStatus,
                        result.Audit.Error);
                    failed++;
                    database.MarkRatingJobFailed(job.Id, result.Audit.Error ?? "AI rating response could not be parsed.");
                    messages.Add($"{photo.BaseName}: {result.Audit.Error ?? "AI rating response could not be parsed."}");
                    continue;
                }

                var criteriaJson = JsonSerializer.Serialize(rating.Criteria, JsonOptions);
                var ratingId = database.SaveRating(
                    photo.Id,
                    options.Profile.Provider,
                    options.Profile.Model,
                    rating.PhotoType,
                    rating.Score,
                    rating.Category,
                    criteriaJson,
                    rating.Reason);
                database.SaveRatingAuditLog(
                    photo.Id,
                    ratingId,
                    options.Profile.Provider,
                    options.Profile.Model,
                    result.Audit.Prompt,
                    result.Audit.RequestJsonRedacted,
                    result.Audit.RawMessageContent,
                    result.Audit.RawResponseJson,
                    result.Audit.HttpStatus,
                    result.Audit.Error);
                database.MarkRatingJobCompleted(job.Id);
                rated++;
                messages.Add($"{photo.BaseName}: {rating.Score} {rating.Category} - {rating.Reason}");
            }
            catch (Exception ex)
            {
                failed++;
                database.MarkRatingJobFailed(job.Id, ex.Message);
                messages.Add($"{photo.BaseName}: {ex.Message}");
            }
        }

        return new RatingWorkerResult(rated, skipped, failed, messages);
    }
}

public sealed record RatingWorkerOptions(
    Uri BaseUrl,
    string ApiKey,
    AiProfile Profile,
    string Prompt,
    bool Force);

public sealed record RatingWorkerResult(
    int Rated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Messages);

public sealed record RatingWorkerProgress(
    int Current,
    int Total,
    string Label);
