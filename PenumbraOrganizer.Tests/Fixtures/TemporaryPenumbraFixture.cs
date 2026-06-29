namespace PenumbraOrganizer.Tests.Fixtures;

using LiteDB;

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
    public string ModDataDbPath => Path.Combine(PenumbraConfigPath, "mod_data.db");
    public string OrganizationJsonPath => Path.Combine(PenumbraConfigPath, "mod_filesystem", "organization.json");
    public string PluginManifestPath => Path.Combine(InstalledPluginsPath, "Penumbra.json");
    public string PluginAssemblyPath => Path.Combine(InstalledPluginsPath, "Penumbra.dll");

    public void WriteMainConfig()
    {
        var json = """
        {
          "Version": 13,
          "EnableMods": true,
          "ModDirectory": "__MOD_ROOT__"
        }
        """.Replace("__MOD_ROOT__", ModRoot.Replace("\\", "\\\\", StringComparison.Ordinal));
        File.WriteAllText(PenumbraJsonPath, json);
        if (!File.Exists(OrganizationJsonPath))
            WriteOrganizationJson("""{"Folders":{},"Separators":{}}""");
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

    public void WriteModData(params (string DirectoryName, string Folder)[] rows)
    {
        using var db = new LiteDatabase($"Filename={ModDataDbPath};Connection=Direct");
        var collection = db.GetCollection("LocalModData");
        foreach (var row in rows)
        {
            var doc = new BsonDocument
            {
                ["_id"] = row.DirectoryName,
                ["Folder"] = row.Folder,
            };
            collection.Upsert(doc);
        }
    }

    public void WriteModDataDocument(BsonDocument document)
    {
        using var db = new LiteDatabase($"Filename={ModDataDbPath};Connection=Direct");
        var collection = db.GetCollection("LocalModData");
        collection.Upsert(document);
    }

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
