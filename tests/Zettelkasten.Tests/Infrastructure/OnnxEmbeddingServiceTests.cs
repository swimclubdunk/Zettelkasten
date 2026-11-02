using Microsoft.Extensions.Logging.Abstractions;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class OnnxEmbeddingServiceTests
{
    private static readonly AppPaths RealPaths = new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "models"));
    private static bool ModelPresent => File.Exists(RealPaths.ModelPath) && File.Exists(RealPaths.VocabPath);

    private static async Task<OnnxEmbeddingService> LoadAsync()
    {
        var s = new OnnxEmbeddingService(RealPaths, NullLogger<OnnxEmbeddingService>.Instance);
        await s.InitializeAsync();
        return s;
    }

    [Fact]
    public async Task Missing_model_means_unavailable_without_throwing()
    {
        var paths = new AppPaths(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "no-model-here"));
        using var s = new OnnxEmbeddingService(paths, NullLogger<OnnxEmbeddingService>.Instance);
        var raised = false;
        s.AvailabilityChanged += (_, _) => raised = true;

        await s.InitializeAsync();

        Assert.False(s.IsAvailable);
        Assert.False(raised);
        Assert.Throws<InvalidOperationException>(() => s.Embed("x"));
    }

    [SkippableFact]
    public async Task Real_model_produces_normalized_384_dim_vectors()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        Assert.True(s.IsAvailable);
        var v = s.Embed("Spaced repetition helps memory.");
        Assert.Equal(SemanticSettings.Dimensions, v.Length);
        Assert.Equal(1f, VectorMath.Dot(v, v), 3);
    }

    [SkippableFact]
    public async Task Related_meanings_are_closer_than_unrelated()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        var ml = s.Embed("machine learning");
        Assert.True(VectorMath.Dot(ml, s.Embed("neural networks")) > VectorMath.Dot(ml, s.Embed("cooking recipes")));
        var memory = s.Embed("how memory works");
        Assert.True(VectorMath.Dot(memory, s.Embed("spaced repetition and the forgetting curve"))
                    > VectorMath.Dot(memory, s.Embed("tax return deadline")));
    }

    [SkippableFact]
    public async Task Long_text_is_truncated_not_rejected()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        var v = s.Embed(string.Concat(Enumerable.Repeat("long note text ", 2000)));
        Assert.Equal(SemanticSettings.Dimensions, v.Length);
    }
}
