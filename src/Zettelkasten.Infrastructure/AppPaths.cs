using Microsoft.Data.Sqlite;

namespace Zettelkasten.Infrastructure;

public sealed class AppPaths(string dataDirectory, string modelDirectory)
{
    public string DataDirectory { get; } = dataDirectory;
    public string ModelDirectory { get; } = modelDirectory;
    public string DatabasePath => Path.Combine(DataDirectory, "zettelkasten.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string ModelPath => Path.Combine(ModelDirectory, "model.onnx");
    public string VocabPath => Path.Combine(ModelDirectory, "vocab.txt");

    public string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();

    /// <summary>Set ZETTELKASTEN_DATA to run against another data folder (a demo or test box) without touching your notes.</summary>
    public const string DataDirectoryVariable = "ZETTELKASTEN_DATA";

    public static AppPaths Default() => new(
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zettelkasten"),
        Path.Combine(AppContext.BaseDirectory, "models"));
}
