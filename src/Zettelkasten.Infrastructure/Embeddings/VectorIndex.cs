using System.Collections.Concurrent;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

public sealed record VectorHit(string Id, float Score);

/// <summary>In-memory note vectors; brute-force dot product is fast enough for tens of thousands of notes.</summary>
public sealed class VectorIndex
{
    private readonly ConcurrentDictionary<string, float[]> _vectors = new();

    public int Count => _vectors.Count;
    public void Set(string id, float[] vector) => _vectors[id] = vector;
    public void Remove(string id) => _vectors.TryRemove(id, out _);
    public void Clear() => _vectors.Clear();
    public float[]? Get(string id) => _vectors.GetValueOrDefault(id);

    public IReadOnlyList<VectorHit> Search(float[] query, float minScore, int limit) =>
        _vectors
            .Select(kv => new VectorHit(kv.Key, VectorMath.Dot(query, kv.Value)))
            .Where(h => h.Score >= minScore)
            .OrderByDescending(h => h.Score)
            .Take(limit)
            .ToList();
}
