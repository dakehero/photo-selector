using PhotoSelector.Core.Grouping;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Agent.Workflows;

public sealed class ShootReviewDraftBuilder
{
    public ShootReviewDraft Build(ProjectDatabase database, PhotoProject project)
    {
        var allPhotos = database.ListPhotos(project.Id);
        var currentPhotos = allPhotos
            .Where(photo => PhotoImportStatus.IsCurrent(photo.ImportStatus))
            .ToArray();
        var missingPhotos = allPhotos.Count - currentPhotos.Length;
        var ratings = currentPhotos
            .Select(photo => new PhotoWithRating(photo, database.ListRatings(photo.Id).FirstOrDefault()))
            .ToArray();
        var rated = ratings.Where(item => item.Rating is not null).ToArray();
        var groups = FilenameSequenceGrouper.Group(currentPhotos, SequenceGroupingOptions.Default).ToArray();
        var latestGroupReviews = database.ListGroupReviews(project.Id)
            .GroupBy(review => review.GroupId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(review => review.CreatedAt).ThenByDescending(review => review.Id).First())
            .OrderBy(review => review.GroupId, StringComparer.OrdinalIgnoreCase)
            .Select(review => new ShootReviewGroupReview(
                review.GroupId,
                review.WinnerPhotoId,
                review.WinnerBaseName,
                review.Reason,
                review.Provider,
                review.Model))
            .ToArray();
        var topCandidates = SelectTopCandidates(rated);
        var weakPatterns = BuildWeakPatterns(rated, missingPhotos);
        var nextShootNotes = BuildNextShootNotes(rated, groups.Length, latestGroupReviews.Length);

        var summary = new ShootReviewSummary(
            allPhotos.Count,
            currentPhotos.Length,
            missingPhotos,
            rated.Length,
            CountCategory(rated, "keep"),
            CountCategory(rated, "maybe"),
            CountCategory(rated, "reject"),
            groups.Length,
            latestGroupReviews.Length);

        return new ShootReviewDraft(
            project.Id,
            project.SourceDirectory,
            summary,
            BuildSummaryText(summary, topCandidates, latestGroupReviews),
            groups
                .Select(group => new ShootReviewGroup(
                    group.Id,
                    group.Type,
                    group.Key,
                    group.Reason,
                    group.Items.Select(item => new ShootReviewGroupItem(item.PhotoId, item.BaseName)).ToArray()))
                .ToArray(),
            latestGroupReviews,
            topCandidates,
            weakPatterns,
            nextShootNotes);
    }

    private static ShootReviewCandidate[] SelectTopCandidates(IReadOnlyList<PhotoWithRating> rated)
    {
        var keep = rated
            .Where(item => string.Equals(item.Rating!.Category, "keep", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var candidates = keep.Length > 0
            ? keep
            : rated.Where(item => string.Equals(item.Rating!.Category, "maybe", StringComparison.OrdinalIgnoreCase)).ToArray();

        return candidates
            .OrderByDescending(item => item.Rating!.Score)
            .ThenBy(item => item.Photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => new ShootReviewCandidate(
                item.Photo.Id,
                item.Photo.BaseName,
                item.Rating!.Score,
                item.Rating.Category,
                item.Rating.Reason))
            .ToArray();
    }

    private static string[] BuildWeakPatterns(IReadOnlyList<PhotoWithRating> rated, int missingPhotos)
    {
        var patterns = new List<string>();
        var rejected = CountCategory(rated, "reject");
        if (rejected > 0)
        {
            patterns.Add($"{rejected} photo(s) were rated reject; inspect these for recurring focus, timing, or composition issues.");
        }

        var maybe = CountCategory(rated, "maybe");
        if (maybe > 0)
        {
            patterns.Add($"{maybe} photo(s) landed in maybe; compare them against keepers before export.");
        }

        if (missingPhotos > 0)
        {
            patterns.Add($"{missingPhotos} catalog photo(s) are missing from disk and should not drive current shoot decisions.");
        }

        return patterns.ToArray();
    }

    private static string[] BuildNextShootNotes(
        IReadOnlyList<PhotoWithRating> rated,
        int groupCount,
        int reviewedGroupCount)
    {
        var notes = new List<string>();
        if (groupCount > reviewedGroupCount)
        {
            notes.Add("Review unreviewed candidate groups to turn similar-frame clusters into explicit keepers.");
        }

        if (CountCategory(rated, "maybe") > CountCategory(rated, "keep"))
        {
            notes.Add("Aim for clearer in-camera decisions so fewer frames land in the maybe range.");
        }

        if (CountCategory(rated, "reject") > 0)
        {
            notes.Add("Use rejected frames as evidence for the next shoot's focus, timing, and composition checklist.");
        }

        return notes.Count == 0
            ? ["Keep collecting group-level decisions so future shoot reviews can learn your preferences."]
            : notes.ToArray();
    }

    private static string BuildSummaryText(
        ShootReviewSummary summary,
        IReadOnlyList<ShootReviewCandidate> topCandidates,
        IReadOnlyList<ShootReviewGroupReview> groupReviews)
    {
        var winner = groupReviews.FirstOrDefault()?.WinnerBaseName ?? topCandidates.FirstOrDefault()?.BaseName;
        var prefix = $"Reviewed {summary.CurrentPhotos} current photo(s): {summary.Keep} keep, {summary.Maybe} maybe, {summary.Reject} reject.";
        return string.IsNullOrWhiteSpace(winner)
            ? prefix
            : $"{prefix} Current strongest referenced frame: {winner}.";
    }

    private static int CountCategory(IReadOnlyList<PhotoWithRating> rated, string category)
    {
        return rated.Count(item => string.Equals(item.Rating!.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PhotoWithRating(PhotoItem Photo, PhotoRating? Rating);
}

public sealed record ShootReviewDraft(
    long ProjectId,
    string SourceDirectory,
    ShootReviewSummary Summary,
    string SummaryText,
    IReadOnlyList<ShootReviewGroup> Groups,
    IReadOnlyList<ShootReviewGroupReview> GroupReviews,
    IReadOnlyList<ShootReviewCandidate> TopCandidates,
    IReadOnlyList<string> WeakPatterns,
    IReadOnlyList<string> NextShootNotes);

public sealed record ShootReviewSummary(
    int TotalPhotos,
    int CurrentPhotos,
    int MissingPhotos,
    int RatedPhotos,
    int Keep,
    int Maybe,
    int Reject,
    int Groups,
    int ReviewedGroups);

public sealed record ShootReviewGroup(
    string Id,
    string Type,
    string Key,
    string Reason,
    IReadOnlyList<ShootReviewGroupItem> Items);

public sealed record ShootReviewGroupItem(long PhotoId, string BaseName);

public sealed record ShootReviewGroupReview(
    string GroupId,
    long WinnerPhotoId,
    string WinnerBaseName,
    string Reason,
    string Provider,
    string Model);

public sealed record ShootReviewCandidate(
    long PhotoId,
    string BaseName,
    double Score,
    string Category,
    string Reason);
