using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Config;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Agent.Workers;

public sealed class RatingWorker(IPhotoRatingClient client)
{
    public RatingWorkerResult ProcessPending(
        ProjectDatabase database,
        IEnumerable<RatingJob> jobs,
        RatingWorkerOptions options,
        Action<RatingWorkerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ProcessPendingAsync(database, jobs, options, progress, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    private async Task<RatingWorkerResult> ProcessPendingAsync(
        ProjectDatabase database,
        IEnumerable<RatingJob> jobs,
        RatingWorkerOptions options,
        Action<RatingWorkerProgress>? progress,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var rated = 0;
        var skipped = 0;
        var failed = 0;
        var jobList = jobs.ToArray();
        var total = jobList.Length;
        var completed = 0;
        var workItems = new List<RatingWorkItem>();
        var databaseLock = new object();
        var progressLock = new object();

        void ReportProgress(string label)
        {
            var current = Interlocked.Increment(ref completed);
            lock (progressLock)
            {
                progress?.Invoke(new RatingWorkerProgress(current, total, label));
            }
        }

        for (var index = 0; index < jobList.Length; index++)
        {
            var job = jobList[index];
            cancellationToken.ThrowIfCancellationRequested();

            var photo = database.GetPhoto(job.PhotoId);
            if (photo is null)
            {
                skipped++;
                database.MarkRatingJobFailed(job.Id, "Photo not found.");
                ReportProgress($"photo:{job.PhotoId}");
                continue;
            }

            if (!options.Force &&
                !string.Equals(photo.ImportStatus, "changed", StringComparison.OrdinalIgnoreCase) &&
                database.ListRatings(photo.Id).Count > 0)
            {
                skipped++;
                database.MarkRatingJobCompleted(job.Id);
                ReportProgress(photo.BaseName);
                continue;
            }

            if (string.IsNullOrWhiteSpace(photo.JpegPath) || !File.Exists(photo.JpegPath))
            {
                skipped++;
                database.MarkRatingJobFailed(job.Id, "JPEG file not found.");
                ReportProgress(photo.BaseName);
                continue;
            }

            workItems.Add(new RatingWorkItem(index, job, photo));
        }

        using var gate = new SemaphoreSlim(Math.Max(1, options.Concurrency));
        var orderedMessages = new string?[jobList.Length];
        var tasks = workItems.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await client.RatePhotoAsync(
                        new PhotoRatingRequest(
                            options.BaseUrl,
                            options.ApiKey,
                            options.Profile.Model,
                            options.Prompt,
                            item.Photo.JpegPath!,
                            options.Preview),
                        cancellationToken);
                var rating = result.Rating;

                lock (databaseLock)
                {
                    if (rating is null)
                    {
                        database.SaveRatingAuditLog(
                            item.Photo.Id,
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
                        database.MarkRatingJobFailed(item.Job.Id, result.Audit.Error ?? "AI rating response could not be parsed.");
                        orderedMessages[item.Index] = $"{item.Photo.BaseName}: {result.Audit.Error ?? "AI rating response could not be parsed."}";
                        return;
                    }

                    var criteriaJson = JsonSerializer.Serialize(rating.Criteria.ToArray(), RatingJsonContext.Default.AiRatingCriterionArray);
                    var ratingId = database.SaveRating(
                        item.Photo.Id,
                        options.Profile.Provider,
                        options.Profile.Model,
                        rating.PhotoType,
                        rating.Score,
                        rating.Category,
                        criteriaJson,
                        rating.Reason);
                    database.SaveRatingAuditLog(
                        item.Photo.Id,
                        ratingId,
                        options.Profile.Provider,
                        options.Profile.Model,
                        result.Audit.Prompt,
                        result.Audit.RequestJsonRedacted,
                        result.Audit.RawMessageContent,
                        result.Audit.RawResponseJson,
                        result.Audit.HttpStatus,
                        result.Audit.Error);
                    database.MarkRatingJobCompleted(item.Job.Id);
                    rated++;
                    orderedMessages[item.Index] = $"{item.Photo.BaseName}: {rating.Score} {rating.Category} - {rating.Reason}";
                }
            }
            catch (Exception ex)
            {
                lock (databaseLock)
                {
                    failed++;
                    database.MarkRatingJobFailed(item.Job.Id, ex.Message);
                    orderedMessages[item.Index] = $"{item.Photo.BaseName}: {ex.Message}";
                }
            }
            finally
            {
                gate.Release();
                ReportProgress(item.Photo.BaseName);
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        messages.AddRange(orderedMessages.Where(message => message is not null)!);

        return new RatingWorkerResult(rated, skipped, failed, messages);
    }

    private sealed record RatingWorkItem(int Index, RatingJob Job, PhotoItem Photo);
}

public sealed record RatingWorkerOptions(
    Uri BaseUrl,
    string ApiKey,
    AiProfile Profile,
    string Prompt,
    bool Force,
    int Concurrency = 1,
    PhotoPreviewOptions? Preview = null);

public sealed record RatingWorkerResult(
    int Rated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Messages);

public sealed record RatingWorkerProgress(
    int Current,
    int Total,
    string Label);
