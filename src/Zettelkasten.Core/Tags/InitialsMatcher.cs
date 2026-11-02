namespace Zettelkasten.Core.Tags;

/// <summary>Detects abbreviations: "ml" is the initials of "machine-learning".</summary>
public static class InitialsMatcher
{
    public static bool IsMatch(string a, string b)
    {
        var (shortTag, longTag) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shortTag.Length < 2 || shortTag.Contains('-')) return false;
        var parts = longTag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != shortTag.Length) return false;
        for (var i = 0; i < parts.Length; i++)
            if (parts[i][0] != shortTag[i]) return false;
        return true;
    }
}
