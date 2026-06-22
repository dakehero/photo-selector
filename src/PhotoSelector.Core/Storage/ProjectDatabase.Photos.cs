using LinqToDB;
using PhotoSelector.Core.Metadata;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public void ReplacePhotos(long projectId, IEnumerable<PhotoPair> pairs)
    {
        if (!Projects.Any(project => project.Id == projectId))
        {
            ThrowSqliteForeignKey();
        }

        var pairList = pairs.ToArray();
        using var transaction = database.BeginTransaction();
        var now = FormatTimestamp(DateTimeOffset.UtcNow);

        var importedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairList)
        {
            importedBaseNames.Add(pair.BaseName);
            var jpegFingerprint = GetFileFingerprint(pair.JpegPath);
            var rawFingerprint = GetFileFingerprint(pair.RawPath);
            var captureTime = PhotoMetadataReader.ReadCaptureTime(pair.JpegPath);
            var existing = Photos
                .Where(photo => photo.ProjectId == projectId)
                .AsEnumerable()
                .Where(photo => string.Equals(photo.BaseName, pair.BaseName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(photo => photo.Id)
                .FirstOrDefault();

            if (existing is null)
            {
                database.Insert(new PhotoRow
                {
                    ProjectId = projectId,
                    BaseName = pair.BaseName,
                    JpegPath = pair.JpegPath,
                    RawPath = pair.RawPath,
                    CaptureTime = FormatNullableTimestamp(captureTime),
                    ImportStatus = "imported",
                    JpegSize = jpegFingerprint.Size,
                    JpegModifiedAt = jpegFingerprint.ModifiedAt,
                    RawSize = rawFingerprint.Size,
                    RawModifiedAt = rawFingerprint.ModifiedAt,
                });
                continue;
            }

            var hasExistingFingerprint = existing.JpegSize is not null ||
                existing.JpegModifiedAt is not null ||
                existing.RawSize is not null ||
                existing.RawModifiedAt is not null;
            var pathChanged = !string.Equals(existing.JpegPath, pair.JpegPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.RawPath, pair.RawPath, StringComparison.OrdinalIgnoreCase);
            var fingerprintChanged = hasExistingFingerprint &&
                (!Nullable.Equals(existing.JpegSize, jpegFingerprint.Size) ||
                    existing.JpegModifiedAt != jpegFingerprint.ModifiedAt ||
                    !Nullable.Equals(existing.RawSize, rawFingerprint.Size) ||
                    existing.RawModifiedAt != rawFingerprint.ModifiedAt);
            var changed = pathChanged || fingerprintChanged;

            Photos
                .Where(photo => photo.Id == existing.Id)
                .Set(photo => photo.BaseName, pair.BaseName)
                .Set(photo => photo.JpegPath, pair.JpegPath)
                .Set(photo => photo.RawPath, pair.RawPath)
                .Set(photo => photo.CaptureTime, FormatNullableTimestamp(captureTime))
                .Set(photo => photo.ImportStatus, changed ? "changed" : "imported")
                .Set(photo => photo.JpegSize, jpegFingerprint.Size)
                .Set(photo => photo.JpegModifiedAt, jpegFingerprint.ModifiedAt)
                .Set(photo => photo.RawSize, rawFingerprint.Size)
                .Set(photo => photo.RawModifiedAt, rawFingerprint.ModifiedAt)
                .Update();

            if (changed && !string.IsNullOrWhiteSpace(pair.JpegPath))
            {
                RequeueRatingJob(projectId, existing.Id, now);
            }
        }

        var stalePhotoIds = Photos
            .Where(photo => photo.ProjectId == projectId)
            .AsEnumerable()
            .Where(photo => !importedBaseNames.Contains(photo.BaseName))
            .Select(photo => photo.Id)
            .ToArray();

        foreach (var stalePhotoId in stalePhotoIds)
        {
            DeletePhotoCascade(stalePhotoId);
        }

        transaction.Commit();
    }

    public IReadOnlyList<PhotoItem> ListPhotos(long projectId)
    {
        return Photos
            .Where(photo => photo.ProjectId == projectId)
            .AsEnumerable()
            .OrderBy(photo => photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(photo => photo.BaseName, StringComparer.Ordinal)
            .ThenBy(photo => photo.Id)
            .Select(ToPhoto)
            .ToArray();
    }

    public PhotoItem? GetPhoto(long photoId)
    {
        var photo = Photos.FirstOrDefault(item => item.Id == photoId);
        return photo is null ? null : ToPhoto(photo);
    }

    private void DeletePhotoCascade(long photoId)
    {
        UserMarks.Where(mark => mark.PhotoId == photoId).Delete();
        RatingAuditLogs.Where(log => log.PhotoId == photoId).Delete();
        Ratings.Where(rating => rating.PhotoId == photoId).Delete();
        RatingJobs.Where(job => job.PhotoId == photoId).Delete();
        ArenaRatings.Where(rating => rating.PhotoId == photoId).Delete();
        Photos.Where(photo => photo.Id == photoId).Delete();
    }

    private static PhotoItem ToPhoto(PhotoRow row)
    {
        return new PhotoItem(
            row.Id,
            row.ProjectId,
            row.BaseName,
            row.JpegPath,
            row.RawPath,
            row.CaptureTime is null ? null : ParseTimestamp(row.CaptureTime),
            row.ImportStatus,
            row.JpegSize,
            row.JpegModifiedAt is null ? null : ParseTimestamp(row.JpegModifiedAt),
            row.RawSize,
            row.RawModifiedAt is null ? null : ParseTimestamp(row.RawModifiedAt));
    }

    private static (long? Size, string? ModifiedAt) GetFileFingerprint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (null, null);
        }

        var file = new FileInfo(path);
        return (file.Length, FormatTimestamp(file.LastWriteTimeUtc));
    }
}
