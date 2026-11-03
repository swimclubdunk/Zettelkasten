using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Search;

/// <summary>Uses the note's own stored vector, so nothing is embedded on the fly.</summary>
public sealed class SimilarNotesService(INoteRepository notes, IEmbeddingService embeddings, VectorIndex index)
    : ISimilarNotesService
{
    public async Task<IReadOnlyList<SimilarNote>> FindAsync(string noteId, int limit = SemanticSettings.MaxSimilarNotes,
        CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable || index.Get(noteId) is not { } vector) return [];

        var linked = (await notes.GetOutgoingLinksAsync(noteId, ct))
            .Concat(await notes.GetBacklinksAsync(noteId, ct))
            .Select(l => l.NoteId).Append(noteId).ToHashSet();
        var hits = index.Search(vector, SemanticSettings.SimilarNoteMinScore, limit + linked.Count)
            .Where(h => !linked.Contains(h.Id)).Take(limit).ToList();
        if (hits.Count == 0) return [];

        var summaries = (await notes.GetSummariesAsync(hits.Select(h => h.Id).ToList(), ct)).ToDictionary(s => s.Id);
        return hits.Where(h => summaries.ContainsKey(h.Id))
            .Select(h => new SimilarNote(summaries[h.Id], h.Score)).ToList();
    }
}
