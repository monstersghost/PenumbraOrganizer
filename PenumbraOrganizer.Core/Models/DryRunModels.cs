using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public enum PenumbraWriteTargetKind
{
    ModDataDatabase,
    OrganizationJson,
}

public enum DryRunPlanValidationStatus
{
    Valid,
    Invalid,
    Stale,
}

public enum PlanInvalidationReason
{
    ProposalChanged,
    ProtectionChanged,
    OrganizationStrategyChanged,
    NewScanCompleted,
    SessionChanged,
    PenumbraVersionChanged,
    SourceFileHashChanged,
    SchemaFingerprintChanged,
    ModLibraryIdentityChanged,
    TargetModMissing,
    ApplicationRestarted,
    UnsupportedSchema,
    CurrentFolderChanged,
}

public enum ApplyStatus
{
    Pending,
    Ready,
    InProgress,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled,
}

public enum WritePermissionStatus
{
    Passed,
    Blocked,
}

public sealed record ProposalSnapshot(
    string SnapshotIdentity,
    string OrganizationSessionIdentity,
    OrganizationPreferences OrganizationPreferences,
    IReadOnlyList<OrganizerModProposal> Proposals,
    IReadOnlyList<OrganizerFolder> Folders,
    OrganizerValidationResult ValidationResult);

public sealed record DryRunSourceFileSnapshot(
    string Path,
    long Length,
    string Sha256,
    string SchemaFingerprint);

public sealed record DryRunPlanEntry(
    string StableScanId,
    string PhysicalModIdentity,
    string CurrentVirtualFolder,
    string ProposedVirtualFolder,
    OrganizerProposalSource ProposalSource,
    bool Protected,
    OrganizerRowStatus ValidationStatus,
    string AuthoritativeStateEntryIdentity,
    string TargetPath,
    string RecordKey,
    string SourceHash,
    string ExpectedHash,
    IReadOnlyList<string> Warnings,
    bool RequiresWrite);

public sealed record DryRunFileChange(
    string TargetPath,
    PenumbraWriteTargetKind WriteTargetKind,
    string ExactRecordKey,
    string SourceSha256,
    long SourceLength,
    string ExpectedSha256,
    long ExpectedLength,
    string ExpectedBytesBase64,
    IReadOnlyList<string> AffectedRecordKeys,
    IReadOnlyList<string> ExactAffectedFields,
    bool AtomicReplaceSupported,
    string Description);

public sealed record DryRunSummary(
    int ProtectedRowCount,
    int ChangedRowCount,
    int UnchangedRowCount,
    int InvalidRowCount,
    int AffectedModCount,
    int WriteOperationCount);

public sealed record DryRunValidationResult(
    DryRunPlanValidationStatus Status,
    bool ApplyPermitted,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PlanInvalidationReason> InvalidationReasons);

public sealed record WritePermissionCheckItem(
    string Target,
    string Check,
    WritePermissionStatus Status,
    string Message);

public sealed record WritePermissionPreflightResult(
    bool Succeeded,
    IReadOnlyList<WritePermissionCheckItem> Checks,
    IReadOnlyList<string> BlockingProcesses,
    long RequiredBytes,
    long? AvailableBytes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record DryRunPlan(
    Guid PlanId,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string InstalledPenumbraVersion,
    string ScanIdentity,
    string InstallationIdentity,
    string OrganizationSessionIdentity,
    string ProposalSnapshotIdentity,
    OrganizationPreferences OrganizationPreferences,
    IReadOnlyList<SchemaFingerprint> SourceSchemaFingerprints,
    IReadOnlyList<DryRunSourceFileSnapshot> SourceFiles,
    IReadOnlyList<DryRunPlanEntry> Entries,
    IReadOnlyList<DryRunFileChange> FileChanges,
    DryRunValidationResult Validation,
    DryRunSummary Summary,
    bool ApplyPermitted,
    IReadOnlyList<string> Warnings);

public sealed record ApplyOperation(
    Guid OperationId,
    Guid PlanId,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string InstalledPenumbraVersion,
    string ScanIdentity,
    string InstallationIdentity,
    string OrganizationSessionIdentity,
    string ProposalSnapshotIdentity,
    Guid BackupOperationId,
    WritePermissionPreflightResult Preflight,
    ApplyStatus Status,
    bool RollbackAvailable,
    string? LastError);

public sealed record ApplyFileResult(
    string TargetPath,
    string RecordKey,
    ApplyResultStatus Status,
    string SourceSha256,
    string ExpectedSha256,
    string? FinalSha256,
    string Message,
    bool WriteCompleted);

public sealed record ApplyResult(
    Guid OperationId,
    Guid PlanId,
    ApplyStatus Status,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<ApplyFileResult> Files,
    bool RollbackAvailable,
    string? LastError,
    bool AutomaticRollbackAttempted = false,
    bool AutomaticRollbackSucceeded = false,
    string? AutomaticRollbackMessage = null);

public sealed record PostApplyVerificationResult(
    Guid OperationId,
    bool Succeeded,
    int VerifiedChangedModCount,
    int VerifiedProtectedModCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
