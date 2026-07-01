using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public enum OrganizerProposalSource
{
    Manual,
    DeterministicRule,
    ImportedAi,
    PreservedCurrent,
    RestoredByUndo,
}

public enum OrganizerOperationType
{
    AssignToFolder,
    ReturnToCurrent,
    Protect,
    Unprotect,
    CreateFolder,
    RenameFolder,
    DeleteFolder,
    ApplyDeterministicSuggestions,
    ImportAiSuggestions,
}

public enum OrganizerRowStatus
{
    ValidChange,
    Unchanged,
    Protected,
    NeedsReview,
    InvalidPath,
    BlockedProtected,
    MissingMod,
    StaleScan,
}

public sealed class OrganizerModProposal
{
    public required string StableScanId { get; init; }
    public required string Name { get; init; }
    public required string CurrentVirtualFolder { get; init; }
    public required string ProposedVirtualFolder { get; set; }
    public required string OriginalCreator { get; init; }
    public string OrganizerCreatorLabel { get; set; } = string.Empty;
    public string OrganizerTypeLabel { get; set; } = string.Empty;
    public bool Protected { get; set; }
    public bool OriginalProtected { get; init; }
    public OrganizerProposalSource Source { get; set; } = OrganizerProposalSource.PreservedCurrent;
    public bool NeedsReview { get; set; }
}

public sealed record OrganizerFolder(string Path, bool ManuallyCreated = false, bool Protected = false);

public sealed record OrganizerOperationChange(
    string StableScanId,
    string BeforeProposedVirtualFolder,
    string AfterProposedVirtualFolder,
    bool BeforeProtected,
    bool AfterProtected,
    OrganizerProposalSource BeforeSource,
    OrganizerProposalSource AfterSource,
    bool BeforeNeedsReview,
    bool AfterNeedsReview);

public sealed record OrganizerFolderChange(
    string BeforePath,
    string AfterPath,
    bool BeforeExists,
    bool AfterExists,
    bool BeforeManuallyCreated,
    bool AfterManuallyCreated);

public sealed record OrganizerHistoryEntry(
    Guid Id,
    OrganizerOperationType OperationType,
    string Description,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<string> AffectedStableScanIds,
    IReadOnlyList<OrganizerOperationChange> RowChanges,
    IReadOnlyList<OrganizerFolderChange> FolderChanges);

public sealed class OrganizerMutationResult
{
    public required bool Succeeded { get; init; }
    public required string Message { get; init; }
    public OrganizerHistoryEntry? HistoryEntry { get; init; }

    public static OrganizerMutationResult Blocked(string message)
        => new() { Succeeded = false, Message = message };

    public static OrganizerMutationResult Success(string message, OrganizerHistoryEntry historyEntry)
        => new() { Succeeded = true, Message = message, HistoryEntry = historyEntry };
}

public sealed record OrganizerValidationIssue(string StableScanId, string Code, string Message);

public sealed record OrganizerValidationRow(
    string StableScanId,
    string ModName,
    string CurrentVirtualFolder,
    string ProposedVirtualFolder,
    OrganizerProposalSource Source,
    OrganizerRowStatus Status,
    string Message);

public sealed record OrganizerValidationSummary(
    int TotalMods,
    int Changed,
    int Unchanged,
    int Protected,
    int NeedsReview,
    int Invalid,
    int Warnings);

public sealed class OrganizerValidationResult
{
    public required IReadOnlyList<OrganizerValidationIssue> Errors { get; init; }
    public required IReadOnlyList<OrganizerValidationIssue> Warnings { get; init; }
    public required IReadOnlyList<OrganizerValidationRow> Rows { get; init; }
    public required OrganizerValidationSummary Summary { get; init; }
    public IReadOnlyList<OrganizerValidationRow> ValidChanges => Rows.Where(row => row.Status == OrganizerRowStatus.ValidChange).ToArray();
    public IReadOnlyList<OrganizerValidationRow> UnchangedRows => Rows.Where(row => row.Status == OrganizerRowStatus.Unchanged).ToArray();
    public IReadOnlyList<OrganizerValidationRow> BlockedRows => Rows.Where(row => row.Status is OrganizerRowStatus.BlockedProtected or OrganizerRowStatus.InvalidPath or OrganizerRowStatus.MissingMod or OrganizerRowStatus.StaleScan).ToArray();
}

public sealed class OrganizerSessionDocument
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("scanIdentity")]
    public required string ScanIdentity { get; init; }

    [JsonPropertyName("scanTimestampUtc")]
    public required DateTimeOffset ScanTimestampUtc { get; init; }

    [JsonPropertyName("installationIdentity")]
    public required string InstallationIdentity { get; init; }

    [JsonPropertyName("installedPenumbraVersion")]
    public string? InstalledPenumbraVersion { get; init; }

    [JsonPropertyName("organizationPreferences")]
    public required OrganizationPreferences OrganizationPreferences { get; init; }

    [JsonPropertyName("proposedFolders")]
    public required IReadOnlyList<OrganizerSessionFolder> ProposedFolders { get; init; }

    [JsonPropertyName("mods")]
    public required IReadOnlyList<OrganizerSessionMod> Mods { get; init; }
}

public sealed record OrganizerSessionFolder(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("manuallyCreated")] bool ManuallyCreated,
    [property: JsonPropertyName("protected")] bool Protected);

public sealed record OrganizerSessionMod(
    [property: JsonPropertyName("stableScanId")] string StableScanId,
    [property: JsonPropertyName("currentVirtualFolder")] string CurrentVirtualFolder,
    [property: JsonPropertyName("proposedVirtualFolder")] string ProposedVirtualFolder,
    [property: JsonPropertyName("protected")] bool Protected,
    [property: JsonPropertyName("organizerCreatorLabel")] string OrganizerCreatorLabel,
    [property: JsonPropertyName("organizerTypeLabel")] string OrganizerTypeLabel,
    [property: JsonPropertyName("proposalSource")] OrganizerProposalSource ProposalSource,
    [property: JsonPropertyName("needsReview")] bool NeedsReview);

public sealed record OrganizerSessionRestoreResult(
    bool CanResume,
    bool IsStale,
    string Message,
    OrganizerSessionDocument? Session);
