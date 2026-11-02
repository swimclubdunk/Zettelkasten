using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class NoteRepositoryTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 14, 30, 15, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _repo = new NoteRepository(_db.Factory, _embeddings, _index, _time);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Note> CreateNextAsync()
    {
        _time.Advance(TimeSpan.FromSeconds(10));
        return await _repo.CreateAsync();
    }

    [Fact]
    public async Task Create_returns_persisted_note_with_id_from_clock()
    {
        var note = await _repo.CreateAsync();
        Assert.Equal("20261102143015", note.Id);
        Assert.NotNull(await _repo.GetAsync(note.Id));
        Assert.Equal(1, await _repo.CountAsync());
    }

    [Fact]
    public async Task Title_is_derived_from_first_line_of_content()
    {
        var target = await CreateNextAsync();
        target.Content = "# Spaced repetition\n\nReview at growing intervals.";
        await _repo.SaveAsync(target);
        var source = await CreateNextAsync();
        source.Content = $"see [[{target.Id}]]";
        await _repo.SaveAsync(source);

        Assert.Equal("Spaced repetition", (await _repo.GetSummariesAsync([target.Id]))[0].Title);
        Assert.Equal("Spaced repetition", Assert.Single(await _repo.GetOutgoingLinksAsync(source.Id)).Title);
        Assert.Equal("see [[" + target.Id + "]]", Assert.Single(await _repo.GetBacklinksAsync(target.Id)).Title);
    }

    [Fact]
    public async Task Create_twice_in_same_second_yields_distinct_ids()
    {
        var a = await _repo.CreateAsync();
        var b = await _repo.CreateAsync();
        var c = await _repo.CreateAsync();
        Assert.Equal(new[] { "20261102143015", "20261102143016", "20261102143017" }, new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public async Task Save_and_get_round_trip_normalizes_tags()
    {
        var target = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Content = "# Spaced repetition\nReview at growing intervals.";
        note.Tags = ["Learning", "learning ", "Deep Work", "  "];
        note.ManualLinks = [target.Id];
        _time.Advance(TimeSpan.FromMinutes(1));

        await _repo.SaveAsync(note);
        var loaded = (await _repo.GetAsync(note.Id))!;

        Assert.Equal("Spaced repetition", loaded.Title);
        Assert.Equal("# Spaced repetition\nReview at growing intervals.", loaded.Content);
        Assert.Equal(new[] { "deep-work", "learning" }, loaded.Tags.Order());
        Assert.Equal(new[] { target.Id }, loaded.ManualLinks);
        Assert.Equal(_time.GetLocalNow().ToUnixTimeMilliseconds(), loaded.UpdatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Save_rebuilds_inline_links_and_keeps_manual_links()
    {
        var a = await CreateNextAsync();
        var b = await CreateNextAsync();
        var c = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Content = $"see [[{a.Id}]]";
        note.ManualLinks = [c.Id];
        await _repo.SaveAsync(note);

        note.Content = $"now [[{b.Id}]]";
        await _repo.SaveAsync(note);

        var links = await _repo.GetOutgoingLinksAsync(note.Id);
        Assert.Contains(links, l => l.NoteId == b.Id && l.Kind == LinkKind.Inline);
        Assert.Contains(links, l => l.NoteId == c.Id && l.Kind == LinkKind.Manual);
        Assert.DoesNotContain(links, l => l.NoteId == a.Id);
    }

    [Fact]
    public async Task Save_with_duplicate_and_self_links_succeeds()
    {
        var a = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Content = $"[[{a.Id}]] [[{a.Id}]] [[{note.Id}]]";
        note.ManualLinks = [a.Id, a.Id, note.Id, "not-an-id"];

        await _repo.SaveAsync(note);

        var links = await _repo.GetOutgoingLinksAsync(note.Id);
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal(a.Id, l.NoteId));
        Assert.Equal(new[] { a.Id }, (await _repo.GetAsync(note.Id))!.ManualLinks);
    }

    [Fact]
    public async Task Backlinks_include_inline_and_manual()
    {
        var target = await CreateNextAsync();
        var inline = await CreateNextAsync();
        var manual = await CreateNextAsync();
        inline.Content = $"Inline\n[[{target.Id}]]";
        manual.Content = "Manual";
        manual.ManualLinks = [target.Id];
        await _repo.SaveAsync(inline);
        await _repo.SaveAsync(manual);

        var back = await _repo.GetBacklinksAsync(target.Id);
        Assert.Contains(back, l => l.NoteId == inline.Id && l.Kind == LinkKind.Inline && l.Title == "Inline");
        Assert.Contains(back, l => l.NoteId == manual.Id && l.Kind == LinkKind.Manual);
    }

    [Fact]
    public async Task Broken_link_resolves_when_target_is_created()
    {
        var note = await CreateNextAsync();
        var futureId = _time.GetLocalNow().AddSeconds(10).ToString("yyyyMMddHHmmss");
        note.Content = $"[[{futureId}]]";
        await _repo.SaveAsync(note);
        Assert.True(Assert.Single(await _repo.GetOutgoingLinksAsync(note.Id)).IsBroken);

        var created = await CreateNextAsync();
        Assert.Equal(futureId, created.Id);
        Assert.False(Assert.Single(await _repo.GetOutgoingLinksAsync(note.Id)).IsBroken);
    }

    [Fact]
    public async Task Save_updates_fts_and_delete_removes_it()
    {
        var note = await CreateNextAsync();
        note.Content = "Zettel\nforgetting curve";
        note.Tags = ["memory"];
        await _repo.SaveAsync(note);

        await using (var ctx = _db.Factory.CreateDbContext())
        {
            Assert.Equal(new[] { note.Id }, await FtsIndex.MatchAsync(ctx, "\"forget\"*", 10, default));
            Assert.Equal(new[] { note.Id }, await FtsIndex.MatchAsync(ctx, "\"memory\"*", 10, default));
        }

        await _repo.DeleteAsync(note.Id);
        await using (var ctx = _db.Factory.CreateDbContext())
            Assert.Empty(await FtsIndex.MatchAsync(ctx, "\"forget\"*", 10, default));
    }

    [Fact]
    public async Task Save_reembeds_only_when_text_changes()
    {
        var note = await CreateNextAsync();
        note.Content = "t";
        await _repo.SaveAsync(note);
        var count = _embeddings.EmbedCount;
        Assert.True(_index.Count >= 1);

        note.Tags = ["new-tag"];          // tag embedding is computed, note text unchanged
        await _repo.SaveAsync(note);
        Assert.Equal(count + 1, _embeddings.EmbedCount);

        note.Content = "changed";
        await _repo.SaveAsync(note);
        Assert.Equal(count + 2, _embeddings.EmbedCount);
    }

    [Fact]
    public async Task Save_without_model_still_saves()
    {
        _embeddings.IsAvailable = false;
        var note = await CreateNextAsync();
        note.Content = "offline";
        note.Tags = ["t"];
        await _repo.SaveAsync(note);
        Assert.Equal("offline", (await _repo.GetAsync(note.Id))!.Title);
        Assert.Equal(0, _index.Count);
    }

    [Fact]
    public async Task Delete_keeps_incoming_links_as_broken()
    {
        var target = await CreateNextAsync();
        var source = await CreateNextAsync();
        source.Content = $"[[{target.Id}]]";
        target.Content = $"[[{source.Id}]]";
        await _repo.SaveAsync(source);
        await _repo.SaveAsync(target);

        await _repo.DeleteAsync(target.Id);

        Assert.Null(await _repo.GetAsync(target.Id));
        Assert.True(Assert.Single(await _repo.GetOutgoingLinksAsync(source.Id)).IsBroken);
        Assert.Equal(0L, await _db.ScalarAsync($"SELECT COUNT(*) FROM NoteLinks WHERE SourceId = '{target.Id}'"));
    }

    [Fact]
    public async Task Failed_save_rolls_back_everything()
    {
        var note = await CreateNextAsync();
        note.Content = "before";
        await _repo.SaveAsync(note);
        await _db.ExecAsync("DROP TABLE NotesFts;"); // force a failure late in the transaction

        note.Content = "after";
        note.Tags = ["x"];
        await Assert.ThrowsAnyAsync<Exception>(() => _repo.SaveAsync(note));

        var loaded = (await _repo.GetAsync(note.Id))!;
        Assert.Equal("before", loaded.Title);
        Assert.Empty(loaded.Tags);
    }
}
