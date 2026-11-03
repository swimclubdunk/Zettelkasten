using System.Text.RegularExpressions;

namespace Zettelkasten.Core.Links;

/// <summary>The sentence around an inline link, as plain text, so a backlink can show why it links.</summary>
public static partial class LinkContext
{
    public const int MaxLength = 140;

    [GeneratedRegex(@"^(#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)+")]
    private static partial Regex BlockMarkers();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\*\*|__|~~|[*`]")]
    private static partial Regex InlineMarkers();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Returns null when <paramref name="content"/> has no inline link to <paramref name="targetId"/>.</summary>
    public static string? Extract(string content, string targetId, Func<string, string?> titleOf)
    {
        var token = $"[[{targetId}]]";
        var line = content.Split('\n').FirstOrDefault(l => l.Contains(token, StringComparison.Ordinal));
        if (line is null) return null;

        line = BlockMarkers().Replace(line.Trim(), "");
        var sentence = SentenceBreak().Split(line).First(s => s.Contains(token, StringComparison.Ordinal));
        var at = sentence.IndexOf(token, StringComparison.Ordinal);

        var before = Clean(sentence[..at], titleOf).TrimStart();
        var center = titleOf(targetId) ?? targetId;
        var after = Clean(sentence[(at + token.Length)..], titleOf).TrimEnd();
        return Trim(before, center, after);
    }

    private static string Clean(string text, Func<string, string?> titleOf)
    {
        var withTitles = LinkParser.Matches(text).Reverse().Aggregate(text, (s, m) =>
            s[..m.Start] + (titleOf(m.TargetId) ?? m.TargetId) + s[(m.Start + m.Length)..]);
        var plain = InlineMarkers().Replace(MarkdownLink().Replace(withTitles, "$1"), "");
        return Whitespace().Replace(plain, " ");
    }

    // Keeps the link in view: cuts both sides evenly, giving unused room on one side to the other.
    private static string Trim(string before, string center, string after)
    {
        if (before.Length + center.Length + after.Length <= MaxLength) return before + center + after;
        var budget = Math.Max(0, MaxLength - center.Length - 2); // two ellipses
        var keepAfter = Math.Min(after.Length, Math.Max(budget / 2, budget - before.Length));
        var keepBefore = Math.Min(before.Length, budget - keepAfter);
        var head = keepBefore < before.Length ? "…" + before[^keepBefore..] : before;
        var tail = keepAfter < after.Length ? after[..keepAfter] + "…" : after;
        return head + center + tail;
    }
}
