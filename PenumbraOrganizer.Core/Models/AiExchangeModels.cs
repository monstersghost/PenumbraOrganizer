using System.Text.Json.Serialization;

namespace PenumbraOrganizer.Core.Models;

public static class AiExchangeFormat
{
    public const int CurrentFormatVersion = 1;
}

public sealed class AiInventoryExport
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = AiExchangeFormat.CurrentFormatVersion;

    [JsonPropertyName("sourceExportId")]
    public required string SourceExportId { get; init; }

    [JsonPropertyName("generatedAtUtc")]
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("installedPenumbraVersion")]
    public string? InstalledPenumbraVersion { get; init; }

    [JsonPropertyName("organizationPreferences")]
    public required AiOrganizationPreferences OrganizationPreferences { get; init; }

    [JsonPropertyName("mods")]
    public required IReadOnlyList<AiInventoryMod> Mods { get; init; }
}

public sealed class AiInventoryMod
{
    [JsonPropertyName("scanId")]
    public required string ScanId { get; init; }

    [JsonPropertyName("protectedRow")]
    public required bool ProtectedRow { get; init; }

    [JsonPropertyName("currentVirtualFolder")]
    public required string CurrentVirtualFolder { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("website")]
    public string Website { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("recognizedMetadataFiles")]
    public IReadOnlyList<string> RecognizedMetadataFiles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("unknownMetadataFiles")]
    public IReadOnlyList<string> UnknownMetadataFiles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("malformedMetadataFiles")]
    public IReadOnlyList<string> MalformedMetadataFiles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("collectionReferenceCount")]
    public int CollectionReferenceCount { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("contentSignalSummary")]
    public string ContentSignalSummary { get; init; } = string.Empty;

    [JsonPropertyName("schemaFingerprints")]
    public IReadOnlyList<AiSchemaFingerprint> SchemaFingerprints { get; init; } = Array.Empty<AiSchemaFingerprint>();
}

public sealed class AiSchemaFingerprint
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    [JsonPropertyName("differenceKind")]
    public required string DifferenceKind { get; init; }

    [JsonPropertyName("notes")]
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class AiOrganizationPreferences
{
    [JsonPropertyName("strategy")]
    public required string Strategy { get; init; }

    [JsonPropertyName("useTypeFolders")]
    public required bool UseTypeFolders { get; init; }

    [JsonPropertyName("useCreatorFolders")]
    public required bool UseCreatorFolders { get; init; }

    [JsonPropertyName("folderOrder")]
    public required IReadOnlyList<string> FolderOrder { get; init; }

    [JsonPropertyName("fixedRootFolder")]
    public string? FixedRootFolder { get; init; }

    [JsonPropertyName("preserveMeaningfulExistingFolders")]
    public required bool PreserveMeaningfulExistingFolders { get; init; }

    [JsonPropertyName("flattenTemporarySourceFolders")]
    public required bool FlattenTemporarySourceFolders { get; init; }

    [JsonPropertyName("normalizeCreatorAliases")]
    public required bool NormalizeCreatorAliases { get; init; }

    [JsonPropertyName("unknownCreatorBehavior")]
    public required string UnknownCreatorBehavior { get; init; }

    [JsonPropertyName("unknownTypeBehavior")]
    public required string UnknownTypeBehavior { get; init; }

    [JsonPropertyName("uncertainClassificationBehavior")]
    public required string UncertainClassificationBehavior { get; init; }

    [JsonPropertyName("preserveCurrentFolderWhenUncertain")]
    public required bool PreserveCurrentFolderWhenUncertain { get; init; }

    [JsonPropertyName("customPattern")]
    public string? CustomPattern { get; init; }
}

public sealed class AiProposalDocument
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; }

    [JsonPropertyName("sourceExportId")]
    public required string SourceExportId { get; init; }

    [JsonPropertyName("generatedBy")]
    public AiProposalGeneratedBy? GeneratedBy { get; init; }

    [JsonPropertyName("summary")]
    public required AiProposalSummary Summary { get; init; }

    [JsonPropertyName("creatorAliases")]
    public IReadOnlyList<AiCreatorAlias> CreatorAliases { get; init; } = Array.Empty<AiCreatorAlias>();

    [JsonPropertyName("proposals")]
    public required IReadOnlyList<AiProposalRow> Proposals { get; init; }
}

public sealed class AiProposalGeneratedBy
{
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }
}

public sealed class AiProposalSummary
{
    [JsonPropertyName("totalRowsReceived")]
    public required int TotalRowsReceived { get; init; }

    [JsonPropertyName("totalRowsReturned")]
    public required int TotalRowsReturned { get; init; }

    [JsonPropertyName("protectedRows")]
    public required int ProtectedRows { get; init; }

    [JsonPropertyName("changedRows")]
    public required int ChangedRows { get; init; }

    [JsonPropertyName("unchangedRows")]
    public required int UnchangedRows { get; init; }

    [JsonPropertyName("reviewRows")]
    public required int ReviewRows { get; init; }
}

public sealed class AiCreatorAlias
{
    [JsonPropertyName("original")]
    public required string Original { get; init; }

    [JsonPropertyName("canonical")]
    public required string Canonical { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

public sealed class AiProposalRow
{
    [JsonPropertyName("scanId")]
    public required string ScanId { get; init; }

    [JsonPropertyName("protected")]
    public required bool Protected { get; init; }

    [JsonPropertyName("currentVirtualFolder")]
    public required string CurrentVirtualFolder { get; init; }

    [JsonPropertyName("proposedVirtualFolder")]
    public required string ProposedVirtualFolder { get; init; }

    [JsonPropertyName("proposedType")]
    public string? ProposedType { get; init; }

    [JsonPropertyName("proposedCreator")]
    public string? ProposedCreator { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record AiProposalValidationIssue(string ScanId, string Code, string Message);

public sealed record AiProposalValidationSummary(
    int InventoryRows,
    int ProposalRows,
    int AcceptedRows,
    int RejectedRows,
    int ErrorCount,
    int WarningCount);

public sealed class AiProposalValidationResult
{
    public required IReadOnlyList<AiProposalValidationIssue> Errors { get; init; }
    public required IReadOnlyList<AiProposalValidationIssue> Warnings { get; init; }
    public required IReadOnlyList<AiProposalRow> AcceptedProposals { get; init; }
    public required IReadOnlyList<AiProposalRow> RejectedProposals { get; init; }
    public required AiProposalValidationSummary Summary { get; init; }

    public bool IsValid => Errors.Count == 0;
}
