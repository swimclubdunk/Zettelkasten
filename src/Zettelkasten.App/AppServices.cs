using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.App.Services;
using Zettelkasten.App.ViewModels;
using Zettelkasten.App.Views;
using Zettelkasten.Infrastructure;

namespace Zettelkasten.App;

public static class AppServices
{
    public static IServiceCollection AddZettelkastenApp(this IServiceCollection services, AppPaths paths)
    {
        services.AddZettelkastenInfrastructure(paths);
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<NoteEditorViewModel>();
        services.AddSingleton<NotePanelViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
