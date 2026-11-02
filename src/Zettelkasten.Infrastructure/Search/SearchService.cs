using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Search;

public sealed class SearchService(
    IDbContextFactory<AppDbContext> factory,
    INoteRepository notes,
    IEmbeddingService embeddings,
    VectorIndex index,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = SearchQuery.Parse(query);
        if (q.IsEmpty)
        {
            var recent = await notes.GetRecentAsync(SemanticSettings.MaxResults, ct);
            return recent.Select(n => new SearchResult(n, false, false)).ToList();
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var tags = q.Tags.Distinct().ToList();
        var keyword = new HashSet<string>();
        var semantic = new HashSet<string>();
        List<string> ordered;

        if (q.FreeText.Length > 0)
        {
            var rankings = new List<IReadOnlyList<string>>();
            var keywordIds = await KeywordAsync(db, q.FreeText, ct);
            keyword.UnionWith(keywordIds);
            rankings.Add(keywordIds);

            if (embeddings.IsAvailable)
            {
                var vector = await Task.Run(() => embeddings.Embed(q.FreeText), ct);
                var semanticIds = index.Search(vector, SemanticSettings.SearchMinScore, SemanticSettings.CandidateLimit)
                    .Select(h => h.Id).ToList();
                semantic.UnionWith(semanticIds);
                rankings.Add(semanticIds);
            }
            ordered = RankFusion.Fuse(rankings).ToList();
        }
        else if (q.IdPrefix is not null)
        {
            ordered = await db.Notes.AsNoTracking()
                .Where(n => n.Id.StartsWith(q.IdPrefix))
                .OrderByDescending(n => n.Id).Select(n => n.Id)
                .Take(SemanticSettings.CandidateLimit).ToListAsync(ct);
        }
        else
        {
            var withTags = await NotesWithAllTagsAsync(db, tags, ct);
            ordered = await db.Notes.AsNoTracking()
                .Where(n => withTags.Contains(n.Id))
                .OrderByDescending(n => n.UpdatedAtMs).Select(n => n.Id)
                .Take(SemanticSettings.MaxResults).ToListAsync(ct);
        }

        if (q.IdPrefix is not null)
            ordered = ordered.Where(id => id.StartsWith(q.IdPrefix, StringComparison.Ordinal)).ToList();
        if (tags.Count > 0)
        {
            var allowed = await NotesWithAllTagsAsync(db, tags, ct);
            ordered = ordered.Where(allowed.Contains).ToList();
        }

        var summaries = await notes.GetSummariesAsync(ordered.Take(SemanticSettings.MaxResults).ToList(), ct);
        return summaries.Select(n => new SearchResult(n, keyword.Contains(n.Id), semantic.Contains(n.Id))).ToList();
    }

    private async Task<List<string>> KeywordAsync(AppDbContext db, string freeText, CancellationToken ct)
    {
        var expression = FtsIndex.BuildMatchExpression(freeText);
        if (expression is null) return [];
        try
        {
            return await FtsIndex.MatchAsync(db, expression, SemanticSettings.CandidateLimit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Keyword search failed for {Expression}", expression);
            return [];
        }
    }

    private static async Task<HashSet<string>> NotesWithAllTagsAsync(AppDbContext db, List<string> tags, CancellationToken ct)
    {
        var ids = await db.NoteTags.AsNoTracking()
            .Where(nt => tags.Contains(nt.Tag!.Name))
            .GroupBy(nt => nt.NoteId)
            .Where(g => g.Count() == tags.Count)
            .Select(g => g.Key)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }
}
