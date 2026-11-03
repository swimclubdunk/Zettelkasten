using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class SimilarNotesServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;
    private SimilarNotesService _similar = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, _index, _time);
        _similar = new SimilarNotesService(_notes, _embeddings, _index);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Note> AddAsync(string content, Action<Note>? edit = null)
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var n = await _notes.CreateAsync();
        n.Content = content;
        edit?.Invoke(n);
        await _notes.SaveAsync(n);
        return n;
    }

    [Fact]
    public async Task Finds_notes_with_shared_meaning_best_first_and_excludes_itself()
    {
        var note = await AddAsync("spaced repetition memory review intervals");
        var close = await AddAsync("memory review intervals schedule");
        var closer = await AddAsync("spaced repetition memory review");
        await AddAsync("sourdough bread baking");

        var result = await _similar.FindAsync(note.Id);

        Assert.Equal(new[] { closer.Id, close.Id }, result.Select(s => s.Note.Id));
        Assert.True(result[0].Score >= result[1].Score);
    }

    [Fact]
    public async Task Excludes_notes_already_linked_in_either_direction()
    {
        var linkedTo = await AddAsync("memory review intervals");
        var manual = await AddAsync("memory review spacing");
        var note = await AddAsync($"memory review [[{linkedTo.Id}]]", n => n.ManualLinks = [manual.Id]);
        var linkingHere = await AddAsync($"memory review notes [[{note.Id}]]");
        var free = await AddAsync("memory review habits");

        var ids = (await _similar.FindAsync(note.Id)).Select(s => s.Note.Id).ToList();

        Assert.Contains(free.Id, ids);
        Assert.DoesNotContain(linkedTo.Id, ids);
        Assert.DoesNotContain(manual.Id, ids);
        Assert.DoesNotContain(linkingHere.Id, ids);
    }

    [Fact]
    public async Task Respects_the_limit()
    {
        var note = await AddAsync("memory review");
        for (var i = 0; i < 4; i++) await AddAsync($"memory review {i}");
        Assert.Equal(2, (await _similar.FindAsync(note.Id, limit: 2)).Count);
    }

    [Fact]
    public async Task Returns_nothing_without_the_model_or_a_vector()
    {
        var note = await AddAsync("memory review");
        await AddAsync("memory review again");
        Assert.Empty(await _similar.FindAsync("20990101000000"));

        _embeddings.IsAvailable = false;
        Assert.Empty(await _similar.FindAsync(note.Id));
    }
}
