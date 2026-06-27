using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class RecoveryServiceHooks
{
    public Func<BackupFileRequest, string, CancellationToken, Task>? AfterBackupFileCopiedAsync { get; init; }
    public Func<Guid, CancellationToken, Task>? BeforePersistManifestAsync { get; init; }
    public Func<RollbackFileEntry, int, CancellationToken, Task>? AfterRollbackFileProcessedAsync { get; init; }
}
