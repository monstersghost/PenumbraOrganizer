using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public sealed record RealInstallationValidationOptions(
    bool Authorized,
    bool CreateVerifiedBackup);

public sealed record ValidationMappedRecord(
    string StableScanId,
    string ModName,
    string CurrentVirtualFolder,
    string ProposedVirtualFolder,
    string TargetPath,
    string RecordKey,
    bool Protected,
    OrganizerRowStatus ValidationStatus,
    bool RequiresWrite,
    IReadOnlyList<string> Warnings);

public sealed record RealInstallationValidationResult(
    PenumbraInstallation Installation,
    ScanInventory Inventory,
    ProposalSnapshot ProposalSnapshot,
    DryRunPlan Plan,
    WritePermissionPreflightResult Preflight,
    IReadOnlyList<ValidationMappedRecord> Records,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    bool BackupCreated,
    Guid? BackupOperationId,
    string Summary,
    bool AppearsSafeForApply);

public enum AiImportDecisionKind
{
    ImportedSuggestion,
    ManualOverride,
    RejectedSuggestion,
    NeedsReview,
    Unchanged,
}

public sealed record AiProposalImportDecision(
    string ScanId,
    string ModName,
    AiImportDecisionKind Decision,
    string Message);

public sealed record ImportedProposalRow(
    string StableScanId,
    string ProposedVirtualFolder,
    string OrganizerCreatorLabel,
    string OrganizerTypeLabel,
    OrganizerProposalSource Source,
    bool Protected,
    bool NeedsReview);

public sealed record AiProposalImportResult(
    string ProposalPath,
    string SourceExportId,
    int ImportedCount,
    int ManualOverrideCount,
    int RejectedCount,
    int NeedsReviewCount,
    IReadOnlyList<ImportedProposalRow> ImportedRows,
    IReadOnlyList<AiProposalImportDecision> Decisions,
    IReadOnlyList<AiProposalValidationIssue> Errors,
    IReadOnlyList<AiProposalValidationIssue> Warnings,
    string Summary);

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
