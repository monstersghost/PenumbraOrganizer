namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

public sealed class PenumbraVirtualFolderWriter : IPenumbraVirtualFolderWriter
{
    private const string SchemaFileName = "sort_order.json";
    private const string DataProperty = "Data";
    private const string EmptyFoldersProperty = "EmptyFolders";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public Task<IReadOnlyList<DryRunSourceFileSnapshot>> CaptureSourceFilesAsync(PenumbraInstallation installation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = LoadState(installation);
        return Task.FromResult<IReadOnlyList<DryRunSourceFileSnapshot>>([state.SourceFile]);
    }

    public Task<IReadOnlyList<SchemaFingerprint>> CaptureSchemaFingerprintsAsync(PenumbraInstallation installation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = LoadState(installation);
        return Task.FromResult<IReadOnlyList<SchemaFingerprint>>([state.SchemaFingerprint]);
    }

    public Task<IReadOnlyList<DryRunPlanEntry>> MapPlanEntriesAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = LoadState(installation);
        var rowsById = proposalSnapshot.ValidationResult.Rows.ToDictionary(row => row.StableScanId, StringComparer.Ordinal);
        var proposalsById = proposalSnapshot.Proposals.ToDictionary(proposal => proposal.StableScanId, StringComparer.Ordinal);
        var entries = new List<DryRunPlanEntry>(inventory.Mods.Count);

        foreach (var mod in inventory.Mods.OrderBy(mod => mod.StableScanId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!rowsById.TryGetValue(mod.StableScanId, out var row))
                throw new InvalidOperationException($"The validated proposal snapshot is missing row {mod.StableScanId}.");

            proposalsById.TryGetValue(mod.StableScanId, out var proposal);
            var warnings = new List<string>(mod.Warnings);
            if (!string.IsNullOrWhiteSpace(row.Message))
                warnings.Add(row.Message);

            var effectiveProtected = mod.Protected || proposal?.Protected == true;
            var requiresWrite = row.Status == OrganizerRowStatus.ValidChange && !effectiveProtected;

            // Every installed mod is organizable: it either already has a sort_order entry, or it
            // lives at the root and we will create one. Preserve the existing display leaf so a
            // move never silently renames the mod in Penumbra's UI.
            var currentFolder = state.CurrentFolderFor(mod.StableScanId);
            if (!string.Equals(currentFolder, row.CurrentVirtualFolder, StringComparison.Ordinal))
                warnings.Add("The authoritative sort_order folder no longer matches the scan snapshot.");

            var displayLeaf = state.CurrentFullPathFor(mod.StableScanId) is { } existing
                ? PenumbraSortOrder.DisplayLeaf(existing)
                : DefaultDisplayName(mod);
            var proposedSortPath = requiresWrite
                ? BuildProposedSortPath(row.ProposedVirtualFolder, displayLeaf, mod)
                : string.Empty;

            entries.Add(new DryRunPlanEntry(
                mod.StableScanId,
                mod.PhysicalDirectoryName,
                row.CurrentVirtualFolder,
                row.ProposedVirtualFolder,
                row.Source,
                effectiveProtected,
                row.Status,
                $"{SchemaFileName}:{mod.StableScanId}",
                state.SourcePath,
                mod.StableScanId,
                state.SourceFile.Sha256,
                state.SourceFile.Sha256,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                requiresWrite,
                proposedSortPath));
        }

