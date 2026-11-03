using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Zettelkasten.Core.Settings;
using Zettelkasten.Infrastructure.Settings;

namespace Zettelkasten.App.Services;

public interface IThemeService
{
    ThemePreference Preference { get; }
    AppTheme Current { get; }
    void SetPreference(ThemePreference preference);
    /// <summary>Raised on the UI thread after the palette was swapped.</summary>
    event EventHandler? ThemeChanged;
}

/// <summary>Swaps the Colors.*.xaml palette in the application resources and keeps title bars in step.
/// "System" follows the Windows app theme, including changes while the app runs.</summary>
public sealed class ThemeService(SettingsStore store, ILogger<ThemeService> logger) : IThemeService, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkMode = 20;

    private AppSettings _settings = new();
    private bool _initialized;

    public ThemePreference Preference => _settings.Theme;
    public AppTheme Current { get; private set; } = AppTheme.Light;
    public event EventHandler? ThemeChanged;

    /// <summary>Applies the saved preference; call once on the UI thread before the first window is shown.</summary>
    public void Initialize()
    {
        _settings = store.Load();
        Apply(force: true);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _initialized = true;
    }

    public void SetPreference(ThemePreference preference)
    {
        if (preference == _settings.Theme) return;
        _settings = _settings with { Theme = preference };
        store.Save(_settings);
        Apply(force: false);
    }

    private void Apply(bool force)
    {
        var theme = ThemeResolver.Resolve(_settings.Theme, SystemUsesLightTheme());
        if (!force && theme == Current) return;
        Current = theme;

        var app = Application.Current;
        if (app is null) return;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{typeof(ThemeService).Assembly.GetName().Name};component/Themes/Colors.{theme}.xaml"),
        };
        var dictionaries = app.Resources.MergedDictionaries;
        var index = dictionaries.ToList().FindIndex(d => d.Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true);
        if (index >= 0) dictionaries[index] = palette;
        else dictionaries.Insert(0, palette);

        foreach (Window window in app.Windows) ApplyTitleBar(window);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_initialized || e.Category != UserPreferenceCategory.General || _settings.Theme != ThemePreference.System) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(force: false));
    }

    private bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the Windows app theme; assuming light");
            return true;
        }
    }

    private void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = Current == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)); // no-op before Windows 10 20H1
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
}
