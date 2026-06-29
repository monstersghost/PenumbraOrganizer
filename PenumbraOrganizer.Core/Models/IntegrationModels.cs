using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public sealed record RealInstallationValidationOptions(
    bool Authorized,
    bool CreateVerifiedBackup);

public sealed record ControlledTestOptions(
    string TestFolderName,
    int MaximumSelectedModCount = 3);

public sealed record ControlledTestRequest(
    string TestFolderName,
    IReadOnlyList<string> StableScanIds,
    int MaximumSelectedModCount = 3);

public enum ControlledTestCandidateStatus
{
    Eligible,
    Protected,
    Ambiguous,
    Unsupported,
}

public sealed record ControlledTestCandidate(
    string StableScanId,
    string ModName,
    string CurrentVirtualFolder,
    string CurrentProposedFolder,
    string ProposedTestFolder,
    string PhysicalDirectory,
    string TargetPath,
    string RecordKey,
    ControlledTestCandidateStatus Status,
    string StatusMessage,
    bool CanSelect);

public sealed record ControlledTestSetup(
    ControlledTestOptions Options,
    IReadOnlyList<ControlledTestCandidate> Candidates,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ValidationMappedRecord(
    string StableScanId,
    string ModName,
    string CurrentVirtualFolder,
    string ProposedVirtualFolder,
    string PhysicalDirectory,
    string TargetPath,
    string RecordKey,
    bool Protected,
    OrganizerRowStatus ValidationStatus,
    bool RequiresWrite,
    IReadOnlyList<string> Warnings);

public sealed record RealInstallationValidationReport(
    string PenumbraStateDirectory,
    string ModLibraryRoot,
    string InstalledPenumbraVersion,
    int ModsScanned,
    int ProposedChanges,
    int MappedRecords,
    int MissingRecords,
    int AmbiguousRecords,
    int ProtectedMods,
    int UnsupportedRecords,
    int UnsupportedStructures,
    string WritableTargetStatus,
    string GameOrLauncherStatus,
    string BackupReadiness,
    string RollbackReadiness,
    bool ApplyCurrentlySafe);

public sealed record RealInstallationValidationResult(
    PenumbraInstallation Installation,
    ScanInventory Inventory,
    ProposalSnapshot ProposalSnapshot,
    DryRunPlan Plan,
    WritePermissionPreflightResult Preflight,
    IReadOnlyList<ValidationMappedRecord> Records,
    RealInstallationValidationReport Report,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    bool BackupCreated,
    Guid? BackupOperationId,
    string Summary,
    bool AppearsSafeForApply);

public sealed record DiagnosticExportRequest(
    string ApplicationVersion,
    PenumbraInstallation? Installation,
    ScanInventory? Inventory,
    OrganizerValidationResult? ReviewValidation,
    DryRunPlan? DryRunPlan,
    ApplyOperation? ApplyOperation,
    ApplyResult? ApplyResult,
    RealInstallationValidationResult? RealInstallationValidation,
    IReadOnlyList<OperationHistoryEntry> Operations,
    string ActivityLog);

public sealed record DiagnosticExportResult(
    string ExportFolder,
    string ZipPath,
    IReadOnlyList<string> IncludedItems);

public enum PenumbraUiObservationStatus
{
    NotCheckedYet,
    AppearedImmediately,
    AppearedAfterReloadOrRestart,
    DidNotAppear,
}

public sealed class DiagnosticSummaryDocument
{
    [JsonPropertyName("applicationVersion")]
    public required string ApplicationVersion { get; init; }

    [JsonPropertyName("windowsVersion")]
    public required string WindowsVersion { get; init; }

    [JsonPropertyName("penumbraVersion")]
    public string? PenumbraVersion { get; init; }

    [JsonPropertyName("compatibilityStatus")]
    public string? CompatibilityStatus { get; init; }

    [JsonPropertyName("installation")]
    public object? Installation { get; init; }

    [JsonPropertyName("validation")]
    public object? Validation { get; init; }

    [JsonPropertyName("operations")]
    public required IReadOnlyList<object> Operations { get; init; }
}
