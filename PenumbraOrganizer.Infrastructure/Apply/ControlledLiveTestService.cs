namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using System.Text;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

public sealed class ControlledLiveTestService : IControlledLiveTestService
{
    private readonly IOrganizerProposalValidationService _validationService;

    public ControlledLiveTestService(IOrganizerProposalValidationService validationService)
    {
        _validationService = validationService;
    }

    public Task<ControlledTestSetup> BuildSetupAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        ControlledTestOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedFolder = VirtualFolderPath.Normalize(options.TestFolderName);
        var errors = new List<string>();
        if (!VirtualFolderPath.IsValid(normalizedFolder, out var folderError))
            errors.Add(folderError);

        var state = PenumbraVirtualFolderWriter.LoadState(installation);
        var proposalById = proposalSnapshot.Proposals.ToDictionary(proposal => proposal.StableScanId, StringComparer.Ordinal);
        var candidates = inventory.Mods
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.StableScanId, StringComparer.Ordinal)
            .Select(mod =>
            {
                proposalById.TryGetValue(mod.StableScanId, out var proposal);
                var proposedFolder = string.IsNullOrWhiteSpace(normalizedFolder) ? options.TestFolderName : normalizedFolder;
                var status = GetStatus(mod, proposal);
                return new ControlledTestCandidate(
                    mod.StableScanId,
                    mod.Name,
                    mod.CurrentVirtualFolder,
                    proposal?.ProposedVirtualFolder ?? mod.CurrentVirtualFolder,
                    proposedFolder,
                    mod.PhysicalDirectory,
                    state.SourcePath,
                    mod.StableScanId,
                    status.Status,
                    status.Message,
                    status.Status == ControlledTestCandidateStatus.Eligible);
            })
            .ToArray();

        var warnings = candidates.Count(candidate => candidate.Status == ControlledTestCandidateStatus.Ambiguous) > 0
            ? ["Ambiguous mods stay excluded from the controlled live test until you choose clearer candidates."]
            : Array.Empty<string>();

        return Task.FromResult(new ControlledTestSetup(
            new ControlledTestOptions(normalizedFolder, options.MaximumSelectedModCount),
            candidates,
            errors,
            warnings));
    }

    public ProposalSnapshot BuildControlledSnapshot(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        ControlledTestRequest request)
    {
        var normalizedFolder = VirtualFolderPath.Normalize(request.TestFolderName);
        if (!VirtualFolderPath.IsValid(normalizedFolder, out var folderError))
            throw new InvalidOperationException(folderError);

        var selectedIds = request.StableScanIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0)
            throw new InvalidOperationException("Select at least one mod for the controlled live test.");
        if (selectedIds.Length > request.MaximumSelectedModCount)
            throw new InvalidOperationException($"Controlled Test Apply is limited to {request.MaximumSelectedModCount} mods.");

        var setup = BuildSetupAsync(
            installation,
            inventory,
            proposalSnapshot,
            new ControlledTestOptions(normalizedFolder, request.MaximumSelectedModCount),
            CancellationToken.None).GetAwaiter().GetResult();
        var candidatesById = setup.Candidates.ToDictionary(candidate => candidate.StableScanId, StringComparer.Ordinal);
        foreach (var selectedId in selectedIds)
        {
            if (!candidatesById.TryGetValue(selectedId, out var candidate))
                throw new InvalidOperationException($"The controlled test candidate {selectedId} is no longer available.");
            if (!candidate.CanSelect)
                throw new InvalidOperationException($"{candidate.ModName} cannot be selected for Controlled Test Apply: {candidate.StatusMessage}");
        }

        var proposals = proposalSnapshot.Proposals
            .OrderBy(proposal => proposal.StableScanId, StringComparer.Ordinal)
            .Select(proposal =>
            {
                var selected = selectedIds.Contains(proposal.StableScanId, StringComparer.Ordinal);
                return new OrganizerModProposal
                {
                    StableScanId = proposal.StableScanId,
                    Name = proposal.Name,
                    CurrentVirtualFolder = proposal.CurrentVirtualFolder,
                    ProposedVirtualFolder = selected ? normalizedFolder : proposal.CurrentVirtualFolder,
                    OriginalCreator = proposal.OriginalCreator,
                    OrganizerCreatorLabel = proposal.OrganizerCreatorLabel,
                    OrganizerTypeLabel = proposal.OrganizerTypeLabel,
                    Protected = proposal.Protected,
                    OriginalProtected = proposal.OriginalProtected,
                    Source = selected ? OrganizerProposalSource.Manual : OrganizerProposalSource.PreservedCurrent,
                    NeedsReview = false,
                };
            })
            .ToArray();

        if (!proposals.Any(proposal =>
                selectedIds.Contains(proposal.StableScanId, StringComparer.Ordinal) &&
                !string.Equals(proposal.CurrentVirtualFolder, proposal.ProposedVirtualFolder, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("At least one selected mod must move to a different Penumbra folder for a controlled live test.");
        }

        // Preserve the base snapshot's folder set (which includes existing empty folders) and add
        // the controlled test folder. Rebuilding from proposals alone would drop empty folders,
        // which the authoritative writer would then delete from sort_order.json.
        var folders = proposalSnapshot.Folders
            .Concat(proposals
                .Select(proposal => proposal.ProposedVirtualFolder)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => new OrganizerFolder(folder, ManuallyCreated: true, Protected: false)))
            .GroupBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var validation = _validationService.Validate(inventory, proposals, folders, proposalSnapshot.OrganizationPreferences);

        return new ProposalSnapshot(
            BuildControlledSnapshotIdentity(proposalSnapshot.OrganizationSessionIdentity, normalizedFolder, selectedIds),
            BuildControlledSessionIdentity(proposalSnapshot.OrganizationSessionIdentity, normalizedFolder, selectedIds),
            proposalSnapshot.OrganizationPreferences,
            proposals,
            folders,
            validation);
    }

    private static (ControlledTestCandidateStatus Status, string Message) GetStatus(
        ModScanResult mod,
        OrganizerModProposal? proposal)
    {
        if (mod.Protected || proposal?.Protected == true || proposal?.OriginalProtected == true)
            return (ControlledTestCandidateStatus.Protected, "Protected mods stay in their current Penumbra folder.");

        if (mod.Warnings.Any(warning =>
                warning.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) ||
                warning.Contains("same display name", StringComparison.OrdinalIgnoreCase) ||
                warning.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
        {
            return (ControlledTestCandidateStatus.Ambiguous, "This mod has ambiguous identity signals in the current scan.");
        }

        // Under the sort_order.json model every installed mod is organizable (a mod with no
        // entry simply lives at the root), so there is no "missing record" exclusion.
        return (ControlledTestCandidateStatus.Eligible, "Ready for a controlled live test.");
    }

    private static string BuildControlledSnapshotIdentity(
        string baseIdentity,
        string testFolderName,
        IReadOnlyList<string> selectedIds)
        => baseIdentity + ":controlled:" + Hash($"{testFolderName}|{string.Join("|", selectedIds.OrderBy(id => id, StringComparer.Ordinal))}");

    private static string BuildControlledSessionIdentity(
        string baseIdentity,
        string testFolderName,
        IReadOnlyList<string> selectedIds)
        => baseIdentity + ":controlled-session:" + Hash($"{testFolderName}|{string.Join("|", selectedIds.OrderBy(id => id, StringComparer.Ordinal))}");

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
