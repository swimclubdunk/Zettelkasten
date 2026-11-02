using System.Text;

namespace Zettelkasten.Core.Tags;

public static class TagNormalizer
{
    public static string Normalize(string input)
    {
        var s = input.Normalize(NormalizationForm.FormC).Trim().TrimStart('#').ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        var pendingDash = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                pendingDash = sb.Length > 0;
                continue;
            }
            if (pendingDash) sb.Append('-');
            pendingDash = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
