namespace Zettelkasten.Core.Links;

/// <summary>Turns [[id]] into Markdown links the preview renderer understands.</summary>
public static class WikiLinkRewriter
{
    public const string NoteScheme = "zk:";
    public const string BrokenScheme = "zk-broken:";

    public static string Rewrite(string markdown, Func<string, string?> titleLookup)
    {
        var matches = LinkParser.Matches(markdown);
        if (matches.Count == 0) return markdown;

        var sb = new System.Text.StringBuilder(markdown.Length + matches.Count * 16);
        var pos = 0;
        foreach (var m in matches)
        {
            sb.Append(markdown, pos, m.Start - pos);
            var title = titleLookup(m.TargetId);
            if (title is null)
                sb.Append($"[⚠ {m.TargetId}]({BrokenScheme}{m.TargetId})");
            else
            {
                var text = string.IsNullOrWhiteSpace(title) ? m.TargetId : Escape(title);
                sb.Append($"[{text}]({NoteScheme}{m.TargetId})");
            }
            pos = m.Start + m.Length;
        }
        sb.Append(markdown, pos, markdown.Length - pos);
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
}
