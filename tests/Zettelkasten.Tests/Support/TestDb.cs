using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Tests.Support;

public sealed class TestDb : IAsyncDisposable
{
    private TestDb(AppPaths paths)
    {
        Paths = paths;
        Factory = new Factory_(paths.ConnectionString);
    }

    public AppPaths Paths { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }

    public static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "zk-tests", Guid.NewGuid().ToString("N"));

    public static async Task<TestDb> CreateAsync(TimeProvider? time = null, string? directory = null)
    {
        var db = new TestDb(new AppPaths(directory ?? NewDirectory(), Path.Combine(AppContext.BaseDirectory, "models")));
        await new DbInitializer(db.Factory, db.Paths, time ?? TimeProvider.System, NullLogger<DbInitializer>.Instance)
            .InitializeAsync();
        return db;
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var c = new SqliteConnection(Paths.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    public async Task ExecAsync(string sql)
    {
        await using var c = new SqliteConnection(Paths.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Paths.DataDirectory, recursive: true); } catch (IOException) { }
        return ValueTask.CompletedTask;
    }

    private sealed class Factory_(string cs) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(AppDbContext.CreateOptions(cs));
    }
}
