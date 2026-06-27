using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class BackupService : IBackupService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IBackupVerificationService _backupVerificationService;
    private readonly IOperationHistoryService _historyService;
    private readonly RecoveryServiceHooks? _hooks;

    public BackupService(IBackupVerificationService backupVerificationService, IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(), backupVerificationService, historyService, null)
    {
    }

    public BackupService(
        string backupsRoot,
        IBackupVerificationService backupVerificationService,
        IOperationHistoryService historyService,
        RecoveryServiceHooks? hooks = null)
        : this(new RecoveryStorageLayout(backupsRoot), backupVerificationService, historyService, hooks)
    {
    }

    internal BackupService(
        RecoveryStorageLayout layout,
        IBackupVerificationService backupVerificationService,
        IOperationHistoryService historyService,
        RecoveryServiceHooks? hooks)
    {
        _layout = layout;
        _backupVerificationService = backupVerificationService;
        _historyService = historyService;
        _hooks = hooks;
    }

    public async Task<OperationPackageDetails> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken)
    {
        var operation = CreateOperationRecord(request, BackupStatus.Pending, OperationVerificationStatus.Pending, RollbackTransactionStatus.Available, 0, 0, false, null);
        _layout.EnsureOperationDirectories(request.OperationId);
        await WriteOperationAsync(operation, cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request);

            operation = operation with { BackupStatus = BackupStatus.Copying };
            await WriteOperationAsync(operation, cancellationToken);

            var entries = new List<BackupFileEntry>();
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 1;
            foreach (var file in request.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Protected)
                    throw new InvalidOperationException($"Protected files cannot be included in a writable backup plan: {file.SourcePath}");

                var sourcePath = RecoveryStorageLayout.ValidateAbsoluteTargetPath(file.SourcePath);
                if (!seenTargets.Add(sourcePath))
                    throw new InvalidOperationException($"Duplicate source target path detected: {sourcePath}");
                if (!File.Exists(sourcePath))
                    throw new InvalidOperationException($"The source file does not exist: {sourcePath}");

                var relativeBackupPath = _layout.BuildRelativeBackupPath(sourcePath, index++);
                if (!seenRelativePaths.Add(relativeBackupPath))
                    throw new InvalidOperationException($"Duplicate backup destination collision detected: {relativeBackupPath}");

                var backupPath = _layout.ResolveBackupFilePath(request.OperationId, relativeBackupPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

                var (originalLength, originalSha256) = await RecoveryFileInspector.GetLengthAndHashAsync(sourcePath, cancellationToken);
                var classification = RecoveryFileInspector.Classify(sourcePath);
                var tempBackupPath = backupPath + ".tmp";

                await CopyExactBytesAsync(sourcePath, tempBackupPath, cancellationToken);
                if (_hooks?.AfterBackupFileCopiedAsync is not null)
                    await _hooks.AfterBackupFileCopiedAsync(file, tempBackupPath, cancellationToken);

                var (backupLength, backupSha256) = await RecoveryFileInspector.GetLengthAndHashAsync(tempBackupPath, cancellationToken);
                if (backupLength != originalLength)
                    throw new InvalidOperationException($"Copied length mismatch for {sourcePath}.");
                if (!backupSha256.Equals(originalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Backup hash mismatch for {sourcePath}.");

                var jsonStatus = classification == BackupFileClassification.Json
                    ? await RecoveryFileInspector.ValidateJsonAsync(tempBackupPath, cancellationToken)
                    : JsonValidationStatus.NotApplicable;
                if (classification == BackupFileClassification.Json && jsonStatus != JsonValidationStatus.Valid)
                    throw new InvalidOperationException($"Expected JSON backup is invalid for {sourcePath}.");

                File.Move(tempBackupPath, backupPath, overwrite: true);

                entries.Add(new BackupFileEntry(
                    sourcePath,
                    relativeBackupPath,
                    originalLength,
                    originalSha256,
                    backupLength,
                    backupSha256,
                    classification,
                    jsonStatus,
                    Protected: false,
                    file.AssociatedStableScanIds ?? Array.Empty<string>(),
                    file.WritablePlanOperationId));
            }

            operation = operation with { BackupStatus = BackupStatus.Verifying, AffectedFileCount = entries.Count };
            await WriteOperationAsync(operation, cancellationToken);

            if (_hooks?.BeforePersistManifestAsync is not null)
                await _hooks.BeforePersistManifestAsync(request.OperationId, cancellationToken);

            var manifest = new BackupManifest(
                request.OperationId,
                operation.CreatedAtUtc,
                operation.ApplicationVersion,
                request.PenumbraVersion,
                request.ScanIdentity,
                entries);
            await AtomicJsonFileStore.WriteAsync(
                _layout.GetManifestPath(request.OperationId),
                manifest,
                value => value.OperationId == request.OperationId && value.Files.Count == entries.Count,
                cancellationToken);

            await _backupVerificationService.VerifyAsync(request.OperationId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            operation = operation with
            {
                BackupStatus = BackupStatus.Failed,
                VerificationStatus = OperationVerificationStatus.Failed,
                FailureCount = 1,
                LastError = "Backup creation was cancelled.",
            };
            await WriteOperationAsync(operation, CancellationToken.None);
        }
        catch (Exception ex)
        {
            operation = operation with
            {
                BackupStatus = BackupStatus.Failed,
                VerificationStatus = OperationVerificationStatus.Failed,
                FailureCount = 1,
                LastError = ex.Message,
            };
            await WriteOperationAsync(operation, CancellationToken.None);
        }

        await _historyService.RefreshOperationAsync(request.OperationId, CancellationToken.None);
        return await _historyService.TryLoadOperationAsync(request.OperationId, CancellationToken.None)
               ?? throw new InvalidOperationException($"Operation {request.OperationId} could not be reloaded after backup creation.");
    }

    private async Task WriteOperationAsync(BackupOperation operation, CancellationToken cancellationToken)
    {
        await AtomicJsonFileStore.WriteAsync(
            _layout.GetOperationPath(operation.OperationId),
            operation,
            value => value.OperationId != Guid.Empty,
            cancellationToken);
    }

    private BackupOperation CreateOperationRecord(
        BackupRequest request,
        BackupStatus backupStatus,
        OperationVerificationStatus verificationStatus,
        RollbackTransactionStatus rollbackStatus,
        int conflictCount,
        int failureCount,
        bool hasRollbackTransaction,
        string? lastError)
        => new(
            request.OperationId,
            DateTimeOffset.UtcNow,
            request.ApplicationVersion ?? typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "dev",
            request.PenumbraVersion,
            request.ScanIdentity,
            backupStatus,
            ApplyStatus.Pending,
            rollbackStatus,
            verificationStatus,
            request.Files.Count,
            request.AffectedModCount,
            conflictCount,
            failureCount,
            _layout.GetOperationDirectory(request.OperationId),
            hasRollbackTransaction,
            RollbackAvailable: false,
            lastError,
            ObservationStatus: null,
            ObservationRecordedAtUtc: null);

    private static void ValidateRequest(BackupRequest request)
    {
        if (request.OperationId == Guid.Empty)
            throw new InvalidOperationException("A globally unique operation ID is required.");
        if (string.IsNullOrWhiteSpace(request.ScanIdentity))
            throw new InvalidOperationException("A scan identity is required.");
        if (request.Files.Count == 0)
            throw new InvalidOperationException("At least one source file is required for backup creation.");
    }

    private static async Task CopyExactBytesAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }
}
