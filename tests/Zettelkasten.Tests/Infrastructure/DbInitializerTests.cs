using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class DbInitializerTests
{
    [Fact]
    public async Task Creates_schema_including_fts_table()
    {
        await using var db = await TestDb.CreateAsync();
        var count = await db.ScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Notes','Tags','NoteTags','NoteLinks','NoteEmbeddings','NotesFts')");
        Assert.Equal(6L, count);
    }

    [Fact]
    public async Task Enables_wal_mode()
    {
        await using var db = await TestDb.CreateAsync();
        Assert.Equal("wal", await db.ScalarAsync("PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task Backs_up_existing_database_before_migrating_once()
    {
        var dir = TestDb.NewDirectory();
        Directory.CreateDirectory(dir);
        await using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "zettelkasten.db")};Pooling=False"))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE Legacy(x INTEGER);";
            await cmd.ExecuteNonQueryAsync();
        }
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

        await using var db = await TestDb.CreateAsync(time, dir);
        Assert.True(File.Exists(Path.Combine(dir, "zettelkasten.db.bak-20261102090000")));

        time.Advance(TimeSpan.FromMinutes(1));
        await TestDb.CreateAsync(time, dir); // second start: nothing pending -> no new backup
        Assert.Single(Directory.GetFiles(dir, "*.bak-*"));
    }
}
