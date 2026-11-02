using System.Security.Cryptography;
using System.Text;

namespace Zettelkasten.Infrastructure.Embeddings;

public static class ContentHash
{
    /// <summary>The text that gets embedded for a note.</summary>
    public static string NoteText(string title, string content) => $"{title}\n{content}";

    /// <summary>The text that gets embedded for a tag: "machine-learning" -> "machine learning".</summary>
    public static string TagText(string tagName) => tagName.Replace('-', ' ');

    public static string Compute(string title, string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NoteText(title, content))));
}
