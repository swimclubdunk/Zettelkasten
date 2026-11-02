using Microsoft.Data.Sqlite;

namespace Zettelkasten.Infrastructure.Data;

public static class SqliteBackup
{
    /// <summary>Consistent copy via SQLite's online backup API (safe while the DB is in use).</summary>
    public static void Copy(string sourceConnectionString, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var source = new SqliteConnection(sourceConnectionString);
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }
}
