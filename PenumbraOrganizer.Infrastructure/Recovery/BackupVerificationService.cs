using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class BackupVerificationService : IBackupVerificationService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IOperationHistoryService _historyService;

    public BackupVerificationService(IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(), historyService)
    {
    }

    public BackupVerificationService(string backupsRoot, IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(backupsRoot), historyService)
    {
    }

    internal BackupVerificationService(RecoveryStorageLayout layout, IOperationHistoryService historyService)
    {
        _layout = layout;
        _historyService = historyService;
    }

    public async Task<BackupVerificationResult> VerifyAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var verifiedCount = 0;
        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(_layout.GetOperationPath(operationId), cancellationToken);
        var manifest = await AtomicJsonFileStore.ReadAsync<BackupManifest>(_layout.GetManifestPath(operationId), cancellationToken);

        if (manifest is null)
        {
            issues.Add("The backup manifest is missing or invalid.");
        }
        else
        {
            if (manifest.OperationId != operationId)
                issues.Add("The backup manifest operation ID does not match the operation package.");
            if (manifest.Files.Count == 0)
                issues.Add("The backup manifest does not contain any files.");

            var seenBackupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenBackupPaths.Add(file.RelativeBackupPath))
                {
                    issues.Add($"Duplicate backup path detected: {file.RelativeBackupPath}");
                    continue;
                }

                if (file.Protected)
                {
                    issues.Add($"Protected files cannot appear in a writable backup plan: {file.SourceTargetPath}");
                    continue;
                }

                string backupPath;
                try
                {
                    backupPath = _layout.ResolveBackupFilePath(operationId, file.RelativeBackupPath);
                }
                catch (InvalidOperationException ex)
                {
                    issues.Add(ex.Message);
                    continue;
                }

                if (!File.Exists(backupPath))
                {
                    issues.Add($"Backup file is missing: {file.RelativeBackupPath}");
                    continue;
                }

                var (length, sha256) = await RecoveryFileInspector.GetLengthAndHashAsync(backupPath, cancellationToken);
                if (length != file.BackupLength)
                {
                    issues.Add($"Backup length mismatch for {file.RelativeBackupPath}.");
                    continue;
                }

                if (!sha256.Equals(file.BackupSha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Backup hash mismatch for {file.RelativeBackupPath}.");
                    continue;
                }

                if (file.Classification == BackupFileClassification.Json)
                {
                    var jsonStatus = await RecoveryFileInspector.ValidateJsonAsync(backupPath, cancellationToken);
                    if (jsonStatus != JsonValidationStatus.Valid || file.JsonValidationStatus != JsonValidationStatus.Valid)
                    {
                        issues.Add($"Backup JSON is invalid for {file.RelativeBackupPath}.");
                        continue;
                    }
                }

                verifiedCount++;
            }
        }

        var result = new BackupVerificationResult(
            operationId,
            DateTimeOffset.UtcNow,
            issues.Count == 0,
            verifiedCount,
            issues.Count,
            issues);

        var verificationDocument = await AtomicJsonFileStore.ReadAsync<OperationVerificationDocument>(_layout.GetVerificationPath(operationId), cancellationToken)
            ?? new OperationVerificationDocument();
        verificationDocument = verificationDocument with { BackupVerification = result };
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetVerificationPath(operationId),
            verificationDocument,
            _ => true,
            cancellationToken);

        var updatedOperation = operation with
        {
            BackupStatus = result.Succeeded ? BackupStatus.Verified : BackupStatus.Failed,
            VerificationStatus = result.Succeeded ? OperationVerificationStatus.Verified : OperationVerificationStatus.Failed,
            FailureCount = result.FailureCount,
            LastError = result.Succeeded ? null : string.Join(Environment.NewLine, result.Issues),
        };
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetOperationPath(operationId),
            updatedOperation,
            value => value.OperationId != Guid.Empty,
            cancellationToken);
        await _historyService.RefreshOperationAsync(operationId, cancellationToken);

        return result;
    }
}
