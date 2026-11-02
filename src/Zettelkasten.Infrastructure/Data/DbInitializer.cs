using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Zettelkasten.Infrastructure.Data;

public sealed class DbInitializer(
    IDbContextFactory<AppDbContext> factory, AppPaths paths, TimeProvider time, ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var existed = File.Exists(paths.DatabasePath);

        await using var db = await factory.CreateDbContextAsync(ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (existed && pending.Count > 0)
        {
            var stamp = time.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var backupPath = $"{paths.DatabasePath}.bak-{stamp}";
            SqliteBackup.Copy(paths.ConnectionString, backupPath);
            logger.LogInformation("Backed up database to {BackupPath} before applying {Count} migration(s)",
                backupPath, pending.Count);
        }

        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
