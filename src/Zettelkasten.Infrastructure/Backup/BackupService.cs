using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Backup;

public sealed class BackupService(AppPaths paths) : IBackupService
{
    public Task ExportAsync(string destinationPath, CancellationToken ct = default) =>
        Task.Run(() => SqliteBackup.Copy(paths.ConnectionString, destinationPath), ct);
}
