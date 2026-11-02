using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.App;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Infrastructure;

namespace Zettelkasten.Tests.App;

public class AppServicesTests
{
    [Fact]
    public void Main_view_model_resolves_with_shared_children()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddZettelkastenApp(new AppPaths(Path.GetTempPath(), Path.GetTempPath()));
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var main = sp.GetRequiredService<MainViewModel>();
        Assert.Same(main.Editor, sp.GetRequiredService<NoteEditorViewModel>());
        Assert.Same(main.Panel, sp.GetRequiredService<NotePanelViewModel>());
        Assert.Same(main.Search, sp.GetRequiredService<SearchViewModel>());
    }
}
