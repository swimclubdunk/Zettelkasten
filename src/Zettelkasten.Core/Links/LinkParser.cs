using System.Text.RegularExpressions;

namespace Zettelkasten.Core.Links;

public readonly record struct LinkMatch(int Start, int Length, string TargetId);

/// <summary>Finds inline [[yyyyMMddHHmmss]] links in Markdown text.</summary>
public static partial class LinkParser
{
    [GeneratedRegex(@"\[\[(\d{14})\]\]")]
    private static partial Regex LinkRegex();

    public static IReadOnlyList<LinkMatch> Matches(string text) =>
        LinkRegex().Matches(text)
            .Select(m => new LinkMatch(m.Index, m.Length, m.Groups[1].Value))
            .ToList();

    public static IReadOnlyList<string> ExtractTargets(string text) =>
        Matches(text).Select(m => m.TargetId).Distinct().ToList();

    public static LinkMatch? FindAt(string text, int offset)
    {
        foreach (var m in Matches(text))
            if (offset >= m.Start && offset <= m.Start + m.Length) return m;
        return null;
    }
}
