using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class SearchServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;
    private SearchService _search = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, _index, _time);
        _search = new SearchService(_db.Factory, _notes, _embeddings, _index, NullLogger<SearchService>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Note> AddAsync(string title, string content = "", params string[] tags)
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var n = await _notes.CreateAsync();
        n.Content = $"{title}\n{content}";
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
        return n;
    }

    [Fact]
    public async Task Empty_query_returns_most_recent_first()
    {
        var older = await AddAsync("older");
        var newer = await AddAsync("newer");
        var results = await _search.SearchAsync("   ");
        Assert.Equal(new[] { newer.Id, older.Id }, results.Select(r => r.Note.Id));
        Assert.All(results, r => Assert.False(r.MatchedKeyword || r.MatchedSemantic));
    }

    [Fact]
    public async Task Keyword_prefix_match_is_flagged()
    {
        var hit = await AddAsync("Spaced repetition", "review intervals");
        await AddAsync("Cooking", "pasta");
        var r = Assert.Single(await _search.SearchAsync("repet"));
        Assert.Equal(hit.Id, r.Note.Id);
        Assert.True(r.MatchedKeyword);
    }

    [Fact]
    public async Task Keyword_search_ignores_diacritics()
    {
        var accented = await AddAsync("Café culture");
        var plain = await AddAsync("Naive cafe list");
        Assert.Equal(2, (await _search.SearchAsync("cafe")).Count);
        Assert.Contains(await _search.SearchAsync("CAFÉ"), r => r.Note.Id == plain.Id);
        Assert.Contains(await _search.SearchAsync("café"), r => r.Note.Id == accented.Id);
    }

    [Fact]
    public async Task Semantic_only_match_is_found_and_flagged()
    {
        _embeddings.Synonyms["memory"] = "recall";
        var n = await AddAsync("Total recall");
        var r = Assert.Single(await _search.SearchAsync("memory"));
        Assert.Equal(n.Id, r.Note.Id);
        Assert.True(r.MatchedSemantic);
        Assert.False(r.MatchedKeyword);
    }

    [Fact]
    public async Task Note_matching_both_ways_ranks_first()
    {
        _embeddings.Synonyms["memory"] = "recall";
        var semanticOnly = await AddAsync("Total recall");
        var both = await AddAsync("Memory palace");
        var results = await _search.SearchAsync("memory");
        Assert.Equal(both.Id, results[0].Note.Id);
        Assert.True(results[0].MatchedKeyword && results[0].MatchedSemantic);
        Assert.Contains(results, r => r.Note.Id == semanticOnly.Id);
    }

    [Fact]
    public async Task Tag_filter_restricts_results_and_works_alone()
    {
        var tagged = await AddAsync("memory one", "", "learning", "psych");
        await AddAsync("memory two", "", "learning");
        Assert.Equal(new[] { tagged.Id }, (await _search.SearchAsync("memory #learning #Psych")).Select(r => r.Note.Id));
        Assert.Equal(2, (await _search.SearchAsync("#learning")).Count);
        Assert.Empty(await _search.SearchAsync("#nonexistent"));
    }

    [Fact]
    public async Task Id_prefix_lists_and_filters()
    {
        var a = await AddAsync("alpha");
        var b = await AddAsync("beta");
        Assert.Equal(new[] { b.Id, a.Id }, (await _search.SearchAsync("id:2026")).Select(r => r.Note.Id));
        Assert.Equal(new[] { a.Id }, (await _search.SearchAsync($"id:{a.Id}")).Select(r => r.Note.Id));
        Assert.Empty(await _search.SearchAsync("alpha id:1999"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"unbalanced")]
    [InlineData("*")]
    [InlineData("a*b")]
    [InlineData("-memory")]
    [InlineData("title:memory")]
    [InlineData("(memory")]
    [InlineData("memory AND")]
    [InlineData("NOT")]
    [InlineData("NEAR(a b)")]
    [InlineData("^")]
    [InlineData("!!!???")]
    [InlineData("🧠")]
    public async Task Search_never_throws_on_fts_syntax(string query)
    {
        await AddAsync("memory", "content");
        var ex = await Record.ExceptionAsync(() => _search.SearchAsync(query));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Works_keyword_only_without_model()
    {
        var n = await AddAsync("Zettel");
        _embeddings.IsAvailable = false;
        var r = Assert.Single(await _search.SearchAsync("zettel"));
        Assert.Equal(n.Id, r.Note.Id);
        Assert.False(r.MatchedSemantic);
    }
}
