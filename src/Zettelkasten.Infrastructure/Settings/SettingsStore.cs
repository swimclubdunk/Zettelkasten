using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Settings;

namespace Zettelkasten.Infrastructure.Settings;

/// <summary>Reads and writes settings.json. A missing or unreadable file means defaults; settings never block startup.</summary>
public sealed class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(paths.SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsPath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read {Path}; using default settings", paths.SettingsPath);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(paths.SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save {Path}", paths.SettingsPath);
        }
    }
}
