namespace PenumbraOrganizer.Infrastructure.Penumbra;

using System.Text.Json;

/// <summary>
/// Reads Penumbra's authoritative virtual-folder organization from <c>sort_order.json</c>.
/// </summary>
/// <remarks>
/// The file shape is:
/// <code>
/// {
///   "Data": { "&lt;mod directory name&gt;": "&lt;full virtual path including display leaf&gt;" },
///   "EmptyFolders": [ "Some/Empty/Folder", ... ]
/// }
/// </code>
/// The <c>Data</c> value encodes both the containing folder (everything before the last
/// <c>/</c>) and the mod's display/sort name (the final segment). A mod with no entry lives
/// at the root using its <c>meta.json</c> name as the display name.
/// </remarks>
public sealed class PenumbraSortOrder
{
    public const string FileName = "sort_order.json";

    /// <summary>
    /// The canonical empty document. A missing sort_order.json is semantically identical to this
    /// (every mod at the root), so the apply path materializes this baseline before writing.
    /// </summary>
    public const string EmptyDocumentJson = "{\n  \"Data\": {},\n  \"EmptyFolders\": []\n}";

    private readonly IReadOnlyDictionary<string, string> _data;

    private PenumbraSortOrder(IReadOnlyDictionary<string, string> data, IReadOnlyList<string> emptyFolders)
    {
        _data = data;
        EmptyFolders = emptyFolders;
    }

    /// <summary>Raw <c>Data</c> map: mod directory name -&gt; full virtual path (folder + display leaf).</summary>
    public IReadOnlyDictionary<string, string> Data => _data;

    /// <summary>Folders Penumbra tracks even though they currently contain no mods.</summary>
    public IReadOnlyList<string> EmptyFolders { get; }

    public static string GetPath(string configDirectory)
        => System.IO.Path.Combine(configDirectory, FileName);

    /// <summary>
    /// Loads <c>sort_order.json</c> from the given Penumbra config directory. A missing file
    /// is treated as an empty organization (every mod at root), not an error.
    /// </summary>
    public static PenumbraSortOrder Load(string configDirectory)
    {
        var path = GetPath(configDirectory);
        if (!File.Exists(path))
            return Empty;

        return Parse(File.ReadAllText(path));
    }

    public static PenumbraSortOrder Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<string>());

    public static PenumbraSortOrder Parse(string json)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyFolders = new List<string>();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("Data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in dataElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                        data[entry.Name] = entry.Value.GetString() ?? string.Empty;
                }
            }

            if (root.TryGetProperty("EmptyFolders", out var emptyElement) && emptyElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in emptyElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } folder)
                        emptyFolders.Add(folder);
                }
            }
        }

        return new PenumbraSortOrder(data, emptyFolders);
    }

    /// <summary>
    /// Returns the containing folder for a mod (the full sort path minus its display leaf).
    /// A mod with no entry, or one that sits at the root, returns <see cref="string.Empty"/>.
    /// </summary>
    public string GetFolderFor(string modDirectoryName)
        => _data.TryGetValue(modDirectoryName, out var fullPath)
            ? ParentFolder(fullPath)
            : string.Empty;

    /// <summary>
    /// Returns the full sort path for a mod (folder + display leaf) when an explicit entry
    /// exists, otherwise <c>null</c>. Used by the writer to preserve the original display leaf.
    /// </summary>
    public string? GetFullPathFor(string modDirectoryName)
        => _data.TryGetValue(modDirectoryName, out var fullPath) ? fullPath : null;

    /// <summary>The folder portion of a full sort path: everything before the final separator.</summary>
    public static string ParentFolder(string fullPath)
    {
        var index = fullPath.LastIndexOf('/');
        return index < 0 ? string.Empty : fullPath[..index];
    }

    /// <summary>The display/sort leaf of a full sort path: everything after the final separator.</summary>
    public static string DisplayLeaf(string fullPath)
    {
        var index = fullPath.LastIndexOf('/');
        return index < 0 ? fullPath : fullPath[(index + 1)..];
    }
}
