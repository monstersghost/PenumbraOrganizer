namespace PenumbraOrganizer.Tests.Fixtures;

using PenumbraOrganizer.Infrastructure.Penumbra;

public sealed class TemporaryPenumbraFixture : IDisposable
{
    public TemporaryPenumbraFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerTests", Guid.NewGuid().ToString("N"));
        BasePath = Path.Combine(RootPath, "XIVLauncher");
        PluginConfigsPath = Path.Combine(BasePath, "pluginConfigs");
        PenumbraConfigPath = Path.Combine(PluginConfigsPath, "Penumbra");
        InstalledPluginsPath = Path.Combine(BasePath, "installedPlugins", "Penumbra", "1.6.1.10");
        ModRoot = Path.Combine(RootPath, "Mods");

        Directory.CreateDirectory(PluginConfigsPath);
        Directory.CreateDirectory(PenumbraConfigPath);
        Directory.CreateDirectory(Path.Combine(PenumbraConfigPath, "collections"));
        Directory.CreateDirectory(Path.Combine(PenumbraConfigPath, "mod_filesystem"));
        Directory.CreateDirectory(InstalledPluginsPath);
        Directory.CreateDirectory(ModRoot);
    }

    public string RootPath { get; }
    public string BasePath { get; }
    public string PluginConfigsPath { get; }
    public string PenumbraConfigPath { get; }
    public string InstalledPluginsPath { get; }
    public string ModRoot { get; }

    public string PenumbraJsonPath => Path.Combine(PluginConfigsPath, "Penumbra.json");
    public string SortOrderPath => Path.Combine(PenumbraConfigPath, "sort_order.json");
    public string OrganizationJsonPath => Path.Combine(PenumbraConfigPath, "mod_filesystem", "organization.json");
    public string PluginManifestPath => Path.Combine(InstalledPluginsPath, "Penumbra.json");
    public string PluginAssemblyPath => Path.Combine(InstalledPluginsPath, "Penumbra.dll");

    public void WriteMainConfig() => WriteMainConfig(ModRoot);

    public void WriteMainConfig(string modDirectory)
    {
        var json = """
        {
          "Version": 13,
          "EnableMods": true,
          "ModDirectory": "__MOD_ROOT__"
        }
        """.Replace("__MOD_ROOT__", modDirectory.Replace("\\", "\\\\", StringComparison.Ordinal));
        File.WriteAllText(PenumbraJsonPath, json);
    }

    public void WritePluginManifest(string version = "1.6.1.10")
    {
        var json = $$"""
        {
          "Name": "Penumbra",
          "AssemblyVersion": "{{version}}"
        }
        """;
        File.WriteAllText(PluginManifestPath, json);
        File.WriteAllBytes(PluginAssemblyPath, new byte[] { 0x4D, 0x5A });
    }

    /// <summary>
    /// Writes the authoritative <c>sort_order.json</c>. Each entry's value is the full virtual
    /// path (folder + display leaf), matching Penumbra's real on-disk format.
    /// </summary>
    public void WriteSortOrder(params (string Id, string FullPath)[] entries)
        => WriteSortOrder(entries, Array.Empty<string>());

    public void WriteSortOrder((string Id, string FullPath)[] entries, string[] emptyFolders)
    {
        var payload = new
        {
            Data = entries.ToDictionary(entry => entry.Id, entry => entry.FullPath, StringComparer.Ordinal),
            EmptyFolders = emptyFolders,
        };
        File.WriteAllText(SortOrderPath, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Seeds <c>sort_order.json</c> from folder-only rows. The display leaf defaults to the mod's
    /// directory name, which is the common case for tests that only care about the folder.
    /// </summary>
    public void WriteModData(params (string DirectoryName, string Folder)[] rows)
    {
        var entries = rows
            .Select(row => (row.DirectoryName,
                FullPath: string.IsNullOrEmpty(row.Folder) ? row.DirectoryName : $"{row.Folder}/{row.DirectoryName}"))
            .ToArray();
        WriteSortOrder(entries);
    }

    /// <summary>Writes raw <c>sort_order.json</c> text for malformed-schema or unknown-field tests.</summary>
    public void WriteSortOrderRaw(string json)
        => File.WriteAllText(SortOrderPath, json);

    /// <summary>Reads back the current containing folder for a mod from <c>sort_order.json</c>.</summary>
    public string CurrentFolderOf(string modDirectoryName)
        => PenumbraSortOrder.Load(PenumbraConfigPath).GetFolderFor(modDirectoryName);

    /// <summary>Reads back the full sort path (folder + leaf) for a mod, or null if it has no entry.</summary>
    public string? CurrentSortPathOf(string modDirectoryName)
        => PenumbraSortOrder.Load(PenumbraConfigPath).GetFullPathFor(modDirectoryName);

    public string MetaJsonPathOf(string modDirectoryName) => Path.Combine(ModRoot, modDirectoryName, "meta.json");
    public string LocalModDataPathOf(string modDirectoryName) => Path.Combine(PenumbraConfigPath, "mod_data", modDirectoryName + ".json");

    /// <summary>Writes a per-user <c>mod_data/&lt;id&gt;.json</c> local-data file.</summary>
    public void WriteLocalModData(string modDirectoryName, string json)
    {
        var directory = Path.Combine(PenumbraConfigPath, "mod_data");
        Directory.CreateDirectory(directory);
        File.WriteAllText(LocalModDataPathOf(modDirectoryName), json);
    }

    public string ReadMetaJson(string modDirectoryName) => File.ReadAllText(MetaJsonPathOf(modDirectoryName));
    public string ReadLocalModData(string modDirectoryName) => File.ReadAllText(LocalModDataPathOf(modDirectoryName));

    public string CreateMod(string folderName, string metaJson, string? defaultModJson = null)
    {
        var modPath = Path.Combine(ModRoot, folderName);
        Directory.CreateDirectory(modPath);
        File.WriteAllText(Path.Combine(modPath, "meta.json"), metaJson);
        if (defaultModJson is not null)
            File.WriteAllText(Path.Combine(modPath, "default_mod.json"), defaultModJson);
        return modPath;
    }

    public void WriteCollection(string fileName, object payload)
    {
        var path = Path.Combine(PenumbraConfigPath, "collections", fileName);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    public void WriteOrganizationJson(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OrganizationJsonPath)!);
        File.WriteAllText(OrganizationJsonPath, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, true);
    }
}
