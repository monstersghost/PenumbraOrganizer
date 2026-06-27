using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public enum BackupStatus
{
    Pending,
    Copying,
    Verifying,
    Verified,
    Failed,
}

public enum BackupFileClassification
{
    Json,
    Text,
    Binary,
}

public enum JsonValidationStatus
{
    NotApplicable,
    Valid,
    Invalid,
}

public enum OperationVerificationStatus
{
    Pending,
    Verified,
    Failed,
}

public enum RollbackTransactionStatus
{
    Available,
    InProgress,
    Completed,
    CompletedWithConflicts,
    PartiallyCompleted,
    Failed,
    Cancelled,
}

public enum RollbackFileStatus
{
    Pending,
    Restored,
    Skipped,
    Conflict,
    MissingBackup,
    CorruptBackup,
    Failed,
    AlreadyRestored,
}

public enum ApplyResultStatus
{
    Pending,
    Applied,
    Skipped,
    Failed,
}

public sealed record BackupFileRequest(
    string SourcePath,
    bool Protected,
    IReadOnlyList<string>? AssociatedStableScanIds = null,
    string? WritablePlanOperationId = null);

public sealed record BackupRequest(
    Guid OperationId,
    string ScanIdentity,
    IReadOnlyList<BackupFileRequest> Files,
    string? ApplicationVersion = null,
    string? PenumbraVersion = null,
    int? AffectedModCount = null);

public sealed record BackupOperation(
    Guid OperationId,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string? PenumbraVersion,
    string ScanIdentity,
    BackupStatus BackupStatus,
    ApplyStatus ApplyStatus,
    RollbackTransactionStatus RollbackStatus,
    OperationVerificationStatus VerificationStatus,
    int AffectedFileCount,
    int? AffectedModCount,
    int ConflictCount,
    int FailureCount,
    string OperationFolder,
    bool HasRollbackTransaction,
    bool RollbackAvailable,
    string? LastError);

public sealed record BackupManifest(
    Guid OperationId,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string? PenumbraVersion,
    string ScanIdentity,
    IReadOnlyList<BackupFileEntry> Files);

public sealed record BackupFileEntry(
    string SourceTargetPath,
    string RelativeBackupPath,
    long OriginalLength,
    string OriginalSha256,
    long BackupLength,
    string BackupSha256,
    BackupFileClassification Classification,
    JsonValidationStatus JsonValidationStatus,
    bool Protected,
    IReadOnlyList<string> AssociatedStableScanIds,
    string? WritablePlanOperationId);

public sealed record BackupVerificationResult(
    Guid OperationId,
    DateTimeOffset VerifiedAtUtc,
    bool Succeeded,
    int VerifiedFileCount,
    int FailureCount,
    IReadOnlyList<string> Issues);

public sealed record RollbackTransaction(
    Guid OperationId,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string? PenumbraVersion,
    string ScanIdentity,
    RollbackTransactionStatus Status,
    IReadOnlyList<RollbackFileEntry> Files,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError);

public sealed record RollbackFileEntry(
    string TargetPath,
    string RelativeBackupPath,
    string OriginalSha256,
    long OriginalLength,
    string ExpectedAppliedSha256,
    bool ExistedBeforeApply,
    BackupFileClassification Classification,
    bool Protected,
    ApplyResultStatus ApplyResultStatus,
    RollbackFileStatus Status,
    IReadOnlyList<string> AssociatedStableScanIds,
    string? PlannedOperationId,
    string? ObservedLiveSha256,
    long? ObservedLiveLength,
    string? Message,
    bool ForceRestoreUsed = false);

public sealed record RollbackConflict(
    string TargetPath,
    string? CurrentLiveSha256,
    string ExpectedAppliedSha256,
    string OriginalBackupSha256,
    string Reason);

public sealed record RollbackResult(
    Guid OperationId,
    RollbackTransactionStatus Status,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<RollbackFileEntry> Files,
    IReadOnlyList<RollbackConflict> Conflicts,
    int FailureCount);

public sealed record RollbackVerificationResult(
    Guid OperationId,
    DateTimeOffset VerifiedAtUtc,
    bool Succeeded,
    RollbackTransactionStatus TransactionStatus,
    int VerifiedFileCount,
    int ConflictCount,
    int FailureCount,
    IReadOnlyList<string> Issues);

public sealed record OperationHistoryEntry(
    Guid OperationId,
    DateTimeOffset CreatedAtUtc,
    BackupStatus BackupStatus,
    ApplyStatus ApplyStatus,
    RollbackTransactionStatus RollbackStatus,
    int AffectedFileCount,
    int? AffectedModCount,
    string? PenumbraVersion,
    OperationVerificationStatus VerificationStatus,
    int ConflictCount,
    int FailureCount,
    string OperationFolder,
    bool HasRollbackTransaction,
    bool RollbackAvailable);

public sealed record OperationPackageDetails(
    BackupOperation Operation,
    BackupManifest? Manifest,
    DryRunPlan? Plan,
    ApplyOperation? ApplyOperation,
    ApplyResult? ApplyResult,
    RollbackTransaction? RollbackTransaction,
    BackupVerificationResult? BackupVerification,
    RollbackVerificationResult? RollbackVerification,
    PostApplyVerificationResult? PostApplyVerification);

public sealed record RollbackExecutionOptions(
    IReadOnlySet<string>? ForceRestoreTargets = null)
{
    public static RollbackExecutionOptions Default { get; } = new();
}
