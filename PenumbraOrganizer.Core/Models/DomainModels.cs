using System.Collections.ObjectModel;
using System.Text.Json;

namespace PenumbraOrganizer.Core.Models;

public enum AppOperationState
{
    Scan,
    Review,
    DryRun,
    Apply,
}

public enum DiscoveryConfidence
{
    Low,
    Medium,
    High,
    Manual,
}

public enum CompatibilityStatus
{
    Compatible,
    VersionChangedSchemaKnown,
    VersionChangedNeedsReview,
    UnknownSchema,
    PenumbraNotFound,
    ConfigurationInvalid,
}

public enum SchemaDifferenceKind
{
    None,
    AdditiveOptionalChange,
    MissingKnownRequiredField,
    TypeChange,
    RootStructureChange,
    UnrecognizedFileType,
}

public enum RuleActionType
{
    SetProposedCategory,
    SetCreatorFolder,
    FlattenSourceFolders,
    PreserveMeaningfulSourceFolder,
    CanonicalizeCreator,
    MarkProtected,
    SendToReview,
    LeaveUnchanged,
}

public enum OrganizationStrategy
{
    StartManually,
    CreatorOnly,
    TypeOnly,
    TypeThenCreator,
    CreatorThenType,
    PreserveAndClean,
    Custom,
}

public enum OrganizationFolderComponent
{
    FixedRoot,
    Creator,
    Type,
}

public enum UnknownCreatorBehavior
{
    PlaceUnderSelectedType,
    PreserveCurrent,
    Review,
    NotApplicable,
}

public enum UnknownTypeBehavior
{
    PlaceUnderCreator,
    PreserveCurrent,
    Review,
    NotApplicable,
}

public enum UncertainClassificationBehavior
{
    Review,
    PreserveCurrent,
    UseBestSupportedWithWarning,
}

public enum ProposalSource
{
    Manual,
    DeterministicRule,
    PreservedCurrent,
    RestoredByUndo,
}

public sealed record DiscoveryEvidence(string Source, string Detail);

public sealed record OrganizationPreferences(
    OrganizationStrategy Strategy,
    bool UseTypeFolders,
    bool UseCreatorFolders,
    IReadOnlyList<OrganizationFolderComponent> FolderOrder,
    string? FixedRootFolder,
    bool PreserveMeaningfulExistingFolders,
    bool FlattenTemporarySourceFolders,
    bool NormalizeCreatorAliases,
    UnknownCreatorBehavior UnknownCreatorBehavior,
    UnknownTypeBehavior UnknownTypeBehavior,
    UncertainClassificationBehavior UncertainClassificationBehavior,
    bool PreserveCurrentFolderWhenUncertain,
    string? CustomPattern)
{
    public static OrganizationPreferences DefaultManual { get; } = new(
        OrganizationStrategy.PreserveAndClean,
        UseTypeFolders: false,
        UseCreatorFolders: false,
        FolderOrder: Array.Empty<OrganizationFolderComponent>(),
        FixedRootFolder: null,
        PreserveMeaningfulExistingFolders: true,
        FlattenTemporarySourceFolders: true,
        NormalizeCreatorAliases: true,
        UnknownCreatorBehavior.PreserveCurrent,
        UnknownTypeBehavior.PreserveCurrent,
        UncertainClassificationBehavior.Review,
        PreserveCurrentFolderWhenUncertain: true,
        CustomPattern: null);
}

public sealed record OrganizerLabelOverrides(
    string? CreatorLabel,
    string? TypeLabel,
    string? PreferredCanonicalCreatorName,
    bool? MeaningfulExistingFolder,
    bool? TemporarySourceFolder);

public sealed record PenumbraInstallation(
    string ConfigurationPath,
    string ConfigDirectory,
    string ModRoot,
    string? PluginAssemblyPath,
    string? PluginManifestPath,
    string? InstalledVersion,
    DiscoveryConfidence Confidence,
    IReadOnlyList<DiscoveryEvidence> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record DiscoveryResult(
    IReadOnlyList<PenumbraInstallation> Installations,
    bool RequiresManualSelection,
    IReadOnlyList<string> Errors);

public sealed record SchemaFingerprint(
    string FileName,
    string Fingerprint,
    SchemaDifferenceKind DifferenceKind,
    IReadOnlyList<string> Notes);

public sealed record ModCollectionState(
    string CollectionName,
    bool? Enabled,
    int? Priority,
    bool Inherited);

public sealed class ModScanResult
{
    public required string StableScanId { get; init; }
    public required string PhysicalDirectory { get; init; }
    public required string PhysicalDirectoryName { get; init; }
    public required string CurrentVirtualFolder { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecognizedMetadataFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnknownMetadataFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MalformedMetadataFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModCollectionState> CollectionStates { get; init; } = Array.Empty<ModCollectionState>();
    public bool Protected { get; init; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string ContentSignalSummary { get; init; } = string.Empty;
    public IReadOnlyList<SchemaFingerprint> SchemaFingerprints { get; init; } = Array.Empty<SchemaFingerprint>();
    public JsonReadOnlyMemory RawMetadata { get; init; } = JsonReadOnlyMemory.Empty;
}

public readonly record struct JsonReadOnlyMemory(IReadOnlyDictionary<string, string> Files)
{
    public static JsonReadOnlyMemory Empty => new(new Dictionary<string, string>());
}

public sealed record VirtualFolderNode(string Path, int DirectModCount, int DescendantModCount, bool Protected);

public sealed record CollectionInventory(
    string Name,
    string FilePath,
    IReadOnlyDictionary<string, JsonElement> RawData);

public sealed class ScanInventory
{
    public required PenumbraInstallation Installation { get; init; }
    public required DateTimeOffset ScannedAtUtc { get; init; }
    public required IReadOnlyList<ModScanResult> Mods { get; init; }
    public required IReadOnlyList<VirtualFolderNode> CurrentFolderTree { get; init; }
    public required IReadOnlyList<CollectionInventory> Collections { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record CompatibilityReport(
    CompatibilityStatus Status,
    string InstalledVersion,
    string ScannedVersion,
    IReadOnlyList<SchemaFingerprint> SchemaFingerprints,
    IReadOnlyList<string> Warnings);

public sealed record OrganizationRuleCondition(string Field, string Operator, string Value);

public sealed record OrganizationRuleAction(RuleActionType ActionType, string Value);

public sealed class OrganizationRule
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool Enabled { get; set; }
    public required int Order { get; set; }
    public required IReadOnlyList<OrganizationRuleCondition> Conditions { get; init; }
    public required IReadOnlyList<OrganizationRuleAction> Actions { get; init; }
}

public sealed class ProposedOrganizationRow
{
    public required string StableScanId { get; init; }
    public required string PhysicalDirectoryName { get; init; }
    public required string CurrentVirtualFolder { get; init; }
    public required string ProposedVirtualFolder { get; set; }
    public required string Name { get; init; }
    public string Author { get; init; } = string.Empty;
    public bool Protected { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ProposalSource Source { get; set; } = ProposalSource.PreservedCurrent;
    public string? ProposedType { get; set; }
    public string? ProposedCreator { get; set; }
    public bool ManuallyOverridden { get; set; }
    public OrganizerLabelOverrides? LabelOverrides { get; set; }
}
