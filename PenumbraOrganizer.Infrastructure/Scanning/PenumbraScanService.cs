namespace PenumbraOrganizer.Infrastructure.Scanning;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Classification;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Penumbra;

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

        var warnings = new List<string>();
        var (dbFolders, emptyFolders, modDataDbEntries, missingFolderReferenceSource) =
            await Task.Run(() => LoadOrganization(installation, warnings), cancellationToken);

        progress?.Report("Reading collections");
        var collections = await Task.Run(() => LoadCollections(installation, cancellationToken), cancellationToken);

        progress?.Report("Reading installed mods");
        var collectionStatesByName = BuildCollectionStateLookup(collections);
        var physicalDirectories = Directory.Exists(installation.ModRoot)
            ? Directory.EnumerateDirectories(installation.ModRoot).ToList()
            : new List<string>();

        var directoryNames = new HashSet<string>(physicalDirectories.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        foreach (var dbOnly in dbFolders.Keys.Where(key => !directoryNames.Contains(key)))
            warnings.Add($"{missingFolderReferenceSource} references a mod folder that is missing on disk: {dbOnly}");

        var modDataDirectory = Path.Combine(installation.ConfigDirectory, "mod_data");

        // This reads every installed mod's meta.json / group_*.json / local mod_data file
        // synchronously, which can be hundreds of files. Run it on the thread pool so the UI
        // thread keeps pumping messages (repainting the progress overlay, responding to
        // Windows) instead of blocking for the whole scan.
        var (mods, duplicateNames) = await Task.Run(() =>
        {
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ModScanResult>(physicalDirectories.Count);
            foreach (var physicalDirectory in physicalDirectories.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mod = ScanModDirectory(physicalDirectory, dbFolders, collectionStatesByName, names, modDataDirectory, modDataDbEntries);
                if (mod is not null)
                    results.Add(mod);
            }
            return (results, names);
        }, cancellationToken);

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
            EmptyFolders = emptyFolders,
        };
    }

    // Penumbra has shipped more than one storage format for virtual-folder organization, and an
    // install can be sitting on either one depending on version history (see
    // PenumbraOrganizationBackendSelector). This picks the authoritative source for the scan and
    // reports why, rather than silently assuming sort_order.json.
    private static (
        IReadOnlyDictionary<string, string> DbFolders,
        IReadOnlyList<string> EmptyFolders,
        IReadOnlyDictionary<string, PenumbraModDataDbEntry>? ModDataDbEntries,
        string MissingFolderReferenceSource)
        LoadOrganization(PenumbraInstallation installation, ICollection<string> warnings)
    {
        var backend = PenumbraOrganizationBackendSelector.Detect(installation.ConfigDirectory);
        if (backend == PenumbraOrganizationBackend.ModDataDb)
        {
            var loadResult = PenumbraModDataDb.Load(installation.ConfigDirectory, installation);
            if (loadResult.Status == PenumbraModDataDbLoadStatus.Success)
            {
                var entries = loadResult.Data!.Entries;
                var folders = entries.ToDictionary(kv => kv.Key, kv => kv.Value.Folder, StringComparer.OrdinalIgnoreCase);
                warnings.Add(
                    "This Penumbra install stores virtual folders in mod_data.db, not sort_order.json. Viewing " +
                    "and protecting mods works normally; reorganizing (moving mods) isn't supported yet for this format.");
                return (folders, loadResult.Data.EmptyFolders, entries, PenumbraModDataDb.FileName);
            }

            warnings.Add(
                (loadResult.ErrorMessage ?? "mod_data.db could not be read.") +
                " Falling back to sort_order.json, which may be out of date.");
        }

        var sortOrder = PenumbraSortOrder.Load(installation.ConfigDirectory);
        if (sortOrder.LoadedFromBackup)
        {
            warnings.Add(
                "sort_order.json was missing, so your current folder structure was recovered from Penumbra's own " +
                "sort_order.json.bak. This is read-only and does not change anything; open Penumbra once and it " +
                "will normally rewrite sort_order.json on its own.");
        }

        var dbFolders = sortOrder.Data.Keys.ToDictionary(id => id, sortOrder.GetFolderFor, StringComparer.OrdinalIgnoreCase);
        return (dbFolders, sortOrder.EmptyFolders, null, PenumbraSortOrder.FileName);
    }

    private ModScanResult? ScanModDirectory(
        string physicalDirectory,
        IReadOnlyDictionary<string, string> dbFolders,
        IReadOnlyDictionary<string, IReadOnlyList<ModCollectionState>> collectionStatesByName,
        IDictionary<string, int> duplicateNames,
        string modDataDirectory,
        IReadOnlyDictionary<string, PenumbraModDataDbEntry>? modDataDbEntries)
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

                    // Penumbra 1.7.0+ folds default_mod.json and group_*.json into meta.json
                    // itself, under "DefaultData" and "Groups" respectively.
                    ExtractContentSignals(root, contentPaths);
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

        // No sort_order.json entry (or an empty folder value) means the mod sits at the
        // Penumbra root under its default display name. That is normal, not a warning.
        var currentVirtualFolder = dbFolders.TryGetValue(directoryName, out var folder) ? folder : string.Empty;

        collectionStatesByName.TryGetValue(name, out var collectionStates);

        var localData = modDataDbEntries is not null
            ? ReadLocalModDataFromDb(modDataDbEntries, directoryName)
            : ReadLocalModData(modDataDirectory, directoryName, warnings);

        var targets = ModPathClassifier.Classify(contentPaths);
        var (detectedCategory, detectedSubcategory) = ModPathClassifier.Resolve(targets);
        var isHeliosphereManaged = HeliosphereModDetector.IsHeliosphereManaged(directoryName, jsonFiles.Select(Path.GetFileName)!);

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
            Favorite = localData.Favorite,
            LocalTags = localData.LocalTags,
            Note = localData.Note,
            HasLocalData = localData.Exists,
            RecognizedMetadataFiles = recognized,
            UnknownMetadataFiles = unknown,
            MalformedMetadataFiles = malformed,
            CollectionStates = collectionStates ?? Array.Empty<ModCollectionState>(),
            Protected = isHeliosphereManaged || _protectionService.IsProtectedPath(currentVirtualFolder),
            IsHeliosphereManaged = isHeliosphereManaged,
            Warnings = warnings,
            ContentSignalSummary = SummarizeContentSignals(contentPaths),
            SchemaFingerprints = schemaFingerprints,
            RawMetadata = new JsonReadOnlyMemory(rawFiles),
            Targets = targets,
            DetectedCategory = detectedCategory,
            DetectedSubcategory = detectedSubcategory,
        };
    }

    private readonly record struct LocalModData(bool Exists, bool Favorite, IReadOnlyList<string> LocalTags, string Note);

    private static LocalModData ReadLocalModData(string modDataDirectory, string stableScanId, ICollection<string> warnings)
    {
        var path = Path.Combine(modDataDirectory, stableScanId + ".json");
        if (!File.Exists(path))
            return new LocalModData(false, false, Array.Empty<string>(), string.Empty);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            var favorite = root.TryGetProperty("Favorite", out var fav)
                           && (fav.ValueKind == JsonValueKind.True || fav.ValueKind == JsonValueKind.False)
                           && fav.GetBoolean();
            return new LocalModData(true, favorite, GetStringArray(root, "LocalTags"), GetString(root, "Note"));
        }
        catch (JsonException ex)
        {
            warnings.Add("Local mod data (mod_data) is damaged and was skipped: " + ex.Message);
            return new LocalModData(false, false, Array.Empty<string>(), string.Empty);
        }
    }

    private static LocalModData ReadLocalModDataFromDb(IReadOnlyDictionary<string, PenumbraModDataDbEntry> entries, string stableScanId)
        => entries.TryGetValue(stableScanId, out var entry)
            ? new LocalModData(true, entry.Favorite, entry.LocalTags, entry.Note)
            : new LocalModData(false, false, Array.Empty<string>(), string.Empty);

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
        ExtractFilesAndManipulations(root, paths);

        // Penumbra 1.7.0+ meta.json: the old default_mod.json content lives under "DefaultData".
        if (root.TryGetProperty("DefaultData", out var defaultData) && defaultData.ValueKind == JsonValueKind.Object)
            ExtractFilesAndManipulations(defaultData, paths);

        ExtractOptionsAndContainers(root, paths);

        // Penumbra 1.7.0+ meta.json: each old group_*.json is now an entry in "Groups".
        if (root.TryGetProperty("Groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
                ExtractOptionsAndContainers(group, paths);
        }
    }

    private static void ExtractOptionsAndContainers(JsonElement element, ICollection<string> paths)
    {
        if (element.TryGetProperty("Options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in options.EnumerateArray())
                ExtractFilesAndManipulations(option, paths);
        }

        if (element.TryGetProperty("Containers", out var containers) && containers.ValueKind == JsonValueKind.Array)
        {
            foreach (var container in containers.EnumerateArray())
                ExtractFilesAndManipulations(container, paths);
        }
    }

    private static void ExtractFilesAndManipulations(JsonElement element, ICollection<string> paths)
    {
        if (element.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in files.EnumerateObject())
                paths.Add(entry.Name);
        }

        if (element.TryGetProperty("Manipulations", out var manipulations) && manipulations.ValueKind == JsonValueKind.Array)
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
