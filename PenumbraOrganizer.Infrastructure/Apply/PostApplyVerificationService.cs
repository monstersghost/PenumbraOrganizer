namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class PostApplyVerificationService : IPostApplyVerificationService
{
    public Task<PostApplyVerificationResult> VerifyAsync(
        DryRunPlan plan,
        ApplyResult applyResult,
        PenumbraInstallation? installation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var warnings = new List<string>();
        var appliedTargets = applyResult.Files.Where(file => file.WriteCompleted).Select(file => file.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fileChange in plan.FileChanges)
        {
            if (!appliedTargets.Contains(fileChange.TargetPath))
            {
                errors.Add($"Expected write target was not completed: {fileChange.TargetPath}");
                continue;
            }

            var sourceBytes = File.ReadAllBytes(fileChange.TargetPath);
            var liveHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes));
            if (!string.Equals(liveHash, fileChange.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"The applied hash does not match the planned hash for {fileChange.TargetPath}.");
        }

        // Folder verification only applies when this operation actually changed the
        // organization file (sort_order.json or mod_data.db). A metadata-only operation is fully
        // verified by the per-file hash checks above.
        var organizationChange = plan.FileChanges.FirstOrDefault(change =>
            change.WriteTargetKind is PenumbraWriteTargetKind.SortOrderJson or PenumbraWriteTargetKind.ModDataDb);
        if (organizationChange is null)
        {
            return Task.FromResult(new PostApplyVerificationResult(
                applyResult.OperationId, Succeeded: errors.Count == 0, 0, 0, errors, warnings));
        }

        Func<string, string>? currentFolderFor;
        try
        {
            currentFolderFor = ResolveCurrentFolderLookup(organizationChange.WriteTargetKind, organizationChange.TargetPath, installation, warnings);
        }
        catch (Exception ex)
        {
            errors.Add("The authoritative Penumbra organization file could not be reloaded after Apply: " + ex.Message);
            return Task.FromResult(new PostApplyVerificationResult(applyResult.OperationId, false, 0, 0, errors, warnings));
        }

        if (currentFolderFor is null)
        {
            // mod_data.db with no installation on hand (incomplete-operation recovery): the hash
            // checks above are the only verification possible here; a warning was already added.
            return Task.FromResult(new PostApplyVerificationResult(
                applyResult.OperationId, Succeeded: errors.Count == 0, 0, 0, errors, warnings));
        }

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentFolder = currentFolderFor(entry.StableScanId);

            if (entry.RequiresWrite)
            {
                if (!string.Equals(currentFolder, entry.ProposedVirtualFolder, StringComparison.Ordinal))
                    errors.Add($"The applied folder for {entry.StableScanId} does not match the planned folder.");
            }
            else if (entry.Protected)
            {
                if (!string.Equals(currentFolder, entry.CurrentVirtualFolder, StringComparison.Ordinal))
                    errors.Add($"Protected row {entry.StableScanId} changed unexpectedly.");
            }
            else if (entry.ValidationStatus == OrganizerRowStatus.Unchanged &&
                     !string.Equals(currentFolder, entry.CurrentVirtualFolder, StringComparison.Ordinal))
            {
                errors.Add($"Unrelated row {entry.StableScanId} changed unexpectedly.");
            }
        }

        return Task.FromResult(new PostApplyVerificationResult(
            applyResult.OperationId,
            Succeeded: errors.Count == 0,
            VerifiedChangedModCount: plan.Entries.Count(entry =>
                entry.RequiresWrite &&
                string.Equals(currentFolderFor(entry.StableScanId), entry.ProposedVirtualFolder, StringComparison.Ordinal)),
            VerifiedProtectedModCount: plan.Entries.Count(entry =>
                entry.Protected &&
                string.Equals(currentFolderFor(entry.StableScanId), entry.CurrentVirtualFolder, StringComparison.Ordinal)),
            Errors: errors,
            Warnings: warnings));
    }

    private static Func<string, string>? ResolveCurrentFolderLookup(
        PenumbraWriteTargetKind kind,
        string targetPath,
        PenumbraInstallation? installation,
        ICollection<string> warnings)
    {
        if (kind == PenumbraWriteTargetKind.SortOrderJson)
        {
            var state = PenumbraVirtualFolderWriter.LoadState(targetPath);
            return state.CurrentFolderFor;
        }

        if (installation is null)
        {
            warnings.Add(
                "Could not re-verify mod_data.db folders after Apply without a live installation " +
                "(this only happens during incomplete-operation recovery); relying on the hash checks above.");
            return null;
        }

        var dbState = ModDataDbVirtualFolderWriter.LoadState(installation);
        return dbState.CurrentFolderFor;
    }
}
