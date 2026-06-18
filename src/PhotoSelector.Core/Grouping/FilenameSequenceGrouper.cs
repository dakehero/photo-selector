using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Grouping;

public static class FilenameSequenceGrouper
{
    public const string Method = "filename-sequence";

    public static IReadOnlyList<PhotoGroup> Group(IEnumerable<PhotoItem> photos, int maxGap = 2)
    {
        return Group(photos, new SequenceGroupingOptions(MaxFilenameGap: maxGap));
    }

    public static IReadOnlyList<PhotoGroup> Group(IEnumerable<PhotoItem> photos, SequenceGroupingOptions options)
    {
        if (options.MaxFilenameGap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFilenameGap must be at least 1.");
        }

        if (options.MaxCaptureTimeGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxCaptureTimeGap must be greater than zero when provided.");
        }

        return photos
            .Select(ParseCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.Prefix, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => BuildGroups(group, options))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Items[0].SequenceNumber)
            .ThenBy(group => group.Items[0].BaseName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<PhotoGroup> BuildGroups(IEnumerable<Candidate> candidates, SequenceGroupingOptions options)
    {
        var ordered = candidates
            .OrderBy(candidate => candidate.Number)
            .ThenBy(candidate => candidate.Photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Photo.Id)
            .ToArray();
        var current = new List<Candidate>();

        foreach (var candidate in ordered)
        {
            if (current.Count > 0 && ShouldSplit(current[^1], candidate, options))
            {
                foreach (var group in CreateGroup(current, options))
                {
                    yield return group;
                }

                current.Clear();
            }

            current.Add(candidate);
        }

        foreach (var group in CreateGroup(current, options))
        {
            yield return group;
        }
    }

    private static bool ShouldSplit(Candidate previous, Candidate next, SequenceGroupingOptions options)
    {
        if (next.Number - previous.Number > options.MaxFilenameGap)
        {
            return true;
        }

        if (options.MaxCaptureTimeGap is null ||
            previous.Photo.CaptureTime is null ||
            next.Photo.CaptureTime is null)
        {
            return false;
        }

        var gap = (next.Photo.CaptureTime.Value - previous.Photo.CaptureTime.Value).Duration();
        return gap > options.MaxCaptureTimeGap.Value;
    }

    private static IEnumerable<PhotoGroup> CreateGroup(IReadOnlyList<Candidate> candidates, SequenceGroupingOptions options)
    {
        if (candidates.Count < 2)
        {
            yield break;
        }

        var first = candidates[0];
        var last = candidates[^1];
        var width = Math.Max(first.Width, last.Width);
        var items = candidates
            .Select((candidate, index) => new PhotoGroupItem(
                candidate.Photo.Id,
                candidate.Photo.BaseName,
                index,
                candidate.Number))
            .ToArray();

        yield return new PhotoGroup(
            $"{Method}:{first.Prefix}:{first.Number.ToString($"D{width}")}-{last.Number.ToString($"D{width}")}",
            "sequence",
            first.Prefix,
            BuildReason(options),
            items);
    }

    private static string BuildReason(SequenceGroupingOptions options)
    {
        var reason = $"filename sequence within gap {options.MaxFilenameGap}";
        return options.MaxCaptureTimeGap is null
            ? reason
            : $"{reason}; capture time gap <= {options.MaxCaptureTimeGap.Value.TotalSeconds:0}s when available";
    }

    private static Candidate? ParseCandidate(PhotoItem photo)
    {
        var index = photo.BaseName.Length - 1;
        while (index >= 0 && char.IsDigit(photo.BaseName[index]))
        {
            index--;
        }

        if (index == photo.BaseName.Length - 1)
        {
            return null;
        }

        var prefix = photo.BaseName[..(index + 1)];
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }

        var digits = photo.BaseName[(index + 1)..];
        return long.TryParse(digits, out var number)
            ? new Candidate(photo, prefix, number, digits.Length)
            : null;
    }

    private sealed record Candidate(PhotoItem Photo, string Prefix, long Number, int Width);
}
