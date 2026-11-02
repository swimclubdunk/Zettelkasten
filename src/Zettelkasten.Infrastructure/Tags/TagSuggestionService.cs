using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Core.Tags;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Tags;

public sealed class TagSuggestionService(ITagRepository tags, IEmbeddingService embeddings) : ITagSuggestionService
{
    public async Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default)
    {
        var name = TagNormalizer.Normalize(input);
        if (name.Length == 0) return new TagSuggestions(null, []);

        var all = await tags.GetAllWithEmbeddingsAsync(ct);
        var exact = all.FirstOrDefault(t => string.Equals(t.Tag.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new TagSuggestions(exact.Tag, []);

        var query = embeddings.IsAvailable
            ? await Task.Run(() => embeddings.Embed(ContentHash.TagText(name)), ct)
            : null;

        var similar = all
            .Select(t => new TagSuggestion(
                t.Tag,
                query is not null && t.Embedding is not null ? VectorMath.Dot(query, t.Embedding) : 0f,
                InitialsMatcher.IsMatch(name, t.Tag.Name)))
            .Where(s => s.IsInitialsMatch || s.Score >= SemanticSettings.TagSuggestionMinScore)
            .OrderByDescending(s => s.IsInitialsMatch)
            .ThenByDescending(s => s.Score)
            .Take(SemanticSettings.MaxTagSuggestions)
            .ToList();

        return new TagSuggestions(null, similar);
    }
}
