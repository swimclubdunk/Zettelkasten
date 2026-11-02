using Microsoft.Data.Sqlite;
using Zettelkasten.Infrastructure.Backup;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class BackupServiceTests
{
    [Fact]
    public async Task Export_writes_a_consistent_copy_while_db_is_open()
    {
        await using var db = await TestDb.CreateAsync();
        var notes = new NoteRepository(db.Factory, new FakeEmbeddingService(), new VectorIndex(), TimeProvider.System);
        var n = await notes.CreateAsync();
        n.Content = "keep me";
        await notes.SaveAsync(n);

        await using var open = new SqliteConnection(db.Paths.ConnectionString); // simulate the app holding a connection
        await open.OpenAsync();

        var target = Path.Combine(db.Paths.DataDirectory, "export", "backup.db");
        await new BackupService(db.Paths).ExportAsync(target);

        await using var c = new SqliteConnection($"Data Source={target};Pooling=False");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Title FROM Notes";
        Assert.Equal("keep me", await cmd.ExecuteScalarAsync());
    }
}
