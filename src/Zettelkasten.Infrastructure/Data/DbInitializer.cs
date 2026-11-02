using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Model;

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
        if (pending.Any(m => m.EndsWith("_MergeTitlesIntoContent", StringComparison.Ordinal)))
            await RefreshDerivedTitlesAsync(db, ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }

    /// <summary>Recomputes each note's cached first-line title and its search-index row from the content.</summary>
    private static async Task RefreshDerivedTitlesAsync(AppDbContext db, CancellationToken ct)
    {
        var notes = await db.Notes.Include(n => n.NoteTags).ThenInclude(nt => nt.Tag).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var note in notes) note.Title = NoteTitle.FromContent(note.Content);
        await db.SaveChangesAsync(ct);
        foreach (var note in notes)
            await FtsIndex.UpsertAsync(db, note.Id, note.Title, note.Content, note.NoteTags.Select(nt => nt.Tag!.Name), ct);
        await tx.CommitAsync(ct);
    }
}
