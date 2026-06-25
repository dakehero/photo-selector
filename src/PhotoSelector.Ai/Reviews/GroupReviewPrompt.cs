using System.Text;

namespace PhotoSelector.Ai.Reviews;

public static class GroupReviewPrompt
{
    public static string Build(string groupId, string groupReason, IReadOnlyList<GroupReviewRequestItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are reviewing a small candidate group from the same shoot.");
        builder.AppendLine("Choose the single strongest keeper that a photographer should compare against the others.");
        builder.AppendLine("Prefer expression, gesture, focus, timing, composition, and editorial usefulness.");
        builder.AppendLine("Return JSON only with winner_base_name, reason, and items.");
        builder.AppendLine("Each item should include base_name, verdict, and reason. Verdict should be winner, alternate, or reject.");
        builder.AppendLine($"Group: {groupId}");
        builder.AppendLine($"Grouping reason: {groupReason}");
        builder.AppendLine("Candidates:");

        foreach (var item in items.OrderBy(item => item.Order))
        {
            builder.AppendLine($"- {item.BaseName} (order {item.Order}, sequence {item.SequenceNumber})");
        }

        return builder.ToString().Trim();
    }
}
