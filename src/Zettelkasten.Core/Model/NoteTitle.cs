using System.Text.RegularExpressions;

namespace Zettelkasten.Core.Model;

/// <summary>Notes have no title of their own; lists show the first meaningful line of the content.</summary>
public static partial class NoteTitle
{
    public const int MaxLength = 80;

    // Leading Markdown block markers: headings, quotes, bullets, numbered items
    [GeneratedRegex(@"^(#{1,6}\s+|#{1,6}$|>\s*|[-*+]\s+|\d+[.)]\s+)+")]
    private static partial Regex BlockMarkers();

    public static string FromContent(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = BlockMarkers().Replace(raw.Trim(), "").Trim();
            if (line.Length == 0) continue;
            return line.Length <= MaxLength ? line : line[..(MaxLength - 1)] + "…";
        }
        return "";
    }
}
