namespace PenumbraOrganizer.Infrastructure.Penumbra;

using System.Text.Json;

/// <summary>Per-folder metadata from organization.json's "Folders" dictionary.</summary>
public sealed record PenumbraOrganizationFolderEntry(uint? ExpandedColor, uint? CollapsedColor, string? SortMode, bool? IsSeparator)
{
    /// <summary>
    /// True when the folder has any user customization set (a color, a sort mode, or is drawn as
    /// a separator). A folder with no customization is a plain leftover; one with customization
    /// might be an intentional placeholder the user set up before populating it.
    /// </summary>
    public bool IsCustomized
        => ExpandedColor.HasValue || CollapsedColor.HasValue || !string.IsNullOrEmpty(SortMode) || IsSeparator == true;
}

public enum PenumbraOrganizationJsonLoadStatus
{
    /// <summary>organization.json does not exist at this config directory. A valid, common state --
    /// Penumbra only creates this file on a live folder-tree mutation, not at startup.</summary>
    NotFound,

    /// <summary>The file exists but is not valid JSON, its root isn't an object, or it has no
    /// numeric "Version" field.</summary>
    Malformed,

    /// <summary>The file parsed, but its Version isn't the one version this app understands.</summary>
    UnsupportedVersion,

    Success,
}

public sealed class PenumbraOrganizationJsonLoadResult
{
    private PenumbraOrganizationJsonLoadResult(PenumbraOrganizationJsonLoadStatus status, PenumbraOrganizationJson? data, int? version)
    {
        Status = status;
        Data = data;
        Version = version;
    }

    public PenumbraOrganizationJsonLoadStatus Status { get; }
    public PenumbraOrganizationJson? Data { get; }

    /// <summary>The Version field's raw value, when it could be read (Success or UnsupportedVersion only).</summary>
    public int? Version { get; }

    public static PenumbraOrganizationJsonLoadResult NotFound { get; } = new(PenumbraOrganizationJsonLoadStatus.NotFound, null, null);
    public static PenumbraOrganizationJsonLoadResult Malformed { get; } = new(PenumbraOrganizationJsonLoadStatus.Malformed, null, null);
    public static PenumbraOrganizationJsonLoadResult UnsupportedVersion(int version) => new(PenumbraOrganizationJsonLoadStatus.UnsupportedVersion, null, version);
    public static PenumbraOrganizationJsonLoadResult Success(PenumbraOrganizationJson data) => new(PenumbraOrganizationJsonLoadStatus.Success, data, data.Version);
}

/// <summary>
/// Reads Penumbra's <c>mod_filesystem/organization.json</c> -- folder-node metadata (color, sort
/// mode, separator flag) for every folder Penumbra's live filesystem tree has ever contained.
/// Schema and path confirmed against Ottermandias/Luna's <c>FileSystemSaver.Organization</c> and
/// Penumbra's <c>FilenameService.FileSystemOrganization</c>; see
/// docs/superpowers/specs/2026-07-09-organization-json-cleanup-design.md for the full trace.
/// </summary>
/// <remarks>
/// This is a read-only, scan-time model: every entry point is defensive (parse failures return a
/// <see cref="PenumbraOrganizationJsonLoadStatus"/> rather than throwing) because it runs during a
/// read-only scan that must never fail because of this file.
/// </remarks>
public sealed class PenumbraOrganizationJson
{
    public const int SupportedVersion = 1;

    private PenumbraOrganizationJson(int version, IReadOnlyDictionary<string, PenumbraOrganizationFolderEntry> folders, IReadOnlyList<string> separatorPaths)
    {
        Version = version;
        Folders = folders;
        SeparatorPaths = separatorPaths;
    }

    public int Version { get; }

    /// <summary>Folder path -&gt; folder metadata, for every folder Penumbra's live tree has ever contained.</summary>
    public IReadOnlyDictionary<string, PenumbraOrganizationFolderEntry> Folders { get; }

    /// <summary>
    /// Paths present in the "Separators" object. Read for completeness only -- this app never
    /// prunes separators; a writer built on this model must leave this object untouched.
    /// </summary>
    public IReadOnlyList<string> SeparatorPaths { get; }

    public static string GetPath(string configDirectory)
        => Path.Combine(configDirectory, "mod_filesystem", "organization.json");

    public static PenumbraOrganizationJsonLoadResult Load(string configDirectory)
    {
        var path = GetPath(configDirectory);
        if (!File.Exists(path))
            return PenumbraOrganizationJsonLoadResult.NotFound;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PenumbraOrganizationJsonLoadResult.Malformed;
        }

        return Parse(text);
    }

    public static PenumbraOrganizationJsonLoadResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return PenumbraOrganizationJsonLoadResult.Malformed;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return PenumbraOrganizationJsonLoadResult.Malformed;

            if (!root.TryGetProperty("Version", out var versionElement) || versionElement.ValueKind != JsonValueKind.Number)
                return PenumbraOrganizationJsonLoadResult.Malformed;

            if (!versionElement.TryGetInt32(out var version))
                return PenumbraOrganizationJsonLoadResult.Malformed;
            if (version != SupportedVersion)
                return PenumbraOrganizationJsonLoadResult.UnsupportedVersion(version);

            var folders = new Dictionary<string, PenumbraOrganizationFolderEntry>(StringComparer.Ordinal);
            if (root.TryGetProperty("Folders", out var foldersElement) && foldersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in foldersElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    uint? expandedColor = null;
                    if (entry.Value.TryGetProperty("ExpandedColor", out var expandedElement) && expandedElement.ValueKind == JsonValueKind.Number)
                    {
                        if (expandedElement.TryGetUInt32(out var color))
                            expandedColor = color;
                    }

                    uint? collapsedColor = null;
                    if (entry.Value.TryGetProperty("CollapsedColor", out var collapsedElement) && collapsedElement.ValueKind == JsonValueKind.Number)
                    {
                        if (collapsedElement.TryGetUInt32(out var color))
                            collapsedColor = color;
                    }
                    string? sortMode = entry.Value.TryGetProperty("SortMode", out var sortModeElement) && sortModeElement.ValueKind == JsonValueKind.String
                        ? sortModeElement.GetString()
                        : null;
                    bool? isSeparator = entry.Value.TryGetProperty("IsSeparator", out var isSeparatorElement) && isSeparatorElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? isSeparatorElement.GetBoolean()
                        : null;

                    folders[entry.Name] = new PenumbraOrganizationFolderEntry(expandedColor, collapsedColor, sortMode, isSeparator);
                }
            }

            var separatorPaths = new List<string>();
            if (root.TryGetProperty("Separators", out var separatorsElement) && separatorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in separatorsElement.EnumerateObject())
                    separatorPaths.Add(entry.Name);
            }

            return PenumbraOrganizationJsonLoadResult.Success(new PenumbraOrganizationJson(version, folders, separatorPaths));
        }
    }
}
