using Zettelkasten.App.Services;
using Zettelkasten.Core.Settings;

namespace Zettelkasten.Tests.Support;

public sealed class FakeThemeService : IThemeService
{
    public ThemePreference Preference { get; private set; } = ThemePreference.System;
    public AppTheme Current => Preference == ThemePreference.Dark ? AppTheme.Dark : AppTheme.Light;
    public event EventHandler? ThemeChanged;

    public void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
