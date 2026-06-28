namespace PenumbraOrganizer.Infrastructure.Scanning;

using System.Text;
using System.Text.Json;
using LiteDB;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

public sealed class PenumbraScanService : IPenumbraScanService
{
    private readonly ILogger<PenumbraScanService> _logger;
    private readonly IProtectionService _protectionService;

    public PenumbraScanService(ILogger<PenumbraScanService> logger, IProtectionService protectionService)
    {
        _logger = logger;
        _protectionService = protectionService;
    }

    public async Task<ScanInventory> ScanAsync(PenumbraInstallation installation, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Reading current folders");

        var dbFolders = await Task.Run(() => LoadCurrentFolders(installation), cancellationToken);

        progress?.Report("Reading collections");
        var collections = await Task.Run(() => LoadCollections(installation, cancellationToken), cancellationToken);

        progress?.Report("Reading installed mods");
        var collectionStatesByName = BuildCollectionStateLookup(collections);
        var physicalDirectories = Directory.Exists(installation.ModRoot)
            ? Directory.EnumerateDirectories(installation.ModRoot)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();
        var warnings = new List<string>();

        var directoryNames = new HashSet<string>(physicalDirectories.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        foreach (var dbOnly in dbFolders.Keys.Where(key => !directoryNames.Contains(key)))
            warnings.Add($"mod_data.db references a mod folder that is missing on disk: {dbOnly}");

        var duplicateNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<ModScanResult>(physicalDirectories.Count);

        for (var index = 0; index < physicalDirectories.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index == 0 || index % 50 == 0 || index == physicalDirectories.Count - 1)
                progress?.Report($"Reading installed mods ({index + 1:N0} / {physicalDirectories.Count:N0})");

            var physicalDirectory = physicalDirectories[index];
            var mod = ScanModDirectory(physicalDirectory, dbFolders, collectionStatesByName, duplicateNames);
            if (mod is not null)
                mods.Add(mod);
        }

        foreach (var mod in mods.Where(m => duplicateNames.TryGetValue(m.Name, out var count) && count > 1))
        {
            var combinedWarnings = mod.Warnings.Concat(new[] { "Another installed mod uses the same display name, so collection matching may be ambiguous." }).Distinct().ToArray();
            mod.Warnings = combinedWarnings;
        }

        progress?.Report("Preparing your library");
        var tree = BuildFolderTree(mods);

        return new ScanInventory
        {
            Installation = installation,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = mods,
            CurrentFolderTree = tree,
            Collections = collections,
            Warnings = warnings,
        };
    }

