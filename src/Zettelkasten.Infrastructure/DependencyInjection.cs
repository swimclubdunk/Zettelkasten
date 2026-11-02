using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure.Backup;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Infrastructure.Tags;

namespace Zettelkasten.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddZettelkastenInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(paths.ConnectionString));

        services.AddSingleton<VectorIndex>();
        services.AddSingleton<OnnxEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<OnnxEmbeddingService>());

        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ITagSuggestionService, TagSuggestionService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<DbInitializer>();
        services.AddSingleton<EmbeddingBackfillService>();
        return services;
    }
}
