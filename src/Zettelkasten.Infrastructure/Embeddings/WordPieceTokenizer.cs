using System.Globalization;
using System.Text;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>Minimal uncased BERT tokenizer (basic tokenization + greedy WordPiece) for all-MiniLM-L6-v2.</summary>
public sealed class WordPieceTokenizer
{
    private const int MaxCharsPerWord = 100;
    private readonly Dictionary<string, int> _vocab;
    private readonly int _cls, _sep, _unk;

    public WordPieceTokenizer(IReadOnlyList<string> vocab)
    {
        _vocab = new Dictionary<string, int>(vocab.Count, StringComparer.Ordinal);
        for (var i = 0; i < vocab.Count; i++) _vocab.TryAdd(vocab[i], i);
        _cls = _vocab["[CLS]"];
        _sep = _vocab["[SEP]"];
        _unk = _vocab["[UNK]"];
    }

    public static WordPieceTokenizer FromFile(string path) => new(File.ReadAllLines(path));

    public long[] Encode(string text, int maxTokens = SemanticSettings.MaxTokens)
    {
        var ids = new List<long>(Math.Min(maxTokens, 64)) { _cls };
        foreach (var word in BasicTokenize(text))
        {
            foreach (var id in WordPiece(word))
            {
                if (ids.Count == maxTokens - 1) goto done;
                ids.Add(id);
            }
        }
        done:
        ids.Add(_sep);
        return ids.ToArray();
    }

    internal static IEnumerable<string> BasicTokenize(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var word = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // strip accents
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                continue;
            }
            if (IsPunctuation(ch))
            {
                if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                yield return ch.ToString();
                continue;
            }
            word.Append(ch);
        }
        if (word.Length > 0) yield return word.ToString();
    }

    private IEnumerable<int> WordPiece(string word)
    {
        if (word.Length > MaxCharsPerWord) return [_unk];
        var pieces = new List<int>();
        var start = 0;
        while (start < word.Length)
        {
            var end = word.Length;
            var found = -1;
            while (start < end)
            {
                var sub = word[start..end];
                if (start > 0) sub = "##" + sub;
                if (_vocab.TryGetValue(sub, out var id)) { found = id; break; }
                end--;
            }
            if (found < 0) return [_unk];
            pieces.Add(found);
            start = end;
        }
        return pieces;
    }

    private static bool IsPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~'
        || char.IsPunctuation(c);
}
