namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// Writes Penumbra's virtual-folder organization to <c>mod_data.db</c> (LiteDB) for installs where
/// that format — not <c>sort_order.json</c> — is authoritative. Unlike <c>sort_order.json</c>'s
/// combined "folder+display leaf" encoding, this format's <c>Folder</c> field is bare, so there is
/// no leaf-preservation or entry-removal logic to worry about: every installed mod already has a
/// <c>LocalModData</c> document, and moving one is just overwriting its <c>Folder</c> value.
/// </summary>
public sealed class ModDataDbVirtualFolderWriter : IPenumbraVirtualFolderWriter
{
    private const string SchemaFileName = "mod_data.db";

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
            if (!string.IsNullOrWhiteSpace(row.Message) && IsNoteworthyRowStatus(row.Status))
                warnings.Add(row.Message);

            var effectiveProtected = mod.Protected || proposal?.Protected == true;

            var currentFolder = state.CurrentFolderFor(mod.StableScanId);
            if (!string.Equals(currentFolder, row.CurrentVirtualFolder, StringComparison.Ordinal))
                warnings.Add("The authoritative mod_data.db folder no longer matches the scan snapshot.");

            // A mod can exist on disk (and scan successfully) while mod_data.db has no
            // LocalModData document for it at all -- Penumbra never registered it in its LiteDB
            // store. There is nothing to update in that case, so exclude it from the write plan
            // (same treatment as a protected mod) instead of failing the whole apply.
            var hasModDataDbDocument = state.Data.GetEntry(mod.StableScanId) is not null;
            if (!hasModDataDbDocument)
                warnings.Add("mod_data.db has no record for this mod yet. Open it in Penumbra once, then re-scan, before it can be reorganized here.");

            var folderChanged = row.Status == OrganizerRowStatus.ValidChange;
            var blockedByMissingDocument = folderChanged && !effectiveProtected && !hasModDataDbDocument;
            var requiresWrite = folderChanged && !effectiveProtected && hasModDataDbDocument;

            // A mod that would otherwise have a valid, writable change but can't be written
            // (missing mod_data.db document) is reported as needing review, not silently counted
            // as an applied change -- keeps the apply-readiness checklist and change counts honest
            // (DryRunPlanner's ChangedRowCount and MainViewModel's "All target records mapped"
            // checklist line both key off ValidationStatus == ValidChange).
            var entryStatus = blockedByMissingDocument ? OrganizerRowStatus.NeedsReview : row.Status;

