namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// Prunes orphaned entries from Penumbra's <c>mod_filesystem/organization.json</c>, independent of
/// which backend (<c>sort_order.json</c> or <c>mod_data.db</c>) is authoritative for mod
/// placement -- organization.json tracks Penumbra's own live folder-tree structure, not mod
/// placement, so it accumulates orphaned folders under either backend. See
/// docs/superpowers/specs/2026-07-09-organization-json-cleanup-design.md.
/// </summary>
/// <remarks>
/// Every entry point degrades to returning null rather than throwing or signalling a failure --
/// missing/malformed/unsupported-version organization.json, or an empty confirmed-prune
/// selection, are all "nothing to do" here and must never be treated as a reason to block the
/// primary sort_order.json/mod_data.db write path.
/// </remarks>
public sealed class OrganizationCleanupWriter : IOrganizationCleanupWriter
{
    private const string FileDisplayName = "organization.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public Task<DryRunSourceFileSnapshot?> CaptureSourceFileAsync(PenumbraInstallation installation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PenumbraOrganizationJson.GetPath(installation.ConfigDirectory);
        if (!File.Exists(path))
            return Task.FromResult<DryRunSourceFileSnapshot?>(null);

        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return Task.FromResult<DryRunSourceFileSnapshot?>(new DryRunSourceFileSnapshot(path, bytes.LongLength, hash, hash));
    }

    public Task<DryRunFileChange?> BuildFileChangeAsync(
        PenumbraInstallation installation,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (proposalSnapshot.OrganizationCleanupSelections is not { Count: > 0 } selections)
            return Task.FromResult<DryRunFileChange?>(null);

        var path = PenumbraOrganizationJson.GetPath(installation.ConfigDirectory);
        var loadResult = PenumbraOrganizationJson.Load(installation.ConfigDirectory);
        if (loadResult.Status != PenumbraOrganizationJsonLoadStatus.Success)
            return Task.FromResult<DryRunFileChange?>(null);

        var organizationJson = loadResult.Data!;
        var candidatePaths = OrganizationCleanupAnalyzer.FindCandidates(organizationJson, proposalSnapshot.Proposals)
            .Select(candidate => candidate.Path)
            .ToHashSet(StringComparer.Ordinal);

        var confirmedPrune = selections
            .Where(candidatePaths.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(selection => selection, StringComparer.Ordinal)
            .ToArray();

        if (confirmedPrune.Length == 0)
            return Task.FromResult<DryRunFileChange?>(null);

        var sourceBytes = File.ReadAllBytes(path);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"{FileDisplayName} could not be parsed for editing.");
        if (root["Folders"] is not JsonObject foldersNode)
            throw new InvalidOperationException($"{FileDisplayName} is missing its Folders object.");

        foreach (var folderPath in confirmedPrune)
            foldersNode.Remove(folderPath);

        var updatedJson = root.ToJsonString(SerializerOptions);
        ValidateUpdatedOrganizationJson(updatedJson, organizationJson, confirmedPrune);

        var expectedBytes = Encoding.UTF8.GetBytes(updatedJson);
        var expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes));

        var fileChange = new DryRunFileChange(
            path,
            PenumbraWriteTargetKind.OrganizationJson,
            $"{FileDisplayName}:Folders",
            sourceHash,
            sourceBytes.LongLength,
            expectedHash,
            expectedBytes.LongLength,
            Convert.ToBase64String(expectedBytes),
            confirmedPrune,
            [$"{FileDisplayName}:Folders"],
            AtomicReplaceSupported: true,
            $"Remove {confirmedPrune.Length} orphaned folder(s) from organization.json.");

        return Task.FromResult<DryRunFileChange?>(fileChange);
    }

    // Confirms the pruned paths are truly gone and every other folder entry -- plus the entire
    // Separators object, which this writer never touches -- round-trips with identical values, so
    // an unrelated folder's color/sort-mode/separator flag can never be silently disturbed.
    private static void ValidateUpdatedOrganizationJson(
        string updatedJson,
        PenumbraOrganizationJson original,
        IReadOnlyList<string> prunedPaths)
    {
        var updatedResult = PenumbraOrganizationJson.Parse(updatedJson);
        if (updatedResult.Status != PenumbraOrganizationJsonLoadStatus.Success)
            throw new InvalidOperationException($"The expected-result {FileDisplayName} failed to re-parse after pruning.");

        var updated = updatedResult.Data!;
        var prunedLookup = prunedPaths.ToHashSet(StringComparer.Ordinal);

        foreach (var prunedPath in prunedPaths)
        {
            if (updated.Folders.ContainsKey(prunedPath))
                throw new InvalidOperationException($"The expected-result {FileDisplayName} still contains pruned folder {prunedPath}.");
        }

        foreach (var (folderPath, entry) in original.Folders)
        {
            if (prunedLookup.Contains(folderPath))
                continue;

            if (!updated.Folders.TryGetValue(folderPath, out var updatedEntry) || updatedEntry != entry)
                throw new InvalidOperationException($"Unrelated organization.json folder entry {folderPath} changed unexpectedly.");
        }

        if (updated.Folders.Count != original.Folders.Count - prunedPaths.Count)
            throw new InvalidOperationException($"The expected-result {FileDisplayName} folder count does not match the planned prune.");

        if (!updated.SeparatorPaths.OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual(original.SeparatorPaths.OrderBy(p => p, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The expected-result {FileDisplayName} Separators changed unexpectedly.");
        }
    }
}
