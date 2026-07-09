using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class RollbackService : IRollbackService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IRollbackVerificationService _rollbackVerificationService;
    private readonly IOperationHistoryService _historyService;
    private readonly RecoveryServiceHooks? _hooks;

    public RollbackService(IRollbackVerificationService rollbackVerificationService, IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(), rollbackVerificationService, historyService, null)
    {
    }

    public RollbackService(
        string backupsRoot,
        IRollbackVerificationService rollbackVerificationService,
        IOperationHistoryService historyService,
        RecoveryServiceHooks? hooks = null)
        : this(new RecoveryStorageLayout(backupsRoot), rollbackVerificationService, historyService, hooks)
    {
    }

    internal RollbackService(
        RecoveryStorageLayout layout,
        IRollbackVerificationService rollbackVerificationService,
        IOperationHistoryService historyService,
        RecoveryServiceHooks? hooks)
    {
        _layout = layout;
        _rollbackVerificationService = rollbackVerificationService;
        _historyService = historyService;
        _hooks = hooks;
    }

    public async Task<RollbackTransaction> SaveTransactionAsync(RollbackTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.OperationId == Guid.Empty)
            throw new InvalidOperationException("A rollback transaction requires a valid operation ID.");

        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(_layout.GetOperationPath(transaction.OperationId), cancellationToken);
        var manifest = await AtomicJsonFileStore.ReadRequiredAsync<BackupManifest>(_layout.GetManifestPath(transaction.OperationId), cancellationToken);
        var manifestPaths = manifest.Files.ToDictionary(file => file.RelativeBackupPath, StringComparer.OrdinalIgnoreCase);
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in transaction.Files)
        {
            if (file.Protected)
                throw new InvalidOperationException($"Protected files cannot enter a rollback transaction: {file.TargetPath}");

            var targetPath = RecoveryStorageLayout.ValidateAbsoluteTargetPath(file.TargetPath);
            if (!targetPaths.Add(targetPath))
                throw new InvalidOperationException($"Duplicate rollback target detected: {targetPath}");

            if (!manifestPaths.TryGetValue(file.RelativeBackupPath, out var manifestEntry))
                throw new InvalidOperationException($"The rollback entry {file.RelativeBackupPath} does not exist in the backup manifest.");

            if (!manifestEntry.OriginalSha256.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The rollback entry hash does not match the backup manifest for {file.TargetPath}.");
        }

        await AtomicJsonFileStore.WriteAsync(
            _layout.GetRollbackPath(transaction.OperationId),
            transaction with { Status = RollbackTransactionStatus.Available, StartedAtUtc = null, CompletedAtUtc = null, LastError = null },
            value => value.OperationId == transaction.OperationId,
            cancellationToken);

        var updatedOperation = operation with
        {
            RollbackStatus = RollbackTransactionStatus.Available,
            HasRollbackTransaction = true,
            AffectedFileCount = manifest.Files.Count,
        };
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetOperationPath(transaction.OperationId),
            updatedOperation,
            value => value.OperationId == transaction.OperationId,
            cancellationToken);
        await _historyService.RefreshOperationAsync(transaction.OperationId, cancellationToken);

        return await AtomicJsonFileStore.ReadRequiredAsync<RollbackTransaction>(_layout.GetRollbackPath(transaction.OperationId), cancellationToken);
    }

    public async Task<RollbackResult> ExecuteAsync(Guid operationId, RollbackExecutionOptions options, CancellationToken cancellationToken)
    {
        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(_layout.GetOperationPath(operationId), cancellationToken);
        var manifest = await AtomicJsonFileStore.ReadRequiredAsync<BackupManifest>(_layout.GetManifestPath(operationId), cancellationToken);
        var transaction = await AtomicJsonFileStore.ReadRequiredAsync<RollbackTransaction>(_layout.GetRollbackPath(operationId), cancellationToken);
        var manifestByPath = manifest.Files.ToDictionary(file => file.RelativeBackupPath, StringComparer.OrdinalIgnoreCase);
        var files = transaction.Files.ToList();
        var conflicts = new List<RollbackConflict>();

        transaction = transaction with
        {
            Status = RollbackTransactionStatus.InProgress,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = null,
            LastError = null,
        };
        await PersistRollbackStateAsync(operation with { RollbackStatus = RollbackTransactionStatus.InProgress }, transaction, cancellationToken);

        var onlyTargetPaths = options.OnlyTargetPaths is { } configured
            ? new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase)
            : null;

        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[index];

                RollbackFileEntry updatedFile;
                if (onlyTargetPaths is not null && !onlyTargetPaths.Contains(file.TargetPath))
                {
                    updatedFile = file with
                    {
                        Status = RollbackFileStatus.Skipped,
                        Message = "Not included in this restore selection.",
                    };
                }
                else if (file.ApplyResultStatus != ApplyResultStatus.Applied)
                {
                    updatedFile = file with
                    {
                        Status = RollbackFileStatus.Skipped,
                        Message = "Apply did not complete for this file, so rollback is not required.",
                    };
                }
                else
                {
                    updatedFile = await ProcessFileAsync(operationId, manifestByPath, file, options, conflicts, cancellationToken);
                }

                files[index] = updatedFile;
                transaction = transaction with { Files = files.ToArray() };
                var interimOperation = operation with
                {
                    RollbackStatus = RollbackStatusCalculator.Calculate(transaction.Files),
                    ConflictCount = transaction.Files.Count(entry => entry.Status == RollbackFileStatus.Conflict),
                    FailureCount = transaction.Files.Count(entry => entry.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup),
                };
                await PersistRollbackStateAsync(interimOperation, transaction, cancellationToken);

                if (_hooks?.AfterRollbackFileProcessedAsync is not null)
                    await _hooks.AfterRollbackFileProcessedAsync(updatedFile, index, cancellationToken);
            }

            var completedStatus = RollbackStatusCalculator.Calculate(transaction.Files);
            transaction = transaction with
            {
                Files = files.ToArray(),
                Status = completedStatus,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                LastError = completedStatus is RollbackTransactionStatus.Completed or RollbackTransactionStatus.CompletedWithConflicts
                    ? null
                    : string.Join(Environment.NewLine, files.Where(file => file.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup).Select(file => file.Message).Where(message => !string.IsNullOrWhiteSpace(message))),
            };
            operation = operation with
            {
                RollbackStatus = completedStatus,
                ConflictCount = transaction.Files.Count(entry => entry.Status == RollbackFileStatus.Conflict),
                FailureCount = transaction.Files.Count(entry => entry.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup),
                HasRollbackTransaction = true,
            };
            await PersistRollbackStateAsync(operation, transaction, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            transaction = transaction with
            {
                Files = files.ToArray(),
                Status = RollbackTransactionStatus.Cancelled,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                LastError = "Rollback was cancelled.",
            };
            operation = operation with
            {
                RollbackStatus = RollbackTransactionStatus.Cancelled,
                ConflictCount = transaction.Files.Count(entry => entry.Status == RollbackFileStatus.Conflict),
                FailureCount = transaction.Files.Count(entry => entry.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup),
            };
            await PersistRollbackStateAsync(operation, transaction, CancellationToken.None);
        }

        await _rollbackVerificationService.VerifyAsync(operationId, CancellationToken.None);
        await _historyService.RefreshOperationAsync(operationId, CancellationToken.None);

        var finalTransaction = await AtomicJsonFileStore.ReadRequiredAsync<RollbackTransaction>(_layout.GetRollbackPath(operationId), CancellationToken.None);
        return new RollbackResult(
            operationId,
            finalTransaction.Status,
            finalTransaction.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            finalTransaction.Files,
            conflicts.Distinct().ToArray(),
            finalTransaction.Files.Count(file => file.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup));
    }

    private async Task<RollbackFileEntry> ProcessFileAsync(
        Guid operationId,
        IReadOnlyDictionary<string, BackupFileEntry> manifestByPath,
        RollbackFileEntry file,
        RollbackExecutionOptions options,
        IList<RollbackConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (file.Protected)
        {
            return file with
            {
                Status = RollbackFileStatus.Failed,
                Message = "Protected files cannot be rolled back automatically.",
            };
        }

        if (!manifestByPath.TryGetValue(file.RelativeBackupPath, out var manifestEntry))
        {
            return file with
            {
                Status = RollbackFileStatus.MissingBackup,
                Message = $"The backup manifest entry for {file.TargetPath} is missing.",
            };
        }

        string backupPath;
        try
        {
            backupPath = _layout.ResolveBackupFilePath(operationId, file.RelativeBackupPath);
        }
        catch (InvalidOperationException ex)
        {
            return file with
            {
                Status = RollbackFileStatus.CorruptBackup,
                Message = ex.Message,
            };
        }

        if (!File.Exists(backupPath))
        {
            return file with
            {
                Status = RollbackFileStatus.MissingBackup,
                Message = $"The backup file for {file.TargetPath} is missing.",
            };
        }

        var (backupLength, backupHash) = await RecoveryFileInspector.GetLengthAndHashAsync(backupPath, cancellationToken);
        if (backupLength != manifestEntry.BackupLength || !backupHash.Equals(manifestEntry.BackupSha256, StringComparison.OrdinalIgnoreCase))
        {
            return file with
            {
                Status = RollbackFileStatus.CorruptBackup,
                Message = $"The backup verification failed for {file.TargetPath}.",
            };
        }

        if (manifestEntry.Classification == BackupFileClassification.Json)
        {
            var jsonStatus = await RecoveryFileInspector.ValidateJsonAsync(backupPath, cancellationToken);
            if (jsonStatus != JsonValidationStatus.Valid)
            {
                return file with
                {
                    Status = RollbackFileStatus.CorruptBackup,
                    Message = $"The backup JSON is invalid for {file.TargetPath}.",
                };
            }
        }

        var targetPath = RecoveryStorageLayout.ValidateAbsoluteTargetPath(file.TargetPath);
        var existedBeforeRestore = File.Exists(targetPath);
        if (existedBeforeRestore)
        {
            var (liveLength, liveHash) = await RecoveryFileInspector.GetLengthAndHashAsync(targetPath, cancellationToken);
            if (liveHash.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return file with
                {
                    Status = RollbackFileStatus.AlreadyRestored,
                    ObservedLiveLength = liveLength,
                    ObservedLiveSha256 = liveHash,
                    Message = "The target already matches the original backup.",
                };
            }

            if (!liveHash.Equals(file.ExpectedAppliedSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!ShouldForceRestore(targetPath, options))
                {
                    conflicts.Add(new RollbackConflict(
                        targetPath,
                        liveHash,
                        file.ExpectedAppliedSha256,
                        file.OriginalSha256,
                        "The live file changed after Apply, so automatic rollback was skipped."));
                    return file with
                    {
                        Status = RollbackFileStatus.Conflict,
                        ObservedLiveLength = liveLength,
                        ObservedLiveSha256 = liveHash,
                        Message = "The live file differs from both the applied hash and the original backup.",
                    };
                }
            }
        }
        else if (!file.ExistedBeforeApply)
        {
            return file with
            {
                Status = RollbackFileStatus.Skipped,
                Message = "The target did not exist before Apply, so deletion rollback was not attempted.",
            };
        }

        try
        {
            await RestoreExactBytesAsync(targetPath, backupPath, manifestEntry, cancellationToken);
            var (restoredLength, restoredHash) = await RecoveryFileInspector.GetLengthAndHashAsync(targetPath, cancellationToken);
            return file with
            {
                Status = RollbackFileStatus.Restored,
                ObservedLiveLength = restoredLength,
                ObservedLiveSha256 = restoredHash,
                Message = existedBeforeRestore
                    ? "The file was restored from the verified backup."
                    : "The missing target was recreated from the verified backup.",
                ForceRestoreUsed = ShouldForceRestore(targetPath, options),
            };
        }
        catch (Exception ex)
        {
            return file with
            {
                Status = RollbackFileStatus.Failed,
                Message = ex.Message,
            };
        }
    }

    private async Task PersistRollbackStateAsync(BackupOperation operation, RollbackTransaction transaction, CancellationToken cancellationToken)
    {
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetRollbackPath(transaction.OperationId),
            transaction,
            value => value.OperationId == transaction.OperationId,
            cancellationToken);
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetOperationPath(operation.OperationId),
            operation,
            value => value.OperationId == operation.OperationId,
            cancellationToken);
    }

    private static bool ShouldForceRestore(string targetPath, RollbackExecutionOptions options)
        => options.ForceRestoreTargets?.Any(candidate => string.Equals(candidate, targetPath, StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task RestoreExactBytesAsync(
        string targetPath,
        string backupPath,
        BackupFileEntry manifestEntry,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("The target path must have a valid parent directory.");
        Directory.CreateDirectory(parent);

        var tempPath = targetPath + ".rollback.tmp";
        await using (var source = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
        }

        var (tempLength, tempHash) = await RecoveryFileInspector.GetLengthAndHashAsync(tempPath, cancellationToken);
        if (tempLength != manifestEntry.OriginalLength || !tempHash.Equals(manifestEntry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Temporary restore verification failed for {targetPath}.");

        if (manifestEntry.Classification == BackupFileClassification.Json)
        {
            var jsonStatus = await RecoveryFileInspector.ValidateJsonAsync(tempPath, cancellationToken);
            if (jsonStatus != JsonValidationStatus.Valid)
                throw new InvalidOperationException($"Temporary JSON restore verification failed for {targetPath}.");
        }

        if (File.Exists(targetPath))
            File.Move(tempPath, targetPath, overwrite: true);
        else
            File.Move(tempPath, targetPath);
    }
}