        return Task.FromResult<IReadOnlyList<DryRunPlanEntry>>(entries);
    }

    public async Task<IReadOnlyList<DryRunFileChange>> BuildExpectedFileChangesAsync(
        PenumbraInstallation installation,
        IReadOnlyList<DryRunPlanEntry> planEntries,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = LoadState(installation);
        var changedEntries = planEntries.Where(entry => entry.RequiresWrite).ToArray();

        var duplicateTargets = changedEntries
            .GroupBy(entry => entry.RecordKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTargets.Length > 0)
            throw new InvalidOperationException($"Duplicate authoritative state operations were detected for: {string.Join(", ", duplicateTargets)}");

        var root = LoadEditableRoot(state.SourcePath);
        var data = GetOrCreateObject(root, DataProperty);

        foreach (var entry in changedEntries.OrderBy(entry => entry.RecordKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.ProposedSortPath))
                data.Remove(entry.RecordKey);
            else
                data[entry.RecordKey] = entry.ProposedSortPath;
        }

        // The proposed folder set is authoritative: an explicitly-empty folder is a
        // manually-created proposed folder that no mod occupies. Because the scan seeds existing
        // empty folders into that set, dropping one here (delete/rename) persists, while
        // untouched ones round-trip. A folder a mod moved into is no longer empty.
        var proposedEmptyFolders = proposalSnapshot.Folders
            .Where(folder => folder.ManuallyCreated)
            .Select(folder => folder.Path)
            .Where(folder => !string.IsNullOrEmpty(folder) && !IsFolderOccupied(folder, proposalSnapshot.Proposals))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(folder => folder, StringComparer.Ordinal)
            .ToArray();
        var currentEmptyFolders = state.SortOrder.EmptyFolders.OrderBy(folder => folder, StringComparer.Ordinal).ToArray();
        var emptyFoldersChanged = !proposedEmptyFolders.SequenceEqual(currentEmptyFolders, StringComparer.Ordinal);

        if (changedEntries.Length == 0 && !emptyFoldersChanged)
            return Array.Empty<DryRunFileChange>();

        var emptyFoldersArray = new JsonArray();
        foreach (var folder in proposedEmptyFolders)
            emptyFoldersArray.Add((JsonNode)folder);
        root[EmptyFoldersProperty] = emptyFoldersArray;

        var json = root.ToJsonString(SerializerOptions);
        ValidateUpdatedSortOrder(json, state, changedEntries);

        var expectedBytes = Encoding.UTF8.GetBytes(json);
        var expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes));

        var affectedFields = new List<string>();
        if (changedEntries.Length > 0)
            affectedFields.Add($"{SchemaFileName}:{DataProperty}");
        if (emptyFoldersChanged)
            affectedFields.Add($"{SchemaFileName}:{EmptyFoldersProperty}");

        var description = changedEntries.Length > 0
            ? "Update Penumbra virtual-folder organization in sort_order.json."
            : "Update Penumbra empty-folder list in sort_order.json.";

        await Task.CompletedTask;
        return
        [
            new DryRunFileChange(
                state.SourcePath,
                PenumbraWriteTargetKind.SortOrderJson,
                affectedFields[0],
                state.SourceFile.Sha256,
                state.SourceFile.Length,
                expectedHash,
                expectedBytes.LongLength,
                Convert.ToBase64String(expectedBytes),
                changedEntries.Select(entry => entry.RecordKey).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                affectedFields,
                AtomicReplaceSupported: true,
                description)
        ];
    }

    private static bool IsFolderOccupied(string folder, IReadOnlyList<OrganizerModProposal> proposals)
        => proposals.Any(proposal =>
            !string.IsNullOrEmpty(proposal.ProposedVirtualFolder) &&
            (string.Equals(proposal.ProposedVirtualFolder, folder, StringComparison.Ordinal) ||
             proposal.ProposedVirtualFolder.StartsWith(folder + "/", StringComparison.Ordinal)));

    internal static PenumbraModDataState LoadState(PenumbraInstallation installation)
        => LoadState(PenumbraSortOrder.GetPath(installation.ConfigDirectory));

    internal static PenumbraModDataState LoadState(string sortOrderPath)
    {
        // A missing sort_order.json is treated as the canonical empty document so a fresh
        // install (every mod at the root) can still be organized. The apply path materializes
        // exactly these bytes before writing, keeping source hashes consistent. The source hash
        // must match the live file's raw bytes, so for an existing file we hash the bytes on disk.
        byte[] bytes;
        string json;
        if (File.Exists(sortOrderPath))
        {
            bytes = File.ReadAllBytes(sortOrderPath);
            json = File.ReadAllText(sortOrderPath);
        }
        else
        {
            json = PenumbraSortOrder.EmptyDocumentJson;
            bytes = Encoding.UTF8.GetBytes(json);
        }

        var (sortOrder, differenceKind, notes) = ParseAndValidate(json);

        var fingerprintInput = new StringBuilder();
        foreach (var pair in sortOrder.Data.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            fingerprintInput.Append(pair.Key).Append('|').Append(pair.Value).AppendLine();
        foreach (var folder in sortOrder.EmptyFolders.OrderBy(folder => folder, StringComparer.Ordinal))
            fingerprintInput.Append("empty:").Append(folder).AppendLine();

        var sourceFile = BuildSourceFileSnapshot(sortOrderPath, bytes, fingerprintInput.ToString());
        var schemaFingerprint = new SchemaFingerprint(
            SchemaFileName,
            sourceFile.SchemaFingerprint,
            differenceKind,
            notes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        if (differenceKind != SchemaDifferenceKind.None)
            throw new InvalidOperationException(string.Join(Environment.NewLine, schemaFingerprint.Notes.DefaultIfEmpty("The sort_order.json schema is unsupported.")));

        return new PenumbraModDataState(sortOrderPath, sourceFile, schemaFingerprint, sortOrder, sortOrder.Data.Count);
    }

    private static (PenumbraSortOrder SortOrder, SchemaDifferenceKind DifferenceKind, List<string> Notes) ParseAndValidate(string json)
    {
        var notes = new List<string>();
        var differenceKind = SchemaDifferenceKind.None;

        using (var document = JsonDocument.Parse(json))
        {
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object)
            {
                differenceKind = SchemaDifferenceKind.RootStructureChange;
                notes.Add("sort_order.json root is not a JSON object.");
            }
            else if (rootElement.TryGetProperty(DataProperty, out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in dataElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String)
                    {
                        differenceKind = SchemaDifferenceKind.RootStructureChange;
                        notes.Add($"sort_order.json entry {entry.Name} is missing a string folder path.");
                    }
                }
            }
        }

        return (PenumbraSortOrder.Parse(json), differenceKind, notes);
    }

    private static JsonObject LoadEditableRoot(string sortOrderPath)
    {
        var text = File.Exists(sortOrderPath) ? File.ReadAllText(sortOrderPath) : PenumbraSortOrder.EmptyDocumentJson;
        var node = JsonNode.Parse(text);
        return node as JsonObject ?? new JsonObject();
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static string BuildProposedSortPath(string proposedFolder, string displayLeaf, ModScanResult mod)
    {
        if (string.IsNullOrEmpty(proposedFolder))
        {
            // Returning to the root: keep an explicit entry only when the display leaf differs
            // from the default name; otherwise signal removal with an empty path.
            return string.Equals(displayLeaf, DefaultDisplayName(mod), StringComparison.Ordinal)
                ? string.Empty
                : displayLeaf;
        }

        return $"{proposedFolder}/{displayLeaf}";
    }

    private static string DefaultDisplayName(ModScanResult mod)
        => string.IsNullOrWhiteSpace(mod.Name) ? mod.PhysicalDirectoryName : mod.Name;

    private static DryRunSourceFileSnapshot BuildSourceFileSnapshot(string path, byte[] bytes, string fingerprintInput)
        => new(
            path,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))));

    private static void ValidateUpdatedSortOrder(
        string updatedJson,
        PenumbraModDataState originalState,
        IReadOnlyList<DryRunPlanEntry> changedEntries)
    {
        var updated = PenumbraSortOrder.Parse(updatedJson);
        var changedLookup = changedEntries.ToDictionary(entry => entry.RecordKey, StringComparer.Ordinal);

        foreach (var entry in changedEntries)
        {
            if (!string.Equals(updated.GetFolderFor(entry.RecordKey), entry.ProposedVirtualFolder, StringComparison.Ordinal))
                throw new InvalidOperationException($"The expected-result file did not produce the planned folder for {entry.RecordKey}.");
        }

        // Unrelated entries must be byte-for-byte unchanged.
        foreach (var original in originalState.SortOrder.Data)
        {
            if (changedLookup.ContainsKey(original.Key))
                continue;
            if (!updated.Data.TryGetValue(original.Key, out var updatedPath) || !string.Equals(updatedPath, original.Value, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unrelated sort_order entry {original.Key} changed unexpectedly.");
        }
    }
}
