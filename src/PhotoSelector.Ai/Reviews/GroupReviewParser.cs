using System.Text.Json;

namespace PhotoSelector.Ai.Reviews;

internal static class GroupReviewParser
{
    public static GroupReviewParseResult Parse(string json)
    {
        GroupReviewResponseJson? response;
        try
        {
            response = JsonSerializer.Deserialize(json, ReviewJsonContext.Default.GroupReviewResponseJson);
        }
        catch (JsonException ex)
        {
            return GroupReviewParseResult.Failure($"AI group review response is invalid JSON: {ex.Message}");
        }

        if (response is null)
        {
            return GroupReviewParseResult.Failure("AI group review response was empty.");
        }

        if (string.IsNullOrWhiteSpace(response.WinnerBaseName))
        {
            return GroupReviewParseResult.Failure("AI group review response did not include winner_base_name.");
        }

        if (string.IsNullOrWhiteSpace(response.Reason))
        {
            return GroupReviewParseResult.Failure("AI group review response did not include reason.");
        }

        var items = response.Items?
            .Where(item => !string.IsNullOrWhiteSpace(item.BaseName))
            .Select(item => new GroupReviewItemDecision(
                item.BaseName!.Trim(),
                string.IsNullOrWhiteSpace(item.Verdict) ? "unknown" : item.Verdict.Trim(),
                string.IsNullOrWhiteSpace(item.Reason) ? string.Empty : item.Reason.Trim()))
            .ToArray() ?? [];

        return GroupReviewParseResult.Success(new GroupReviewDecision(
            response.WinnerBaseName.Trim(),
            response.Reason.Trim(),
            items));
    }
}

internal sealed record GroupReviewParseResult(GroupReviewDecision? Review, string? Error)
{
    public bool IsSuccess => Review is not null;

    public static GroupReviewParseResult Success(GroupReviewDecision review)
    {
        return new GroupReviewParseResult(review, null);
    }

    public static GroupReviewParseResult Failure(string error)
    {
        return new GroupReviewParseResult(null, error);
    }
}
