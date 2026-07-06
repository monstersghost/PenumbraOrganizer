using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Apply;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class OperationRecoveryService : IOperationRecoveryService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IOperationHistoryService _historyService;
    private readonly IBackupVerificationService _backupVerificationService;
    private readonly IPostApplyVerificationService _postApplyVerificationService;

    public OperationRecoveryService(
        IOperationHistoryService historyService,
        IBackupVerificationService backupVerificationService,
        IPostApplyVerificationService postApplyVerificationService)
        : this(new RecoveryStorageLayout(), historyService, backupVerificationService, postApplyVerificationService)
    {
    }

    public OperationRecoveryService(
        string backupsRoot,
        IOperationHistoryService historyService,
        IBackupVerificationService backupVerificationService,
        IPostApplyVerificationService postApplyVerificationService)
        : this(new RecoveryStorageLayout(backupsRoot), historyService, backupVerificationService, postApplyVerificationService)
    {
    }

    internal OperationRecoveryService(
        RecoveryStorageLayout layout,
        IOperationHistoryService historyService,
        IBackupVerificationService backupVerificationService,
        IPostApplyVerificationService postApplyVerificationService)
    {
        _layout = layout;
        _historyService = historyService;
        _backupVerificationService = backupVerificationService;
        _postApplyVerificationService = postApplyVerificationService;
    }

    public async Task<IReadOnlyList<IncompleteOperationRecord>> GetIncompleteOperationsAsync(CancellationToken cancellationToken)
    {
        var operations = await _historyService.GetOperationsAsync(cancellationToken);
        var incomplete = new List<IncompleteOperationRecord>();

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await _historyService.TryLoadOperationAsync(operation.OperationId, cancellationToken);
            if (details is null)
                continue;

            if (IsBackupIncomplete(details.Operation))
            {
                incomplete.Add(new IncompleteOperationRecord(
                    details.Operation.OperationId,
                    details.Operation.CreatedAtUtc,
                    IncompleteOperationStage.BackupPreparation,
                    [RecoveryRecommendedAction.Reverify, RecoveryRecommendedAction.ViewDetails],
                    "Backup preparation did not finish cleanly. Re-verify the operation package before continuing.",
                    details.Operation.RollbackAvailable));
                continue;
            }

            if (IsApplyIncomplete(details.Operation))
            {
                var actions = new List<RecoveryRecommendedAction> { RecoveryRecommendedAction.ContinueVerification };
                if (details.Operation.RollbackAvailable)
                    actions.Add(RecoveryRecommendedAction.RollBack);
                actions.Add(RecoveryRecommendedAction.ViewDetails);

                incomplete.Add(new IncompleteOperationRecord(
                    details.Operation.OperationId,
                    details.Operation.CreatedAtUtc,
                    IncompleteOperationStage.Apply,
                    actions,
                    "Apply was interrupted or cancelled. Re-verify the authoritative result before treating it as complete.",
                    details.Operation.RollbackAvailable));
                continue;
            }

            if (IsPostApplyVerificationIncomplete(details))
            {
                var actions = new List<RecoveryRecommendedAction> { RecoveryRecommendedAction.ContinueVerification };
                if (details.Operation.RollbackAvailable)
                    actions.Add(RecoveryRecommendedAction.RollBack);
                actions.Add(RecoveryRecommendedAction.ViewDetails);

                incomplete.Add(new IncompleteOperationRecord(
                    details.Operation.OperationId,
                    details.Operation.CreatedAtUtc,
                    IncompleteOperationStage.PostApplyVerification,
                    actions,
                    "Post-Apply verification is incomplete. Continue verification before trusting the live result.",
                    details.Operation.RollbackAvailable));
                continue;
            }

            if (IsRollbackIncomplete(details.Operation))
            {
                incomplete.Add(new IncompleteOperationRecord(
                    details.Operation.OperationId,
                    details.Operation.CreatedAtUtc,
                    IncompleteOperationStage.Rollback,
                    [RecoveryRecommendedAction.RollBack, RecoveryRecommendedAction.Reverify, RecoveryRecommendedAction.ViewDetails],
                    "Rollback did not finish cleanly. You can retry rollback safely; conflicts will still be skipped by default.",
                    details.Operation.RollbackAvailable));
            }
        }

        return incomplete
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.OperationId)
            .ToArray();
    }

    public Task<BackupVerificationResult> ReverifyBackupAsync(Guid operationId, CancellationToken cancellationToken)
        => _backupVerificationService.VerifyAsync(operationId, cancellationToken);

    public async Task<PostApplyVerificationResult> ContinueVerificationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var details = await _historyService.TryLoadOperationAsync(operationId, cancellationToken)
                      ?? throw new InvalidOperationException($"Operation {operationId} was not found.");
        if (details.Plan is null || details.ApplyResult is null)
            throw new InvalidOperationException("A persisted dry run and apply result are required before verification can continue.");

        // This recovery flow only has a persisted operation ID, not a live PenumbraInstallation, so
        // a mod_data.db-backed operation can only be re-verified via its hash chain here (see
        // PostApplyVerificationService.ResolveCurrentFolderLookup).
        var verification = await _postApplyVerificationService.VerifyAsync(details.Plan, details.ApplyResult, installation: null, cancellationToken);
        var verificationDocument = await AtomicJsonFileStore.ReadAsync<OperationVerificationDocument>(_layout.GetVerificationPath(operationId), cancellationToken)
            ?? new OperationVerificationDocument();
        verificationDocument = verificationDocument with { PostApplyVerification = verification };
        await AtomicJsonFileStore.WriteAsync(_layout.GetVerificationPath(operationId), verificationDocument, _ => true, cancellationToken);

        var finalStatus = verification.Succeeded
            ? details.ApplyResult.Status
            : details.ApplyResult.Files.Any(file => file.WriteCompleted)
                ? ApplyStatus.PartiallyCompleted
                : ApplyStatus.Failed;
        var lastError = verification.Succeeded ? details.ApplyResult.LastError : string.Join(Environment.NewLine, verification.Errors);

        var operation = details.Operation with
        {
            ApplyStatus = finalStatus,
            FailureCount = verification.Succeeded ? details.Operation.FailureCount : Math.Max(details.Operation.FailureCount, 1),
            LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError,
        };
        await AtomicJsonFileStore.WriteAsync(_layout.GetOperationPath(operationId), operation, value => value.OperationId == operationId, cancellationToken);

        var applyDocument = await AtomicJsonFileStore.ReadAsync<ApplyPackageDocument>(_layout.GetApplyPath(operationId), cancellationToken);
        if (applyDocument?.Operation is not null)
        {
            await AtomicJsonFileStore.WriteAsync(
                _layout.GetApplyPath(operationId),
                applyDocument with
                {
                    Operation = applyDocument.Operation with
                    {
                        Status = finalStatus,
                        LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError,
                    }
                },
                value => value.Operation?.OperationId == operationId || value.Result?.OperationId == operationId,
                cancellationToken);
        }

        await _historyService.RefreshOperationAsync(operationId, cancellationToken);
        return verification;
    }

    private static bool IsBackupIncomplete(BackupOperation operation)
        => operation.BackupStatus is BackupStatus.Pending or BackupStatus.Copying or BackupStatus.Verifying
           || (operation.BackupStatus == BackupStatus.Failed && operation.ApplyStatus == ApplyStatus.Pending && !operation.HasRollbackTransaction);

    private static bool IsApplyIncomplete(BackupOperation operation)
        => operation.ApplyStatus is ApplyStatus.InProgress or ApplyStatus.Cancelled;

    private static bool IsPostApplyVerificationIncomplete(OperationPackageDetails details)
        => details.ApplyResult is not null
           && details.Operation.ApplyStatus is ApplyStatus.Completed or ApplyStatus.PartiallyCompleted or ApplyStatus.Failed
           && details.PostApplyVerification is null;

    private static bool IsRollbackIncomplete(BackupOperation operation)
        => operation.RollbackStatus is RollbackTransactionStatus.InProgress or RollbackTransactionStatus.Cancelled or RollbackTransactionStatus.PartiallyCompleted;
}
