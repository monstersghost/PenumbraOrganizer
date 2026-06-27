using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class RollbackVerificationService : IRollbackVerificationService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IOperationHistoryService _historyService;

    public RollbackVerificationService(IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(), historyService)
    {
    }

    public RollbackVerificationService(string backupsRoot, IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(backupsRoot), historyService)
    {
    }

    internal RollbackVerificationService(RecoveryStorageLayout layout, IOperationHistoryService historyService)
    {
        _layout = layout;
        _historyService = historyService;
    }

    public async Task<RollbackVerificationResult> VerifyAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var verifiedCount = 0;
        var manifest = await AtomicJsonFileStore.ReadAsync<BackupManifest>(_layout.GetManifestPath(operationId), cancellationToken);
        var rollback = await AtomicJsonFileStore.ReadAsync<RollbackTransaction>(_layout.GetRollbackPath(operationId), cancellationToken);

        if (manifest is null)
            issues.Add("The backup manifest is required for rollback verification.");
        if (rollback is null)
            issues.Add("The rollback transaction is missing or invalid.");

        if (manifest is null || rollback is null)
        {
            return await PersistResultAsync(
                operationId,
                new RollbackVerificationResult(
                    operationId,
                    DateTimeOffset.UtcNow,
                    false,
                    rollback?.Status ?? RollbackTransactionStatus.Failed,
                    verifiedCount,
                    rollback?.Files.Count(file => file.Status == RollbackFileStatus.Conflict) ?? 0,
                    issues.Count,
                    issues),
                cancellationToken);
        }

        var manifestByBackupPath = manifest.Files.ToDictionary(file => file.RelativeBackupPath, StringComparer.OrdinalIgnoreCase);
        foreach (var file in rollback.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!manifestByBackupPath.TryGetValue(file.RelativeBackupPath, out var manifestEntry))
            {
                issues.Add($"Rollback entry is missing a manifest file: {file.RelativeBackupPath}");
                continue;
            }

            switch (file.Status)
            {
                case RollbackFileStatus.Restored:
                case RollbackFileStatus.AlreadyRestored:
                    if (!File.Exists(file.TargetPath))
                    {
                        issues.Add($"Restored target is missing: {file.TargetPath}");
                        continue;
                    }

                    var (restoredLength, restoredHash) = await RecoveryFileInspector.GetLengthAndHashAsync(file.TargetPath, cancellationToken);
                    if (restoredLength != manifestEntry.OriginalLength)
                    {
                        issues.Add($"Restored length mismatch for {file.TargetPath}.");
                        continue;
                    }

                    if (!restoredHash.Equals(manifestEntry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add($"Restored hash mismatch for {file.TargetPath}.");
                        continue;
                    }

                    if (manifestEntry.Classification == BackupFileClassification.Json)
                    {
                        var jsonStatus = await RecoveryFileInspector.ValidateJsonAsync(file.TargetPath, cancellationToken);
                        if (jsonStatus != JsonValidationStatus.Valid)
                        {
                            issues.Add($"Restored JSON is invalid for {file.TargetPath}.");
                            continue;
                        }
                    }

                    verifiedCount++;
                    break;

                case RollbackFileStatus.Conflict:
                case RollbackFileStatus.Skipped:
                    if (file.ObservedLiveSha256 is null)
                        continue;
                    if (!File.Exists(file.TargetPath))
                    {
                        issues.Add($"Skipped target changed unexpectedly: {file.TargetPath}");
                        continue;
                    }

                    var (_, currentHash) = await RecoveryFileInspector.GetLengthAndHashAsync(file.TargetPath, cancellationToken);
                    if (!currentHash.Equals(file.ObservedLiveSha256, StringComparison.OrdinalIgnoreCase))
                        issues.Add($"Skipped target changed after rollback evaluation: {file.TargetPath}");
                    else
                        verifiedCount++;
                    break;
            }
        }

        var expectedStatus = RollbackStatusCalculator.Calculate(rollback.Files);
        if (rollback.Status != expectedStatus && rollback.Status != RollbackTransactionStatus.Cancelled)
            issues.Add($"Rollback transaction status mismatch. Expected {expectedStatus}, found {rollback.Status}.");

        var result = new RollbackVerificationResult(
            operationId,
            DateTimeOffset.UtcNow,
            issues.Count == 0,
            rollback.Status,
            verifiedCount,
            rollback.Files.Count(file => file.Status == RollbackFileStatus.Conflict),
            rollback.Files.Count(file => file.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup) + issues.Count(issue => issue.Contains("status mismatch", StringComparison.OrdinalIgnoreCase)),
            issues);

        return await PersistResultAsync(operationId, result, cancellationToken);
    }

    private async Task<RollbackVerificationResult> PersistResultAsync(Guid operationId, RollbackVerificationResult result, CancellationToken cancellationToken)
    {
        var verificationDocument = await AtomicJsonFileStore.ReadAsync<OperationVerificationDocument>(_layout.GetVerificationPath(operationId), cancellationToken)
            ?? new OperationVerificationDocument();
        verificationDocument = verificationDocument with { RollbackVerification = result };
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetVerificationPath(operationId),
            verificationDocument,
            _ => true,
            cancellationToken);
        await _historyService.RefreshOperationAsync(operationId, cancellationToken);
        return result;
    }
}
