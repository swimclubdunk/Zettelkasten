using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class TagRepositoryTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;
    private TagRepository _tags = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, new VectorIndex(), _time);
        _tags = new TagRepository(_db.Factory);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<string> NoteWithTagsAsync(params string[] tags)
    {
        _time.Advance(TimeSpan.FromSeconds(5));
        var n = await _notes.CreateAsync();
        n.Content = "note";
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
        return n.Id;
    }

    [Fact]
    public async Task GetAll_returns_tags_sorted_by_name()
    {
        await NoteWithTagsAsync("zeta", "alpha");
        var all = await _tags.GetAllAsync();
        Assert.Equal(new[] { "alpha", "zeta" }, all.Select(t => t.Name));
    }

    [Fact]
    public async Task GetAllWithEmbeddings_returns_vectors_when_model_was_available()
    {
        await NoteWithTagsAsync("memory");
        _embeddings.IsAvailable = false;
        await NoteWithTagsAsync("offline-tag");

        var all = await _tags.GetAllWithEmbeddingsAsync();
        Assert.Equal(384, all.Single(t => t.Tag.Name == "memory").Embedding!.Length);
        Assert.Null(all.Single(t => t.Tag.Name == "offline-tag").Embedding);
    }

    [Fact]
    public async Task Delete_removes_tag_from_notes_and_search_index()
    {
        var id = await NoteWithTagsAsync("memory", "keep");
        var memory = (await _tags.GetAllAsync()).Single(t => t.Name == "memory");

        await _tags.DeleteAsync(memory.Id);

        Assert.Equal(new[] { "keep" }, (await _notes.GetAsync(id))!.Tags);
        Assert.DoesNotContain(await _tags.GetAllAsync(), t => t.Name == "memory");
        await using var ctx = _db.Factory.CreateDbContext();
        Assert.Empty(await FtsIndex.MatchAsync(ctx, "\"memory\"*", 10, default));
        Assert.Equal(new[] { id }, await FtsIndex.MatchAsync(ctx, "\"keep\"*", 10, default));
    }
}
