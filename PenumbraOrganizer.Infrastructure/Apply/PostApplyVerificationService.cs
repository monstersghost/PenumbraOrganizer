namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class PostApplyVerificationService : IPostApplyVerificationService
{
    public Task<PostApplyVerificationResult> VerifyAsync(
        DryRunPlan plan,
        ApplyResult applyResult,
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
        // organization file. A metadata-only operation is fully verified by the per-file hash
        // checks above.
        var sortOrderChange = plan.FileChanges.FirstOrDefault(change => change.WriteTargetKind == PenumbraWriteTargetKind.SortOrderJson);
        if (sortOrderChange is null)
        {
            return Task.FromResult(new PostApplyVerificationResult(
                applyResult.OperationId, Succeeded: errors.Count == 0, 0, 0, errors, warnings));
        }

        PenumbraModDataState state;
        try
        {
            state = PenumbraVirtualFolderWriter.LoadState(sortOrderChange.TargetPath);
        }
        catch (Exception ex)
        {
            errors.Add("The authoritative Penumbra organization file could not be reloaded after Apply: " + ex.Message);
            return Task.FromResult(new PostApplyVerificationResult(applyResult.OperationId, false, 0, 0, errors, warnings));
        }

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentFolder = state.CurrentFolderFor(entry.StableScanId);

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
                string.Equals(state.CurrentFolderFor(entry.StableScanId), entry.ProposedVirtualFolder, StringComparison.Ordinal)),
            VerifiedProtectedModCount: plan.Entries.Count(entry =>
                entry.Protected &&
                string.Equals(state.CurrentFolderFor(entry.StableScanId), entry.CurrentVirtualFolder, StringComparison.Ordinal)),
            Errors: errors,
            Warnings: warnings));
    }
}
