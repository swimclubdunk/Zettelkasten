using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class EmbeddingBackfillServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new() { IsAvailable = false };
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, _index, _time);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private EmbeddingBackfillService Service(VectorIndex index) =>
        new(_db.Factory, _embeddings, index, NullLogger<EmbeddingBackfillService>.Instance);

    private async Task AddNoteAsync(string title, params string[] tags)
    {
        _time.Advance(TimeSpan.FromSeconds(5));
        var n = await _notes.CreateAsync();
        n.Title = title;
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
    }

    [Fact]
    public async Task Backfills_notes_and_tags_saved_while_model_was_unavailable()
    {
        await AddNoteAsync("one", "memory");
        await AddNoteAsync("two");
        Assert.Equal(0, _index.Count);

        _embeddings.IsAvailable = true;
        var count = await Service(_index).BackfillAsync();

        Assert.Equal(3, count); // 2 notes + 1 tag
        Assert.Equal(2, _index.Count);
        Assert.NotNull((await new TagRepository(_db.Factory).GetAllWithEmbeddingsAsync()).Single().Embedding);
        Assert.Equal(0, await Service(_index).BackfillAsync()); // nothing stale anymore
    }

    [Fact]
    public async Task Backfill_does_nothing_without_model()
    {
        await AddNoteAsync("one");
        Assert.Equal(0, await Service(_index).BackfillAsync());
    }

    [Fact]
    public async Task LoadIndex_restores_vectors_from_database()
    {
        _embeddings.IsAvailable = true;
        await AddNoteAsync("one");
        await AddNoteAsync("two");

        var fresh = new VectorIndex();
        await Service(fresh).LoadIndexAsync();

        Assert.Equal(2, fresh.Count);
    }
}
