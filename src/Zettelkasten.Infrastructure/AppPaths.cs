using Microsoft.Data.Sqlite;

namespace Zettelkasten.Infrastructure;

public sealed class AppPaths(string dataDirectory, string modelDirectory)
{
    public string DataDirectory { get; } = dataDirectory;
    public string ModelDirectory { get; } = modelDirectory;
    public string DatabasePath => Path.Combine(DataDirectory, "zettelkasten.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string ModelPath => Path.Combine(ModelDirectory, "model.onnx");
    public string VocabPath => Path.Combine(ModelDirectory, "vocab.txt");

    public string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();

    public static AppPaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zettelkasten"),
        Path.Combine(AppContext.BaseDirectory, "models"));
}
