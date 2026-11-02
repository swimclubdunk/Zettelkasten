using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class TagSuggestionServiceTests
{
    private readonly FakeEmbeddingService _embeddings = new();

    private sealed class StubTags(IReadOnlyList<TagWithEmbedding> all) : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Tag>>(all.Select(t => t.Tag).ToList());
        public Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult(all);
        public Task DeleteAsync(int tagId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private TagSuggestionService Service(params string[] names) =>
        new(new StubTags(names.Select((n, i) =>
            new TagWithEmbedding(new Tag(i + 1, n), _embeddings.Embed(ContentHash.TagText(n)))).ToList()), _embeddings);

    [Fact]
    public async Task Exact_normalized_match_is_reused()
    {
        var r = await Service("machine-learning").SuggestAsync("  Machine   Learning ");
        Assert.Equal("machine-learning", r.ExactMatch?.Name);
        Assert.Empty(r.Similar);
    }

    [Fact]
    public async Task Similar_tags_are_suggested()
    {
        var r = await Service("machine-learning", "cooking").SuggestAsync("machine learning ops");
        Assert.Null(r.ExactMatch);
        var s = Assert.Single(r.Similar);
        Assert.Equal("machine-learning", s.Tag.Name);
        Assert.True(s.Score >= 0.75f);
    }

    [Fact]
    public async Task Initials_match_is_suggested_first()
    {
        var r = await Service("machine-learning-ops", "machine-learning").SuggestAsync("ML");
        Assert.Equal("machine-learning", r.Similar[0].Tag.Name);
        Assert.True(r.Similar[0].IsInitialsMatch);
    }

    [Fact]
    public async Task Initials_still_work_without_model()
    {
        var service = Service("machine-learning", "machine-learning-ops");
        _embeddings.IsAvailable = false;
        var r = await service.SuggestAsync("ml");
        Assert.Equal("machine-learning", Assert.Single(r.Similar).Tag.Name);
    }

    [Fact]
    public async Task Unrelated_and_empty_inputs_suggest_nothing()
    {
        var service = Service("machine-learning");
        Assert.Empty((await service.SuggestAsync("gardening")).Similar);
        var empty = await service.SuggestAsync("  # ");
        Assert.Null(empty.ExactMatch);
        Assert.Empty(empty.Similar);
    }

    [Fact]
    public async Task At_most_five_suggestions()
    {
        // fake cosine = 3 / sqrt(3 * 4) ≈ 0.866 for every tag
        var r = await Service("long-term-memory-a", "long-term-memory-b", "long-term-memory-c",
                "long-term-memory-d", "long-term-memory-e", "long-term-memory-f")
            .SuggestAsync("long term memory");
        Assert.Equal(5, r.Similar.Count);
    }
}
