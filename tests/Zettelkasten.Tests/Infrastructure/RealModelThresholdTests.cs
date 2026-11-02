using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

/// <summary>Checks the SemanticSettings thresholds against the real model (skipped without it).</summary>
public class RealModelThresholdTests(ITestOutputHelper output)
{
    private static readonly AppPaths ModelPaths = new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "models"));

    private static async Task<OnnxEmbeddingService?> LoadModelAsync()
    {
        var s = new OnnxEmbeddingService(ModelPaths, NullLogger<OnnxEmbeddingService>.Instance);
        await s.InitializeAsync();
        return s.IsAvailable ? s : null;
    }

    [SkippableFact]
    public async Task Meaning_only_query_finds_related_note_but_not_unrelated_ones()
    {
        using var model = await LoadModelAsync();
        Skip.If(model is null, "Run scripts/fetch-model.ps1 first.");

        await using var db = await TestDb.CreateAsync();
        var index = new VectorIndex();
        var notes = new NoteRepository(db.Factory, model, index, TimeProvider.System);
        async Task<string> Add(string title, string content)
        {
            var n = await notes.CreateAsync();
            n.Content = $"{title}\n{content}";
            await notes.SaveAsync(n);
            await Task.Delay(1100); // distinct second-resolution IDs
            return n.Id;
        }
        var memory = await Add("Spaced repetition and the forgetting curve", "Reviewing at growing intervals keeps facts in long-term recall.");
        var bread = await Add("Sourdough bread", "Feed the starter, fold the dough, bake at 250 degrees.");
        var tax = await Add("Quarterly tax deadlines", "VAT returns are due ten days after the quarter ends.");

        var query = model.Embed("how memory works");
        foreach (var (id, label) in new[] { (memory, "memory"), (bread, "bread"), (tax, "tax") })
            output.WriteLine($"{label}: {index.Search(query, -1f, 10).Single(h => h.Id == id).Score:0.000}");

        var results = await new SearchService(db.Factory, notes, model, index, NullLogger<SearchService>.Instance)
            .SearchAsync("how memory works");
        Assert.Equal(memory, results[0].Note.Id);
        Assert.True(results[0].MatchedSemantic);
        Assert.DoesNotContain(results, r => r.Note.Id == bread || r.Note.Id == tax);
    }

    [SkippableFact]
    public async Task Tag_suggestion_scores_for_typical_near_duplicates()
    {
        using var model = await LoadModelAsync();
        Skip.If(model is null, "Run scripts/fetch-model.ps1 first.");

        string[] existing = ["machine-learning", "cooking", "productivity", "psychology"];
        string[] inputs = ["machine learning ops", "deep learning", "ml", "recipes", "time management", "psychological"];
        foreach (var input in inputs)
        {
            var q = model.Embed(ContentHash.TagText(input.Replace(' ', '-')));
            var scores = existing.Select(e => $"{e}={VectorMath.Dot(q, model.Embed(ContentHash.TagText(e))):0.00}");
            output.WriteLine($"{input}: {string.Join(", ", scores)}");
        }

        var tags = new TagRepositoryStub(existing.Select((n, i) =>
            new Zettelkasten.Core.Model.TagWithEmbedding(new Zettelkasten.Core.Model.Tag(i + 1, n), model.Embed(ContentHash.TagText(n)))).ToList());
        var suggestions = new TagSuggestionService(tags, model);
        Assert.Contains((await suggestions.SuggestAsync("machine learning ops")).Similar, s => s.Tag.Name == "machine-learning");
        Assert.Contains((await suggestions.SuggestAsync("deep learning")).Similar, s => s.Tag.Name == "machine-learning");
        Assert.Contains((await suggestions.SuggestAsync("psychological")).Similar, s => s.Tag.Name == "psychology");
        Assert.Contains((await suggestions.SuggestAsync("ml")).Similar, s => s.Tag.Name == "machine-learning");
        Assert.DoesNotContain((await suggestions.SuggestAsync("time management")).Similar, s => s.Tag.Name == "machine-learning");
        Assert.DoesNotContain((await suggestions.SuggestAsync("recipes")).Similar, s => s.Tag.Name == "machine-learning");
    }

    private sealed class TagRepositoryStub(IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding> all) : Zettelkasten.Core.Abstractions.ITagRepository
    {
        public Task<IReadOnlyList<Zettelkasten.Core.Model.Tag>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Zettelkasten.Core.Model.Tag>>(all.Select(t => t.Tag).ToList());
        public Task<IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult(all);
        public Task DeleteAsync(int tagId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
