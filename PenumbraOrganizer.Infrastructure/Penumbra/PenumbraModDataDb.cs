namespace PenumbraOrganizer.Infrastructure.Penumbra;

using System.Reflection;
using PenumbraOrganizer.Core.Models;

/// <summary>Per-mod data read from a <c>mod_data.db</c> <c>LocalModData</c> document.</summary>
public sealed record PenumbraModDataDbEntry(string Folder, bool Favorite, IReadOnlyList<string> LocalTags, string Note);

public enum PenumbraModDataDbLoadStatus
{
    /// <summary><c>mod_data.db</c> does not exist at this config directory.</summary>
    NotFound,

    /// <summary>
    /// <c>mod_data.db</c> exists, but the LiteDB engine that reads it (<c>LiteDB.dll</c>, shipped
    /// next to the installed Penumbra plugin) could not be located or loaded.
    /// </summary>
    EngineUnavailable,

    /// <summary>The LiteDB engine loaded, but reading the file threw. See <see cref="PenumbraModDataDbLoadResult.ErrorMessage"/>.</summary>
    Failed,

    Success,
}

public sealed class PenumbraModDataDbLoadResult
{
    private PenumbraModDataDbLoadResult(PenumbraModDataDbLoadStatus status, PenumbraModDataDb? data, string? errorMessage)
    {
        Status = status;
        Data = data;
        ErrorMessage = errorMessage;
    }

    public PenumbraModDataDbLoadStatus Status { get; }
    public PenumbraModDataDb? Data { get; }
    public string? ErrorMessage { get; }

    public static PenumbraModDataDbLoadResult NotFound { get; } = new(PenumbraModDataDbLoadStatus.NotFound, null, null);

    public static PenumbraModDataDbLoadResult EngineUnavailable { get; } = new(
        PenumbraModDataDbLoadStatus.EngineUnavailable,
        null,
        "Penumbra's LiteDB engine (LiteDB.dll) could not be found next to the installed Penumbra plugin, so mod_data.db could not be read.");

    public static PenumbraModDataDbLoadResult Failed(string message) => new(PenumbraModDataDbLoadStatus.Failed, null, message);

    public static PenumbraModDataDbLoadResult Success(PenumbraModDataDb data) => new(PenumbraModDataDbLoadStatus.Success, data, null);
}

/// <summary>
/// Reads Penumbra's per-mod virtual-folder organization from <c>mod_data.db</c>, a LiteDB database
/// some Penumbra versions use instead of <c>sort_order.json</c> (collection <c>LocalModData</c>,
/// one document per mod keyed by its physical directory name, with a <c>Folder</c> string field).
/// Unlike <c>sort_order.json</c>'s combined "folder+display leaf" paths, <c>Folder</c> here is
/// purely the containing folder.
/// </summary>
/// <remarks>
/// All LiteDB access goes through reflection/<c>dynamic</c> against an assembly resolved by
/// <see cref="LiteDbAssemblyLoader"/> rather than a compile-time package reference — see that
/// type's remarks for why. Every entry point is defensive: an unexpected LiteDB API shape degrades
/// to a missing field (or an overall <see cref="PenumbraModDataDbLoadStatus.Failed"/>) rather than
/// throwing into caller code, since this path is read-only and must never take down a scan.
/// </remarks>
public sealed class PenumbraModDataDb
{
    public const string FileName = "mod_data.db";
    public const string CollectionName = "LocalModData";

    private readonly IReadOnlyDictionary<string, PenumbraModDataDbEntry> _entries;

    private PenumbraModDataDb(IReadOnlyDictionary<string, PenumbraModDataDbEntry> entries)
    {
        _entries = entries;
    }

    public static string GetPath(string configDirectory) => Path.Combine(configDirectory, FileName);

    /// <summary>Mod directory name -&gt; local data, for every mod that has a <c>LocalModData</c> document.</summary>
    public IReadOnlyDictionary<string, PenumbraModDataDbEntry> Entries => _entries;

    public string GetFolderFor(string modDirectoryName)
        => _entries.TryGetValue(modDirectoryName, out var entry) ? entry.Folder : string.Empty;

