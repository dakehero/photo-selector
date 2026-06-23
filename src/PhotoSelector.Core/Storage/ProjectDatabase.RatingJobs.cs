using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public int EnqueueRatingJobs(long projectId, bool force = false)
    {
        var now = FormatTimestamp(DateTimeOffset.UtcNow);
        var enqueued = 0;
        foreach (var photo in ListPhotos(projectId))
        {
            if (PhotoImportStatus.IsMissing(photo.ImportStatus))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(photo.JpegPath))
            {
                continue;
            }

            if (!force && ListRatings(photo.Id).Count > 0)
            {
                continue;
            }

            var existing = RatingJobs.FirstOrDefault(job => job.PhotoId == photo.Id);
            if (existing is null)
            {
                database.Insert(new RatingJobRow
                {
                    ProjectId = projectId,
                    PhotoId = photo.Id,
                    Status = "pending",
                    Attempts = 0,
                    LastError = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                enqueued++;
                continue;
            }

            if (!force)
            {
                continue;
            }

            UpdateRatingJobForRetry(existing.Id, now);
            enqueued++;
        }

        return enqueued;
    }

    private void RequeueRatingJob(long projectId, long photoId, string updatedAt)
    {
        var existing = RatingJobs.FirstOrDefault(job => job.PhotoId == photoId);
        if (existing is null)
        {
            database.Insert(new RatingJobRow
            {
                ProjectId = projectId,
                PhotoId = photoId,
                Status = "pending",
                Attempts = 0,
                LastError = null,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
            });
            return;
        }

        UpdateRatingJobForRetry(existing.Id, updatedAt);
    }

    private void UpdateRatingJobForRetry(long jobId, string updatedAt)
    {
        RatingJobs
            .Where(job => job.Id == jobId)
            .Set(job => job.Status, "pending")
            .Set(job => job.Attempts, 0)
            .Set(job => job.LastError, (string?)null)
            .Set(job => job.UpdatedAt, updatedAt)
            .Update();
    }

    public IReadOnlyList<RatingJob> ListRatingJobs(long? projectId = null)
    {
        var query = projectId is null ? RatingJobs : RatingJobs.Where(job => job.ProjectId == projectId.Value);
        return query
            .OrderBy(job => job.Id)
            .AsEnumerable()
            .Select(ToRatingJob)
            .ToArray();
    }

    public IReadOnlyList<RatingJob> ListPendingRatingJobs(long? projectId = null)
    {
        var query = RatingJobs.Where(job => job.Status == "pending");
        if (projectId is not null)
        {
            query = query.Where(job => job.ProjectId == projectId.Value);
        }

        return query
            .OrderBy(job => job.Id)
            .AsEnumerable()
            .Select(ToRatingJob)
            .ToArray();
    }

    public RatingJobSummary GetRatingJobSummary(long? projectId = null)
    {
        var jobs = ListRatingJobs(projectId);
        return new RatingJobSummary(
            jobs.Count,
            jobs.Count(job => job.Status == "pending"),
            jobs.Count(job => job.Status == "completed"),
            jobs.Count(job => job.Status == "failed"));
    }

    public void MarkRatingJobCompleted(long jobId)
    {
        UpdateRatingJob(jobId, "completed", null);
    }

    public void MarkRatingJobFailed(long jobId, string error)
    {
        UpdateRatingJob(jobId, "failed", error);
    }

    private void UpdateRatingJob(long jobId, string status, string? error)
    {
        var existing = RatingJobs.FirstOrDefault(job => job.Id == jobId);
        if (existing is null)
        {
            return;
        }

        RatingJobs
            .Where(job => job.Id == jobId)
            .Set(job => job.Status, status)
            .Set(job => job.Attempts, existing.Attempts + 1)
            .Set(job => job.LastError, error)
            .Set(job => job.UpdatedAt, FormatTimestamp(DateTimeOffset.UtcNow))
            .Update();
    }

    private static RatingJob ToRatingJob(RatingJobRow row)
    {
        return new RatingJob(
            row.Id,
            row.ProjectId,
            row.PhotoId,
            row.Status,
            row.Attempts,
            row.LastError,
            ParseTimestamp(row.CreatedAt),
            ParseTimestamp(row.UpdatedAt));
    }
}
