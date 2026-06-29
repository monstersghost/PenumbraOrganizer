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
        var resultsByTarget = applyResult.Files.ToDictionary(file => file.TargetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var fileChange in plan.FileChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resultsByTarget.TryGetValue(fileChange.TargetPath, out var result))
            {
                errors.Add($"Expected write target was not processed: {fileChange.TargetPath}");
                continue;
            }

            if (result.Status == ApplyResultStatus.Failed)
                errors.Add($"Apply failed for {fileChange.TargetPath}: {result.Message}");

            if (fileChange.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson && result.WriteCompleted)
            {
                var sourceBytes = File.ReadAllBytes(fileChange.TargetPath);
                var liveHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes));
                if (!string.Equals(liveHash, fileChange.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"The applied organization.json hash does not match the planned hash for {fileChange.TargetPath}.");
            }
        }

        var databasePath = plan.FileChanges.SingleOrDefault(change => change.WriteTargetKind == PenumbraWriteTargetKind.ModDataDatabase)?.TargetPath;
        var organizationPath = plan.FileChanges.SingleOrDefault(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson)?.TargetPath;
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(organizationPath))
        {
            errors.Add("The post-Apply verification plan does not identify both authoritative Penumbra targets.");
            return Task.FromResult(new PostApplyVerificationResult(applyResult.OperationId, false, 0, 0, errors, warnings));
        }

        PenumbraModDataState modState;
        PenumbraOrganizationState organizationState;
        try
        {
            modState = PenumbraVirtualFolderWriter.LoadState(databasePath);
            organizationState = PenumbraOrganizationStore.LoadState(organizationPath);
        }
        catch (Exception ex)
        {
            errors.Add("The authoritative Penumbra state could not be reloaded after Apply: " + ex.Message);
            return Task.FromResult(new PostApplyVerificationResult(applyResult.OperationId, false, 0, 0, errors, warnings));
        }

        var plannedOrganizationChange = plan.FileChanges.Single(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson);
        PenumbraOrganizationState plannedOrganizationState;
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerVerify", Guid.NewGuid().ToString("N"), "organization.json");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            File.WriteAllBytes(tempPath, Convert.FromBase64String(plannedOrganizationChange.ExpectedBytesBase64));
            plannedOrganizationState = PenumbraOrganizationStore.LoadState(tempPath);
            File.Delete(tempPath);
            Directory.Delete(Path.GetDirectoryName(tempPath)!, recursive: true);
        }
        catch (Exception ex)
        {
            errors.Add("The planned organization.json payload is invalid: " + ex.Message);
            return Task.FromResult(new PostApplyVerificationResult(applyResult.OperationId, false, 0, 0, errors, warnings));
        }

        foreach (var folder in plannedOrganizationState.Folders.Keys)
        {
            if (!organizationState.Folders.ContainsKey(folder))
                errors.Add($"The required organization folder {folder} is missing after Apply.");
        }

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!modState.Entries.TryGetValue(entry.RecordKey, out var current))
            {
                errors.Add($"The authoritative entry for {entry.StableScanId} ({entry.RecordKey}) is missing after Apply.");
                continue;
            }

            var expectedFolder = entry.Protected ? entry.CurrentVirtualFolder : entry.ProposedVirtualFolder;
            if (!string.Equals(current.Folder, expectedFolder, StringComparison.Ordinal))
                errors.Add($"The applied folder for {entry.StableScanId} does not match the planned folder.");

            if (!entry.Protected &&
                !string.IsNullOrWhiteSpace(expectedFolder) &&
                !organizationState.Folders.ContainsKey(expectedFolder))
            {
                errors.Add($"The destination folder {expectedFolder} does not exist in organization.json for {entry.StableScanId}.");
            }
        }

        return Task.FromResult(new PostApplyVerificationResult(
            applyResult.OperationId,
            Succeeded: errors.Count == 0,
            VerifiedChangedModCount: plan.Entries.Count(entry =>
                entry.RequiresWrite &&
                modState.Entries.TryGetValue(entry.RecordKey, out var current) &&
                string.Equals(current.Folder, entry.ProposedVirtualFolder, StringComparison.Ordinal) &&
                organizationState.Folders.ContainsKey(entry.ProposedVirtualFolder)),
            VerifiedProtectedModCount: plan.Entries.Count(entry =>
                entry.Protected &&
                modState.Entries.TryGetValue(entry.RecordKey, out var current) &&
                string.Equals(current.Folder, entry.CurrentVirtualFolder, StringComparison.Ordinal)),
            Errors: errors,
            Warnings: warnings));
    }
}
