namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using System.Text;
using LiteDB;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class PenumbraVirtualFolderWriter : IPenumbraVirtualFolderWriter
{
    private const string CollectionName = "LocalModData";
    private const string FolderFieldName = "Folder";
    private const string SchemaFileName = "mod_data.db:LocalModData";

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
            var requiresWrite =
                row.Status == OrganizerRowStatus.ValidChange &&
                !effectiveProtected &&
                state.Entries.ContainsKey(mod.StableScanId);

            if (!state.Entries.TryGetValue(mod.StableScanId, out var mapping))
            {
                warnings.Add("No authoritative LocalModData entry exists for this installed mod.");
                entries.Add(new DryRunPlanEntry(
                    mod.StableScanId,
                    mod.PhysicalDirectoryName,
                    row.CurrentVirtualFolder,
                    row.ProposedVirtualFolder,
                    row.Source,
                    effectiveProtected,
                    OrganizerRowStatus.MissingMod,
                    "missing:LocalModData",
                    state.DatabasePath,
                    mod.StableScanId,
                    state.SourceFile.Sha256,
                    state.SourceFile.Sha256,
                    warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    RequiresWrite: false));
                continue;
            }

            if (!string.Equals(mapping.Folder, row.CurrentVirtualFolder, StringComparison.Ordinal))
                warnings.Add("The authoritative LocalModData folder no longer matches the scan snapshot.");

            entries.Add(new DryRunPlanEntry(
                mod.StableScanId,
                mod.PhysicalDirectoryName,
                row.CurrentVirtualFolder,
                row.ProposedVirtualFolder,
                row.Source,
                effectiveProtected,
                row.Status,
                $"{CollectionName}:{mapping.Id}:{FolderFieldName}",
                state.DatabasePath,
                mapping.Id,
                state.SourceFile.Sha256,
                state.SourceFile.Sha256,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                requiresWrite));
        }

        return Task.FromResult<IReadOnlyList<DryRunPlanEntry>>(entries);
    }

    public async Task<IReadOnlyList<DryRunFileChange>> BuildExpectedFileChangesAsync(
        PenumbraInstallation installation,
        IReadOnlyList<DryRunPlanEntry> planEntries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = LoadState(installation);
        var changedEntries = planEntries.Where(entry => entry.RequiresWrite).ToArray();
        if (changedEntries.Length == 0)
            return Array.Empty<DryRunFileChange>();

        var duplicateTargets = changedEntries
            .GroupBy(entry => entry.RecordKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTargets.Length > 0)
            throw new InvalidOperationException($"Duplicate authoritative state operations were detected for: {string.Join(", ", duplicateTargets)}");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerDryRun", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempDatabasePath = Path.Combine(tempDirectory, Path.GetFileName(state.DatabasePath));
        try
        {
            File.Copy(state.DatabasePath, tempDatabasePath, overwrite: true);
            using (var db = new LiteDatabase($"Filename={tempDatabasePath};Connection=Direct"))
            {
                var collection = db.GetCollection(CollectionName);
                foreach (var entry in changedEntries.OrderBy(entry => entry.RecordKey, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = collection.FindById(entry.RecordKey)
                        ?? throw new InvalidOperationException($"The authoritative LocalModData record {entry.RecordKey} is missing.");
                    document[FolderFieldName] = entry.ProposedVirtualFolder;
                    if (!collection.Update(document))
                        throw new InvalidOperationException($"The authoritative LocalModData record {entry.RecordKey} could not be updated.");
                }
                db.Checkpoint();
            }

            ValidateUpdatedDatabase(tempDatabasePath, state, changedEntries);
            var expectedBytes = await File.ReadAllBytesAsync(tempDatabasePath, cancellationToken);
            var expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes));

            return
            [
                new DryRunFileChange(
                    state.DatabasePath,
                    PenumbraWriteTargetKind.ModDataDatabase,
                    $"{CollectionName}.{FolderFieldName}",
                    state.SourceFile.Sha256,
                    state.SourceFile.Length,
                    expectedHash,
                    expectedBytes.LongLength,
                    Convert.ToBase64String(expectedBytes),
                    changedEntries.Select(entry => entry.RecordKey).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    [$"{CollectionName}.{FolderFieldName}"],
                    AtomicReplaceSupported: true,
                    "Update Penumbra LocalModData folder mappings in mod_data.db.")
            ];
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    internal static PenumbraModDataState LoadState(PenumbraInstallation installation)
    {
        var databasePath = Path.Combine(installation.ConfigDirectory, "mod_data.db");
        return LoadState(databasePath);
    }

    internal static PenumbraModDataState LoadState(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new InvalidOperationException("The authoritative Penumbra state file mod_data.db is missing.");

        var entries = new Dictionary<string, PenumbraModDataEntry>(StringComparer.Ordinal);
        var fingerprintInput = new StringBuilder();
        var notes = new List<string>();
        var differenceKind = SchemaDifferenceKind.None;

        using (var db = new LiteDatabase($"Filename={databasePath};Connection=Shared;Timeout=00:00:02"))
        {
            var collection = db.GetCollection(CollectionName);
            foreach (var doc in collection.FindAll().OrderBy(document => document["_id"].ToString(), StringComparer.Ordinal))
            {
                if (!doc.TryGetValue("_id", out var idValue) || idValue.Type != BsonType.String)
                {
                    differenceKind = SchemaDifferenceKind.RootStructureChange;
                    notes.Add("LocalModData contains a non-string _id value.");
                    continue;
                }

                var id = idValue.AsString;
                if (!doc.TryGetValue(FolderFieldName, out var folderValue) || folderValue.Type != BsonType.String)
                {
                    differenceKind = SchemaDifferenceKind.RootStructureChange;
                    notes.Add($"LocalModData entry {id} is missing a string Folder field.");
                    continue;
                }

                var normalizedDoc = new BsonDocument(doc);
                entries[id] = new PenumbraModDataEntry(id, folderValue.AsString, normalizedDoc);
                fingerprintInput.Append(id)
                    .Append('|')
                    .Append(string.Join(',', normalizedDoc.Keys.OrderBy(key => key, StringComparer.Ordinal)))
                    .Append('|')
                    .Append(folderValue.AsString)
                    .AppendLine();
            }
        }

        var sourceFile = BuildSourceFileSnapshot(databasePath, fingerprintInput.ToString());
        var schemaFingerprint = new SchemaFingerprint(
            SchemaFileName,
            sourceFile.SchemaFingerprint,
            differenceKind,
            notes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        if (differenceKind != SchemaDifferenceKind.None)
            throw new InvalidOperationException(string.Join(Environment.NewLine, schemaFingerprint.Notes.DefaultIfEmpty("The LocalModData schema is unsupported.")));

        return new PenumbraModDataState(databasePath, sourceFile, schemaFingerprint, entries, entries.Count);
    }

    private static DryRunSourceFileSnapshot BuildSourceFileSnapshot(string path, string fingerprintInput)
    {
        var bytes = File.ReadAllBytes(path);
        return new DryRunSourceFileSnapshot(
            path,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))));
    }

    private static void ValidateUpdatedDatabase(
        string updatedDatabasePath,
        PenumbraModDataState originalState,
        IReadOnlyList<DryRunPlanEntry> changedEntries)
    {
        var changedLookup = changedEntries.ToDictionary(entry => entry.RecordKey, StringComparer.Ordinal);
        using var db = new LiteDatabase($"Filename={updatedDatabasePath};Connection=Direct");
        var collection = db.GetCollection(CollectionName);
        var updated = collection.FindAll().ToDictionary(doc => doc["_id"].AsString, doc => new BsonDocument(doc), StringComparer.Ordinal);

        if (updated.Count != originalState.RecordCount)
            throw new InvalidOperationException("The expected-result copy changed the authoritative record count.");

        foreach (var original in originalState.Entries)
        {
            if (!updated.TryGetValue(original.Key, out var updatedDoc))
                throw new InvalidOperationException($"The expected-result copy removed authoritative entry {original.Key}.");

            var updatedFolder = updatedDoc[FolderFieldName].AsString;
            if (changedLookup.TryGetValue(original.Key, out var changed))
            {
                if (!string.Equals(updatedFolder, changed.ProposedVirtualFolder, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The expected-result copy did not produce the planned folder for {original.Key}.");
            }
            else if (!string.Equals(updatedFolder, original.Value.Folder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unrelated LocalModData entry {original.Key} changed unexpectedly.");
            }
        }
    }
}
