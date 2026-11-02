using Zettelkasten.Core.Tags;

namespace Zettelkasten.Core.Search;

public sealed record SearchQuery(string FreeText, IReadOnlyList<string> Tags, string? IdPrefix)
{
    public bool IsEmpty => FreeText.Length == 0 && Tags.Count == 0 && IdPrefix is null;

    public static SearchQuery Parse(string? input)
    {
        var words = new List<string>();
        var tags = new List<string>();
        string? idPrefix = null;

        foreach (var token in (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('#'))
            {
                var tag = TagNormalizer.Normalize(token);
                if (tag.Length > 0) tags.Add(tag);
            }
            else if (token.StartsWith("id:", StringComparison.OrdinalIgnoreCase)
                     && token.Length > 3 && token[3..].All(char.IsAsciiDigit))
            {
                idPrefix = token[3..];
            }
            else
            {
                words.Add(token);
            }
        }
        return new SearchQuery(string.Join(' ', words), tags, idPrefix);
    }
}
