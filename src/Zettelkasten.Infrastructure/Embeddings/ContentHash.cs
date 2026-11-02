using System.Security.Cryptography;
using System.Text;

namespace Zettelkasten.Infrastructure.Embeddings;

public static class ContentHash
{
    /// <summary>The text that gets embedded for a tag: "machine-learning" -> "machine learning".</summary>
    public static string TagText(string tagName) => tagName.Replace('-', ' ');

    /// <summary>Hash of the embedded note text (the content); a change means the note must be re-embedded.</summary>
    public static string Compute(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
