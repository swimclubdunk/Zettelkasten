using Zettelkasten.Core.Settings;

namespace Zettelkasten.Tests.Core;

public sealed class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemePreference.System, true, AppTheme.Light)]
    [InlineData(ThemePreference.System, false, AppTheme.Dark)]
    [InlineData(ThemePreference.Light, false, AppTheme.Light)]
    [InlineData(ThemePreference.Dark, true, AppTheme.Dark)]
    public void Resolves_the_effective_theme(ThemePreference preference, bool systemUsesLight, AppTheme expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(preference, systemUsesLight));
}
