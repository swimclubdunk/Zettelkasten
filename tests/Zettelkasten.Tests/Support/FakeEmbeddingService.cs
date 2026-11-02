using System.Text;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Support;

/// <summary>Deterministic bag-of-words embedding: texts sharing words are similar.</summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    private bool _isAvailable = true;

    public bool IsAvailable
    {
        get => _isAvailable;
        set { _isAvailable = value; AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
    }

    public int EmbedCount { get; private set; }
    /// <summary>Words mapped to another word before hashing, to simulate meaning without shared words.</summary>
    public Dictionary<string, string> Synonyms { get; } = [];
    public event EventHandler? AvailabilityChanged;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public float[] Embed(string text)
    {
        if (!IsAvailable) throw new InvalidOperationException("Model not available.");
        EmbedCount++;
        var v = new float[SemanticSettings.Dimensions];
        var word = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(ch)) { word.Append(ch); continue; }
            if (word.Length > 0)
            {
                var w = word.ToString();
                v[Fnv(Synonyms.GetValueOrDefault(w, w)) % (uint)v.Length] += 1;
                word.Clear();
            }
        }
        VectorMath.Normalize(v);
        return v;
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return h;
    }
}
