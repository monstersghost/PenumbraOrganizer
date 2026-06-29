namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

internal sealed record PenumbraOrganizationState(
    string OrganizationPath,
    DryRunSourceFileSnapshot SourceFile,
    SchemaFingerprint SchemaFingerprint,
    JsonObject RootObject,
    IReadOnlyDictionary<string, JsonNode?> Folders);

internal static class PenumbraOrganizationStore
{
    private const string SchemaFileName = "mod_filesystem/organization.json";
    private const string ProtectedRoot = ".Character specific mods";

    public static PenumbraOrganizationState LoadState(PenumbraInstallation installation)
    {
        var organizationPath = Path.Combine(installation.ConfigDirectory, "mod_filesystem", "organization.json");
        return LoadState(organizationPath);
    }

    public static PenumbraOrganizationState LoadState(string organizationPath)
    {
        if (!File.Exists(organizationPath))
            throw new InvalidOperationException("The authoritative Penumbra state file mod_filesystem\\organization.json is missing.");

        var raw = File.ReadAllText(organizationPath, Encoding.UTF8);
        var schema = SchemaFingerprintService.Create(SchemaFileName, raw, new HashSet<string>(StringComparer.Ordinal) { "Folders" });
        if (schema.DifferenceKind != SchemaDifferenceKind.None)
            throw new InvalidOperationException(string.Join(Environment.NewLine, schema.Notes.DefaultIfEmpty("The organization.json schema is unsupported.")));

        JsonObject root;
        try
        {
            root = JsonNode.Parse(raw)?.AsObject()
                   ?? throw new InvalidOperationException("organization.json does not contain a JSON object root.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("organization.json is invalid JSON: " + ex.Message, ex);
        }

        if (root["Folders"] is not JsonObject foldersObject)
            throw new InvalidOperationException("organization.json is missing the required Folders object.");

        var folders = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in foldersObject)
            folders[pair.Key] = pair.Value?.DeepClone();

        return new PenumbraOrganizationState(
            organizationPath,
            BuildSourceFileSnapshot(organizationPath, schema.Fingerprint),
            schema,
            root,
            folders);
    }

    public static HashSet<string> BuildFinalFolderSet(
        IReadOnlyList<DryRunPlanEntry> entries,
        PenumbraOrganizationState organizationState)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var path = entry.Protected ? entry.CurrentVirtualFolder : entry.ProposedVirtualFolder;
            AddPathAndParents(folders, path);
        }

        foreach (var protectedFolder in organizationState.Folders.Keys.Where(key => key.StartsWith(ProtectedRoot, StringComparison.OrdinalIgnoreCase)))
            AddPathAndParents(folders, protectedFolder);

        return folders;
    }

    public static JsonObject BuildOrganizationDocument(
        PenumbraOrganizationState organizationState,
        IReadOnlyCollection<string> finalFolders)
    {
        var newRoot = JsonNode.Parse(organizationState.RootObject.ToJsonString())?.AsObject()
                      ?? throw new InvalidOperationException("The original organization root could not be cloned.");
        var foldersObject = new JsonObject();

        foreach (var folder in finalFolders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
        {
            if (organizationState.Folders.TryGetValue(folder, out var existing))
                foldersObject[folder] = existing?.DeepClone();
            else
                foldersObject[folder] = new JsonObject();
        }

        newRoot["Folders"] = foldersObject;
        if (newRoot["Separators"] is null)
            newRoot["Separators"] = new JsonObject();

        return newRoot;
    }

    public static void ValidatePlannedDocument(JsonObject document, IReadOnlyCollection<string> requiredFolders)
    {
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        JsonObject parsed;
        try
        {
            parsed = JsonNode.Parse(json)?.AsObject()
                     ?? throw new InvalidOperationException("The organization document does not contain a JSON object root.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The organization document is invalid JSON: " + ex.Message, ex);
        }

        if (parsed["Folders"] is not JsonObject foldersObject)
            throw new InvalidOperationException("The organization document is missing the required Folders object.");

        foreach (var folder in requiredFolders)
        {
            if (!foldersObject.ContainsKey(folder))
                throw new InvalidOperationException($"The organization document is missing required folder {folder}.");
        }
    }

    public static string Serialize(JsonObject document)
        => document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static DryRunSourceFileSnapshot BuildSourceFileSnapshot(string path, string schemaFingerprint)
    {
        var bytes = File.ReadAllBytes(path);
        return new DryRunSourceFileSnapshot(
            path,
            bytes.LongLength,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(schemaFingerprint))));
    }

    private static void AddPathAndParents(ISet<string> folders, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < parts.Length; index++)
            folders.Add(string.Join('/', parts.Take(index + 1)));
    }
}
