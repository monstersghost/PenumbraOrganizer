using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public enum PenumbraWriteTargetKind
{
    SortOrderJson,
    ModDataDb,
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

/// <param name="Folders">
/// The authoritative proposed folder set. <b>Footgun:</b> the writer treats the
/// <see cref="OrganizerFolder.ManuallyCreated"/> folders here as the complete empty-folder list for
/// <c>sort_order.json</c>. Any builder MUST include the scan's existing empty folders
/// (<see cref="ScanInventory.EmptyFolders"/>) in this list, or the writer will delete them. Both
/// production builders seed them (MainViewModel via SeedExistingEmptyFolders; the controlled-test
/// builder inherits the seeded base snapshot). New builders must do the same.
/// </param>
public sealed record ProposalSnapshot(
    string SnapshotIdentity,
    string OrganizationSessionIdentity,
    OrganizationPreferences OrganizationPreferences,
    IReadOnlyList<OrganizerModProposal> Proposals,
    IReadOnlyList<OrganizerFolder> Folders,
    OrganizerValidationResult ValidationResult,
    // Folder paths from organization.json the user confirmed for pruning (Folder Cleanup tab,
    // Plan 3). Re-verified against live proposals at write-build time, never trusted as-is --
    // see OrganizationCleanupWriter.BuildFileChangeAsync. Null/empty by default: every existing
    // builder of ProposalSnapshot keeps working unchanged and produces zero organization.json
    // writes, exactly like today.
    IReadOnlyList<string>? OrganizationCleanupSelections = null,
    // User explicitly opted in (Advanced Cleanup dialog) to bypass the per-Apply folder cleanup
    // cap in DryRunValidationService. False by default -- every existing builder keeps the cap
    // enforced exactly like today. Set only by MainViewModel after a strict confirmation dialog;
    // never persisted, so it resets on app restart.
    bool OrganizationCleanupBypassSafetyCap = false);

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
    bool RequiresWrite,
    // Full sort_order.json path (folder + preserved display leaf) the mod should map to after
    // Apply. Empty string means the entry should be removed (mod returns to root with its
    // default display name). Only meaningful when RequiresWrite is true.
    string ProposedSortPath = "");

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
    IReadOnlyList<string> Warnings,
    // Snapshot of organization.json's own source file at plan-build time, kept separate from
    // SourceFiles/SourceSchemaFingerprints (those feed DryRunPlanner's blocking schema-mismatch
    // gate for the *primary* writer -- organization.json must never trip that gate). Null when
    // organization.json cleanup wasn't active for this plan (no writer configured, or the file
    // didn't exist). PlanInvalidationService compares this against a fresh capture for staleness.
    DryRunSourceFileSnapshot? OrganizationCleanupSourceFile = null);

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
    string? LastError);

public sealed record PostApplyVerificationResult(
    Guid OperationId,
    bool Succeeded,
    int VerifiedChangedModCount,
    int VerifiedProtectedModCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