    private ModScanResult? ScanModDirectory(
        string physicalDirectory,
        IReadOnlyDictionary<string, string> dbFolders,
        IReadOnlyDictionary<string, IReadOnlyList<ModCollectionState>> collectionStatesByName,
        IDictionary<string, int> duplicateNames)
    {
        var directoryName = Path.GetFileName(physicalDirectory);
        if (string.IsNullOrWhiteSpace(directoryName))
            return null;

        var jsonFiles = Directory.EnumerateFiles(physicalDirectory, "*.json", SearchOption.TopDirectoryOnly).ToList();
        var recognized = new List<string>();
        var unknown = new List<string>();
        var malformed = new List<string>();
        var warnings = new List<string>();
        var rawFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var schemaFingerprints = new List<SchemaFingerprint>();
        string name = directoryName;
        string author = string.Empty;
        string version = string.Empty;
        string website = string.Empty;
        string description = string.Empty;
        IReadOnlyList<string> tags = Array.Empty<string>();
        var contentPaths = new List<string>();

        foreach (var file in jsonFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            var raw = File.ReadAllText(file, Encoding.UTF8);
            rawFiles[fileName] = raw;

            var isRecognized = fileName.Equals("meta.json", StringComparison.OrdinalIgnoreCase)
                               || fileName.Equals("default_mod.json", StringComparison.OrdinalIgnoreCase)
                               || (fileName.StartsWith("group_", StringComparison.OrdinalIgnoreCase)
                                   && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

            if (!isRecognized)
            {
                unknown.Add(fileName);
                continue;
            }

            recognized.Add(fileName);

            try
            {
                using var doc = JsonDocument.Parse(raw);
                schemaFingerprints.Add(CreateFingerprint(fileName, raw));

                if (fileName.Equals("meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    var root = doc.RootElement;
                    name = GetString(root, "Name", directoryName);
                    author = GetString(root, "Author");
                    version = GetString(root, "Version");
                    website = GetString(root, "Website");
                    description = GetString(root, "Description");
                    tags = GetStringArray(root, "ModTags");
                }
                else
                {
                    ExtractContentSignals(doc.RootElement, contentPaths);
                }
            }
            catch (JsonException ex)
            {
                malformed.Add(fileName);
                warnings.Add($"{fileName} is damaged or unsupported and was scanned in read-only mode.");
                schemaFingerprints.Add(new SchemaFingerprint(fileName, "invalid-json", SchemaDifferenceKind.RootStructureChange, new[] { ex.Message }));
            }
        }

        if (!recognized.Any())
            warnings.Add("This folder does not contain the usual Penumbra mod metadata files.");

        duplicateNames[name] = duplicateNames.TryGetValue(name, out var existing) ? existing + 1 : 1;

        var currentVirtualFolder = dbFolders.TryGetValue(directoryName, out var folder) ? folder : string.Empty;
        if (string.IsNullOrWhiteSpace(currentVirtualFolder))
            warnings.Add("Current Penumbra virtual folder is missing from mod_data.db.");

        collectionStatesByName.TryGetValue(name, out var collectionStates);

        return new ModScanResult
        {
            StableScanId = directoryName,
            PhysicalDirectory = physicalDirectory,
            PhysicalDirectoryName = directoryName,
            CurrentVirtualFolder = currentVirtualFolder,
            Name = name,
            Author = author,
            Version = version,
            Website = website,
            Description = description,
            Tags = tags,
            RecognizedMetadataFiles = recognized,
            UnknownMetadataFiles = unknown,
            MalformedMetadataFiles = malformed,
            CollectionStates = collectionStates ?? Array.Empty<ModCollectionState>(),
            Protected = _protectionService.IsProtectedPath(currentVirtualFolder),
            Warnings = warnings,
            ContentSignalSummary = SummarizeContentSignals(contentPaths),
            SchemaFingerprints = schemaFingerprints,
            RawMetadata = new JsonReadOnlyMemory(rawFiles),
        };
    }

    private static Dictionary<string, string> LoadCurrentFolders(PenumbraInstallation installation)
    {
        var dbPath = Path.Combine(installation.ConfigDirectory, "mod_data.db");
        if (!File.Exists(dbPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var db = new LiteDatabase($"Filename={dbPath};Connection=Shared;Timeout=00:00:02");
        var collection = db.GetCollection("LocalModData");
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in collection.FindAll())
        {
            if (!doc.TryGetValue("_id", out var idValue) || idValue.Type != BsonType.String)
                continue;

            var id = idValue.AsString;
            var folder = doc.TryGetValue("Folder", out var bson) && bson.Type == BsonType.String
                ? bson.AsString
                : string.Empty;
            folders[id] = folder;
        }

        return folders;
    }

    private static List<CollectionInventory> LoadCollections(PenumbraInstallation installation, CancellationToken cancellationToken)
    {
        var result = new List<CollectionInventory>();
        var collectionsDirectory = Path.Combine(installation.ConfigDirectory, "collections");
        if (!Directory.Exists(collectionsDirectory))
            return result;

        foreach (var path in Directory.EnumerateFiles(collectionsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var raw = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in doc.RootElement.EnumerateObject())
                    raw[property.Name] = property.Value.Clone();

                var name = doc.RootElement.TryGetProperty("Name", out var nameProperty)
                    ? nameProperty.GetString() ?? Path.GetFileNameWithoutExtension(path)
                    : Path.GetFileNameWithoutExtension(path);

                result.Add(new CollectionInventory(name, path, raw));
            }
            catch (JsonException)
            {
                result.Add(new CollectionInventory(Path.GetFileNameWithoutExtension(path), path, new Dictionary<string, JsonElement>()));
            }
        }

        return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, IReadOnlyList<ModCollectionState>> BuildCollectionStateLookup(IReadOnlyList<CollectionInventory> collections)
    {
        var lookup = new Dictionary<string, List<ModCollectionState>>(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in collections)
        {
            if (!collection.RawData.TryGetValue("Settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var setting in settings.EnumerateObject())
            {
                bool? enabled = null;
                int? priority = null;

                if (setting.Value.ValueKind == JsonValueKind.Object)
                {
                    if (setting.Value.TryGetProperty("Enabled", out var enabledProperty)
                        && (enabledProperty.ValueKind == JsonValueKind.True || enabledProperty.ValueKind == JsonValueKind.False))
                    {
                        enabled = enabledProperty.GetBoolean();
                    }

                    if (setting.Value.TryGetProperty("Priority", out var priorityProperty)
                        && priorityProperty.TryGetInt32(out var priorityValue))
                    {
                        priority = priorityValue;
                    }
                }

                if (!lookup.TryGetValue(setting.Name, out var list))
                {
                    list = new List<ModCollectionState>();
                    lookup[setting.Name] = list;
                }

                list.Add(new ModCollectionState(collection.Name, enabled, priority, false));
            }
        }

        return lookup.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<ModCollectionState>)kvp.Value.OrderBy(s => s.CollectionName, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<VirtualFolderNode> BuildFolderTree(IReadOnlyList<ModScanResult> mods)
    {
        var directCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var path = string.IsNullOrWhiteSpace(mod.CurrentVirtualFolder) ? "(unassigned)" : mod.CurrentVirtualFolder;
            directCounts[path] = directCounts.TryGetValue(path, out var current) ? current + 1 : 1;
        }

        var allPaths = new HashSet<string>(directCounts.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var path in directCounts.Keys.Where(path => path != "(unassigned)"))
        {
            var current = path;
            while (TryGetParent(current, out var parent))
            {
                allPaths.Add(parent);
                current = parent;
            }
        }

        var nodes = new List<VirtualFolderNode>(allPaths.Count);
        foreach (var path in allPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var direct = directCounts.TryGetValue(path, out var count) ? count : 0;
            var descendant = mods.Count(mod =>
            {
                var folder = string.IsNullOrWhiteSpace(mod.CurrentVirtualFolder) ? "(unassigned)" : mod.CurrentVirtualFolder;
                return folder.Equals(path, StringComparison.OrdinalIgnoreCase)
                       || folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
            });

            nodes.Add(new VirtualFolderNode(path, direct, descendant, path != "(unassigned)" && mods.Any(m => m.Protected && (m.CurrentVirtualFolder.Equals(path, StringComparison.OrdinalIgnoreCase) || m.CurrentVirtualFolder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))));
        }

        return nodes;
    }

    private static bool TryGetParent(string path, out string parent)
    {
        var index = path.LastIndexOf('/');
        if (index <= 0)
        {
            parent = string.Empty;
            return false;
        }

        parent = path[..index];
        return true;
    }

    private static SchemaFingerprint CreateFingerprint(string fileName, string json)
    {
        IReadOnlySet<string>? required = fileName.Equals("meta.json", StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FileVersion", "Name" }
            : null;

        return SchemaFingerprintService.Create(fileName, json, required);
    }

    private static void ExtractContentSignals(JsonElement root, ICollection<string> paths)
    {
        if (root.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in files.EnumerateObject())
                paths.Add(entry.Name);
        }

        if (root.TryGetProperty("Manipulations", out var manipulations) && manipulations.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in manipulations.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Slot", out var slot))
                    paths.Add("slot:" + slot.ToString());
            }
        }
    }

    private static string SummarizeContentSignals(IReadOnlyList<string> contentPaths)
    {
        if (contentPaths.Count == 0)
            return "No content signals found";

        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in contentPaths)
        {
            var category = ClassifySignal(path);
            categories[category] = categories.TryGetValue(category, out var current) ? current + 1 : 1;
        }

        return string.Join(", ", categories.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase).Take(4).Select(kvp => $"{kvp.Key} ({kvp.Value})"));
    }

    private static string ClassifySignal(string path)
    {
        if (path.Contains("/obj/hair/", StringComparison.OrdinalIgnoreCase) || path.Contains("_hir", StringComparison.OrdinalIgnoreCase))
            return "Hair";
        if (path.Contains("/equipment/", StringComparison.OrdinalIgnoreCase) || path.Contains("_top", StringComparison.OrdinalIgnoreCase) || path.Contains("_dwn", StringComparison.OrdinalIgnoreCase) || path.Contains("_sho", StringComparison.OrdinalIgnoreCase))
            return "Clothing";
        if (path.Contains("/accessory/", StringComparison.OrdinalIgnoreCase) || path.Contains("_ear", StringComparison.OrdinalIgnoreCase) || path.Contains("_nek", StringComparison.OrdinalIgnoreCase) || path.Contains("_wrs", StringComparison.OrdinalIgnoreCase))
            return "Accessory";
        if (path.Contains(".pap", StringComparison.OrdinalIgnoreCase) || path.Contains("/animation/", StringComparison.OrdinalIgnoreCase) || path.Contains("emote", StringComparison.OrdinalIgnoreCase))
            return "Animation";
        if (path.Contains("/texture/", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
            return "Textures";
        if (path.Contains("/monster/", StringComparison.OrdinalIgnoreCase))
            return "Monster or minion";
        if (path.Contains("/weapon/", StringComparison.OrdinalIgnoreCase) || path.Contains("/vfx/", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
            return "Props or VFX";
        if (path.StartsWith("slot:", StringComparison.OrdinalIgnoreCase))
            return "Item metadata";
        return "Other";
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? defaultValue
            : defaultValue;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }
}
