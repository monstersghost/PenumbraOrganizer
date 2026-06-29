namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PenumbraOrganizer.Core.Models;

/// <summary>
/// Produces whole-file change records for per-mod metadata edits:
/// author metadata in <c>meta.json</c> and per-user local data in <c>mod_data/&lt;id&gt;.json</c>.
/// Each edit preserves unknown fields (and FileVersion) by round-tripping the existing JSON.
/// </summary>
public sealed class PenumbraMetadataWriter
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public IReadOnlyList<DryRunFileChange> BuildFileChanges(
        PenumbraInstallation installation,
        IReadOnlyList<ModMetadataEdit> edits)
    {
        var changes = new List<DryRunFileChange>();
        foreach (var edit in edits)
        {
            if (edit.TouchesMetaJson)
                changes.Add(BuildMetaChange(installation, edit));
            if (edit.TouchesLocalData)
                changes.Add(BuildLocalDataChange(installation, edit));
        }

        return changes;
    }

    public static string MetaJsonPath(PenumbraInstallation installation, string stableScanId)
        => Path.Combine(installation.ModRoot, stableScanId, "meta.json");

    public static string LocalDataPath(PenumbraInstallation installation, string stableScanId)
        => Path.Combine(installation.ConfigDirectory, "mod_data", stableScanId + ".json");

    private static DryRunFileChange BuildMetaChange(PenumbraInstallation installation, ModMetadataEdit edit)
    {
        var path = MetaJsonPath(installation, edit.StableScanId);
        var root = LoadObject(path, $"meta.json is missing for mod {edit.StableScanId}.", out var sourceBytes);

        if (edit.Name is not null) root["Name"] = edit.Name;
        if (edit.Author is not null) root["Author"] = edit.Author;
        if (edit.Description is not null) root["Description"] = edit.Description;
        if (edit.Version is not null) root["Version"] = edit.Version;
        if (edit.Website is not null) root["Website"] = edit.Website;
        if (edit.ModTags is not null) root["ModTags"] = ToArray(edit.ModTags);

        return BuildChange(path, PenumbraWriteTargetKind.ModMetaJson, sourceBytes, root,
            $"meta.json:{edit.StableScanId}", "Update mod metadata in meta.json.");
    }

    private static DryRunFileChange BuildLocalDataChange(PenumbraInstallation installation, ModMetadataEdit edit)
    {
        var path = LocalDataPath(installation, edit.StableScanId);
        var root = LoadObject(path, $"Local mod data is missing for mod {edit.StableScanId}.", out var sourceBytes);

        if (edit.Favorite is not null) root["Favorite"] = edit.Favorite.Value;
        if (edit.LocalTags is not null) root["LocalTags"] = ToArray(edit.LocalTags);
        if (edit.Note is not null) root["Note"] = edit.Note;

        return BuildChange(path, PenumbraWriteTargetKind.LocalModDataJson, sourceBytes, root,
            $"mod_data:{edit.StableScanId}", "Update local mod data (favorite, tags, note).");
    }

    private static JsonObject LoadObject(string path, string missingMessage, out byte[] sourceBytes)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(missingMessage);

        sourceBytes = File.ReadAllBytes(path);
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"The metadata file is not a JSON object: {path}");
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add((JsonNode)value);
        return array;
    }

    private static DryRunFileChange BuildChange(
        string path,
        PenumbraWriteTargetKind kind,
        byte[] sourceBytes,
        JsonObject root,
        string recordKey,
        string description)
    {
        var json = root.ToJsonString(SerializerOptions);
        var expectedBytes = Encoding.UTF8.GetBytes(json);
        return new DryRunFileChange(
            path,
            kind,
            recordKey,
            Convert.ToHexString(SHA256.HashData(sourceBytes)),
            sourceBytes.LongLength,
            Convert.ToHexString(SHA256.HashData(expectedBytes)),
            expectedBytes.LongLength,
            Convert.ToBase64String(expectedBytes),
            [recordKey],
            [recordKey],
            AtomicReplaceSupported: true,
            description);
    }
}
