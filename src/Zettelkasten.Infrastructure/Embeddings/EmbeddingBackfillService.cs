using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>Loads stored note vectors into memory and embeds anything saved while the model was unavailable.</summary>
public sealed class EmbeddingBackfillService(
    IDbContextFactory<AppDbContext> factory,
    IEmbeddingService embeddings,
    VectorIndex index,
    ILogger<EmbeddingBackfillService> logger)
{
    private const int BatchSize = 50;

    public async Task LoadIndexAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        index.Clear();
        await foreach (var e in db.NoteEmbeddings.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
            index.Set(e.NoteId, VectorMath.FromBytes(e.Vector));
        logger.LogInformation("Loaded {Count} note vectors", index.Count);
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable) return 0;
        var updated = 0;

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var notes = await db.Notes.AsNoTracking()
                .Select(n => new { n.Id, n.Title, n.Content, Hash = n.Embedding != null ? n.Embedding.ContentHash : null })
                .ToListAsync(ct);
            var stale = notes.Where(n => ContentHash.Compute(n.Title, n.Content) != n.Hash).ToList();

            foreach (var batch in stale.Chunk(BatchSize))
            {
                var vectors = new Dictionary<string, float[]>();
                foreach (var n in batch)
                {
                    var vector = await Task.Run(() => embeddings.Embed(ContentHash.NoteText(n.Title, n.Content)), ct);
                    vectors[n.Id] = vector;
                    var hash = ContentHash.Compute(n.Title, n.Content);
                    var existing = await db.NoteEmbeddings.FindAsync([n.Id], ct);
                    if (existing is null)
                        db.NoteEmbeddings.Add(new NoteEmbeddingEntity { NoteId = n.Id, Vector = VectorMath.ToBytes(vector), ContentHash = hash });
                    else
                    {
                        existing.Vector = VectorMath.ToBytes(vector);
                        existing.ContentHash = hash;
                    }
                }
                try
                {
                    await db.SaveChangesAsync(ct);
                    foreach (var (id, v) in vectors) index.Set(id, v);
                    updated += batch.Length;
                }
                catch (DbUpdateException ex)
                {
                    // A concurrent save of the same note won; the next backfill catches up.
                    logger.LogWarning(ex, "Embedding backfill batch failed");
                    db.ChangeTracker.Clear();
                }
            }

            var tags = await db.Tags.Where(t => t.Embedding == null).ToListAsync(ct);
            foreach (var t in tags)
                t.Embedding = VectorMath.ToBytes(embeddings.Embed(ContentHash.TagText(t.Name)));
            await db.SaveChangesAsync(ct);
            updated += tags.Count;
        }

        if (updated > 0) logger.LogInformation("Backfilled {Count} embeddings", updated);
        return updated;
    }
}
