using Microsoft.Extensions.Logging.Abstractions;
using Zettelkasten.Core.Settings;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Settings;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(TestDb.NewDirectory(), "models");
    private SettingsStore Store() => new(_paths, NullLogger<SettingsStore>.Instance);

    public void Dispose()
    {
        try { Directory.Delete(_paths.DataDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(ThemePreference.System, Store().Load().Theme);
    }

    [Fact]
    public void Saved_settings_round_trip_as_readable_json()
    {
        Store().Save(new AppSettings { Theme = ThemePreference.Dark });
        Assert.Equal(ThemePreference.Dark, Store().Load().Theme);
        Assert.Contains("\"Dark\"", File.ReadAllText(_paths.SettingsPath));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsPath, "{ not json");
        Assert.Equal(ThemePreference.System, Store().Load().Theme);
    }
}