    public PenumbraModDataDbEntry? GetEntry(string modDirectoryName)
        => _entries.TryGetValue(modDirectoryName, out var entry) ? entry : null;

    /// <summary>
    /// This backend's empty-folder bookkeeping lives in <c>mod_filesystem/organization.json</c>,
    /// which is out of scope for this pass (see <see cref="PenumbraOrganizationBackendSelector"/>).
    /// </summary>
    public IReadOnlyList<string> EmptyFolders => Array.Empty<string>();

    public static PenumbraModDataDbLoadResult Load(string configDirectory, PenumbraInstallation installation)
    {
        var path = GetPath(configDirectory);
        if (!File.Exists(path))
            return PenumbraModDataDbLoadResult.NotFound;

        var assembly = LiteDbAssemblyLoader.TryLoad(installation);
        if (assembly is null)
            return PenumbraModDataDbLoadResult.EngineUnavailable;

        try
        {
            var entries = ReadEntries(path, assembly);
            return PenumbraModDataDbLoadResult.Success(new PenumbraModDataDb(entries));
        }
        catch (Exception ex)
        {
            return PenumbraModDataDbLoadResult.Failed(ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, PenumbraModDataDbEntry> ReadEntries(string path, Assembly liteDbAssembly)
    {
        var databaseType = liteDbAssembly.GetType("LiteDB.LiteDatabase")
            ?? throw new InvalidOperationException("LiteDB.dll did not expose the expected LiteDB.LiteDatabase type.");

        // LiteDatabase's (string connectionString, BsonMapper? mapper = null) constructor has two
        // declared parameters even though the second is optional at the C# call site.
        // Activator.CreateInstance(type, args) requires an exact argument-count match and does not
        // fill in trailing optional parameters, so it must be invoked via reflection directly,
        // passing Type.Missing for anything after the connection string to pick up its default.
        var constructor = databaseType.GetConstructors()
            .FirstOrDefault(candidate => candidate.GetParameters() is { Length: > 0 } parameters && parameters[0].ParameterType == typeof(string))
            ?? throw new InvalidOperationException("LiteDB.LiteDatabase does not expose a constructor accepting a connection string.");

        // Connection=Shared lets this coexist with a running Penumbra/FFXIV holding the file open,
        // matching the connection string already proven to work against a live install.
        var connectionString = $"Filename={path};Connection=Shared;Timeout=00:00:02";
        var constructorArgs = constructor.GetParameters()
            .Select((_, index) => index == 0 ? connectionString : Type.Missing)
            .ToArray();
        dynamic database = constructor.Invoke(constructorArgs)
            ?? throw new InvalidOperationException("Could not construct LiteDB.LiteDatabase.");

        try
        {
            var result = new Dictionary<string, PenumbraModDataDbEntry>(StringComparer.OrdinalIgnoreCase);
            dynamic collection = database.GetCollection(CollectionName);

            foreach (dynamic doc in collection.FindAll())
            {
                string id = doc["_id"].AsString;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var folder = doc.ContainsKey("Folder") ? (string)doc["Folder"].AsString : string.Empty;

                // Field names beyond "_id"/"Folder" are inferred, not proven against a real file,
                // so a wrong guess must degrade to blank local data instead of failing the whole
                // (proven-correct) folder read.
                var favorite = false;
                var note = string.Empty;
                var tags = new List<string>();
                try
                {
                    favorite = doc.ContainsKey("Favorite") && (bool)doc["Favorite"].AsBoolean;
                    note = doc.ContainsKey("Note") ? (string)doc["Note"].AsString : string.Empty;
                    if (doc.ContainsKey("LocalTags"))
                    {
                        foreach (dynamic tag in doc["LocalTags"].AsArray)
                            tags.Add((string)tag.AsString);
                    }
                }
                catch
                {
                    favorite = false;
                    note = string.Empty;
                    tags = new List<string>();
                }

                result[id] = new PenumbraModDataDbEntry(folder, favorite, tags, note);
            }

            return result;
        }
        finally
        {
            ((IDisposable)database).Dispose();
        }
    }
}