            entries.Add(new DryRunPlanEntry(
                mod.StableScanId,
                mod.PhysicalDirectoryName,
                row.CurrentVirtualFolder,
                row.ProposedVirtualFolder,
                row.Source,
                effectiveProtected,
                entryStatus,
                $"{SchemaFileName}:{mod.StableScanId}",
                state.SourcePath,
                mod.StableScanId,
                state.SourceFile.Sha256,
                state.SourceFile.Sha256,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                requiresWrite,
                // No display-leaf concept for this format: ProposedVirtualFolder alone is what
                // gets written to the Folder field.
                string.Empty));
        }

        return Task.FromResult<IReadOnlyList<DryRunPlanEntry>>(entries);
    }

    // Unchanged/ValidChange/Protected messages ("No folder change.", "Ready for Review Changes.",
    // "Protected and unchanged.") are display-only Notes-column text, not warnings -- surfacing them
    // here would make an "Apply is blocked" dialog list normal, expected rows as if they were the
    // reason Apply is blocked.
    private static bool IsNoteworthyRowStatus(OrganizerRowStatus status)
        => status is not (OrganizerRowStatus.Unchanged or OrganizerRowStatus.ValidChange or OrganizerRowStatus.Protected);

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

        if (changedEntries.Length == 0)
            return Array.Empty<DryRunFileChange>();

        // The expected bytes are computed once, here, by mutating a scratch copy with the real,
        // dynamically-loaded LiteDB engine (never the live file) — the only safe way to guarantee
        // the result is something the exact installed LiteDB version can read back, without
        // reimplementing its binary format. Apply later just replays these exact bytes, the same
        // way it already does for sort_order.json.
        var assembly = LiteDbAssemblyLoader.TryLoad(installation)
            ?? throw new InvalidOperationException(
                "Penumbra's LiteDB engine (LiteDB.dll) could not be found next to the installed Penumbra plugin, so mod_data.db cannot be written.");

        var scratchPath = Path.Combine(Path.GetTempPath(), $"PenumbraOrganizer-mod_data-{Guid.NewGuid():N}.db");
        byte[] expectedBytes;
        try
        {
            File.Copy(state.SourcePath, scratchPath, overwrite: true);
            ApplyFolderUpdates(scratchPath, assembly, changedEntries);
            expectedBytes = await File.ReadAllBytesAsync(scratchPath, cancellationToken);
        }
        finally
        {
            if (File.Exists(scratchPath))
                File.Delete(scratchPath);
        }

        var expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes));

        return
        [
            new DryRunFileChange(
                state.SourcePath,
                PenumbraWriteTargetKind.ModDataDb,
                $"{SchemaFileName}:{PenumbraModDataDb.CollectionName}",
                state.SourceFile.Sha256,
                state.SourceFile.Length,
                expectedHash,
                expectedBytes.LongLength,
                Convert.ToBase64String(expectedBytes),
                changedEntries.Select(entry => entry.RecordKey).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                [$"{SchemaFileName}:Folder"],
                AtomicReplaceSupported: true,
                "Update Penumbra virtual-folder organization in mod_data.db.")
        ];
    }

    internal static PenumbraModDataDbState LoadState(PenumbraInstallation installation)
    {
        var configDirectory = installation.ConfigDirectory;
        var loadResult = PenumbraModDataDb.Load(configDirectory, installation);
        // Fail closed: the read-only scan path degrades gracefully when mod_data.db can't be read,
        // but building a write plan must not proceed on a format we can't reliably read back —
        // mirrors PenumbraVirtualFolderWriter.LoadState's existing throw for an unsupported
        // sort_order.json schema.
        if (loadResult.Status != PenumbraModDataDbLoadStatus.Success)
            throw new InvalidOperationException(loadResult.ErrorMessage ?? "mod_data.db could not be read.");

        var path = PenumbraModDataDb.GetPath(configDirectory);
        var bytes = File.ReadAllBytes(path);

        var fingerprintInput = new StringBuilder();
        foreach (var pair in loadResult.Data!.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            fingerprintInput.Append(pair.Key).Append('|').Append(pair.Value.Folder).AppendLine();

        var sourceFile = BuildSourceFileSnapshot(path, bytes, fingerprintInput.ToString());
        var schemaFingerprint = new SchemaFingerprint(SchemaFileName, sourceFile.SchemaFingerprint, SchemaDifferenceKind.None, Array.Empty<string>());

        return new PenumbraModDataDbState(path, sourceFile, schemaFingerprint, loadResult.Data);
    }

    private static DryRunSourceFileSnapshot BuildSourceFileSnapshot(string path, byte[] bytes, string fingerprintInput)
        => new(
            path,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))));

    private static void ApplyFolderUpdates(string scratchPath, Assembly liteDbAssembly, IReadOnlyList<DryRunPlanEntry> changedEntries)
    {
        var databaseType = liteDbAssembly.GetType("LiteDB.LiteDatabase")
            ?? throw new InvalidOperationException("LiteDB.dll did not expose the expected LiteDB.LiteDatabase type.");

        // See PenumbraModDataDb.ReadEntries for why Activator.CreateInstance can't be used directly
        // here: LiteDatabase's (string, BsonMapper? = null) constructor has two declared
        // parameters even though the second is optional at the C# call site.
        var constructor = databaseType.GetConstructors()
            .FirstOrDefault(candidate => candidate.GetParameters() is { Length: > 0 } parameters && parameters[0].ParameterType == typeof(string))
            ?? throw new InvalidOperationException("LiteDB.LiteDatabase does not expose a constructor accepting a connection string.");

        var constructorArgs = constructor.GetParameters()
            .Select((_, index) => index == 0 ? scratchPath : Type.Missing)
            .ToArray();
        dynamic database = constructor.Invoke(constructorArgs)
            ?? throw new InvalidOperationException("Could not construct LiteDB.LiteDatabase.");

        try
        {
            dynamic collection = database.GetCollection(PenumbraModDataDb.CollectionName);
            foreach (var entry in changedEntries)
            {
                var doc = collection.FindById(entry.RecordKey);
                if (doc is null)
                    throw new InvalidOperationException($"mod_data.db has no LocalModData document for {entry.RecordKey}.");

                doc["Folder"] = entry.ProposedVirtualFolder;
                bool updated = collection.Update(doc);
                if (!updated)
                    throw new InvalidOperationException($"mod_data.db rejected the folder update for {entry.RecordKey}.");

                // Self-verification within the same open connection, before this scratch copy ever
                // becomes the plan's expected bytes.
                var verifyDoc = collection.FindById(entry.RecordKey);
                var verifyFolder = string.Empty;
                if (verifyDoc is not null && verifyDoc!.ContainsKey("Folder"))
                    verifyFolder = (string)verifyDoc!["Folder"].AsString;
                if (!string.Equals(verifyFolder, entry.ProposedVirtualFolder, StringComparison.Ordinal))
                    throw new InvalidOperationException($"mod_data.db did not accept the planned folder for {entry.RecordKey}.");
            }
        }
        finally
        {
            ((IDisposable)database).Dispose();
        }
    }
}
