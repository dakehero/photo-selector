using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Ai.Reviews;

public interface IGroupReviewClient : IDisposable
{
    Task<GroupReviewClientResult> ReviewGroupAsync(GroupReviewRequest request, CancellationToken cancellationToken);
}

public sealed record GroupReviewRequest(
    Uri BaseUrl,
    string ApiKey,
    string Model,
    string Prompt,
    IReadOnlyList<GroupReviewRequestItem> Items,
    PhotoPreviewOptions? Preview = null);

public sealed record GroupReviewRequestItem(
    long PhotoId,
    string BaseName,
    string ImagePath,
    int Order,
    long SequenceNumber);

public sealed record GroupReviewDecision(
    string WinnerBaseName,
    string Reason,
    IReadOnlyList<GroupReviewItemDecision> Items);

public sealed record GroupReviewItemDecision(
    string BaseName,
    string Verdict,
    string Reason);

public sealed record GroupReviewClientResult(
    GroupReviewDecision? Review,
    AiRatingAudit Audit);
