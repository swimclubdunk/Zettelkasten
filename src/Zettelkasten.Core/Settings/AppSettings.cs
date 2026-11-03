namespace Zettelkasten.Core.Settings;

public enum ThemePreference { System, Light, Dark }

public enum AppTheme { Light, Dark }

/// <summary>Per-user preferences, stored next to the database.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
}

public static class ThemeResolver
{
    public static AppTheme Resolve(ThemePreference preference, bool systemUsesLight) => preference switch
    {
        ThemePreference.Light => AppTheme.Light,
        ThemePreference.Dark => AppTheme.Dark,
        _ => systemUsesLight ? AppTheme.Light : AppTheme.Dark,
    };
}
