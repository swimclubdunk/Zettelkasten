using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class DependencyInjectionTests
{
    [Fact]
    public void All_services_resolve()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddZettelkastenInfrastructure(new AppPaths(Path.GetTempPath(), Path.GetTempPath()));
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(sp.GetRequiredService<INoteRepository>());
        Assert.NotNull(sp.GetRequiredService<ITagRepository>());
        Assert.NotNull(sp.GetRequiredService<ISearchService>());
        Assert.NotNull(sp.GetRequiredService<ITagSuggestionService>());
        Assert.NotNull(sp.GetRequiredService<IBackupService>());
        Assert.NotNull(sp.GetRequiredService<DbInitializer>());
        Assert.NotNull(sp.GetRequiredService<EmbeddingBackfillService>());
        Assert.Same(sp.GetRequiredService<OnnxEmbeddingService>(), sp.GetRequiredService<IEmbeddingService>());
    }
}
