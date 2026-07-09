namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class PlanInvalidationService : IPlanInvalidationService
{
    private readonly IPenumbraVirtualFolderWriter _writer;
    private readonly IOrganizationCleanupWriter? _organizationCleanupWriter;

    public PlanInvalidationService(IPenumbraVirtualFolderWriter writer, IOrganizationCleanupWriter? organizationCleanupWriter = null)
    {
        _writer = writer;
        _organizationCleanupWriter = organizationCleanupWriter;
    }

    public async Task<IReadOnlyList<PlanInvalidationReason>> GetInvalidationReasonsAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        var reasons = new HashSet<PlanInvalidationReason>();
        cancellationToken.ThrowIfCancellationRequested();

        var currentInstalledVersion = PenumbraInstalledVersionReader.Read(installation) ?? installation.InstalledVersion ?? "Unknown";
        if (!string.Equals(currentInstalledVersion, plan.InstalledPenumbraVersion, StringComparison.Ordinal))
            reasons.Add(PlanInvalidationReason.PenumbraVersionChanged);

        var currentInstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(installation);
        if (!string.Equals(currentInstallationIdentity, plan.InstallationIdentity, StringComparison.Ordinal))
            reasons.Add(PlanInvalidationReason.ModLibraryIdentityChanged);

        if (!string.Equals(OrganizerSessionService.BuildScanIdentity(inventory), plan.ScanIdentity, StringComparison.Ordinal))
            reasons.Add(PlanInvalidationReason.NewScanCompleted);

        if (!string.Equals(proposalSnapshot.OrganizationSessionIdentity, plan.OrganizationSessionIdentity, StringComparison.Ordinal))
            reasons.Add(PlanInvalidationReason.SessionChanged);

        if (!string.Equals(proposalSnapshot.SnapshotIdentity, plan.ProposalSnapshotIdentity, StringComparison.Ordinal))
            reasons.Add(PlanInvalidationReason.ProposalChanged);

        if (!EquivalentPreferences(proposalSnapshot.OrganizationPreferences, plan.OrganizationPreferences))
            reasons.Add(PlanInvalidationReason.OrganizationStrategyChanged);

        var proposalById = proposalSnapshot.Proposals.ToDictionary(proposal => proposal.StableScanId, StringComparer.Ordinal);
        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!proposalById.TryGetValue(entry.StableScanId, out var proposal))
            {
                reasons.Add(PlanInvalidationReason.TargetModMissing);
                continue;
            }

            if (proposal.Protected != entry.Protected)
                reasons.Add(PlanInvalidationReason.ProtectionChanged);

            if (!string.Equals(proposal.ProposedVirtualFolder, entry.ProposedVirtualFolder, StringComparison.Ordinal) ||
                proposal.Source != entry.ProposalSource)
            {
                reasons.Add(PlanInvalidationReason.ProposalChanged);
            }
        }

        var inventoryById = inventory.Mods.ToDictionary(mod => mod.StableScanId, StringComparer.Ordinal);
        foreach (var entry in plan.Entries.Where(entry => entry.RequiresWrite || entry.ValidationStatus != OrganizerRowStatus.Unchanged))
        {
            if (!inventoryById.TryGetValue(entry.StableScanId, out var mod))
            {
                reasons.Add(PlanInvalidationReason.TargetModMissing);
                continue;
            }

            if (!string.Equals(mod.CurrentVirtualFolder, entry.CurrentVirtualFolder, StringComparison.Ordinal))
                reasons.Add(PlanInvalidationReason.CurrentFolderChanged);
        }

        var currentSourceFiles = await _writer.CaptureSourceFilesAsync(installation, cancellationToken);
        if (!EquivalentSourceFiles(plan.SourceFiles, currentSourceFiles))
            reasons.Add(PlanInvalidationReason.SourceFileHashChanged);

        if (_organizationCleanupWriter is not null)
        {
            try
            {
                var currentOrganizationSourceFile = await _organizationCleanupWriter.CaptureSourceFileAsync(installation, cancellationToken);
                if (!EquivalentOptionalSourceFile(plan.OrganizationCleanupSourceFile, currentOrganizationSourceFile))
                    reasons.Add(PlanInvalidationReason.SourceFileHashChanged);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // organization.json cleanup is a bonus, independent write target -- a race against
                // Penumbra rewriting the file mid-read (or any other internal failure) must never
                // block staleness detection for the primary sort_order.json/mod_data.db plan.
                // Treat the current source file as unchanged from the plan's recorded state rather
                // than reporting a spurious invalidation reason or letting the exception propagate.
            }
        }

        try
        {
            var currentSchemaFingerprints = await _writer.CaptureSchemaFingerprintsAsync(installation, cancellationToken);
            if (currentSchemaFingerprints.Any(fingerprint => fingerprint.DifferenceKind != SchemaDifferenceKind.None))
                reasons.Add(PlanInvalidationReason.UnsupportedSchema);
            if (!EquivalentSchemaFingerprints(plan.SourceSchemaFingerprints, currentSchemaFingerprints))
                reasons.Add(PlanInvalidationReason.SchemaFingerprintChanged);
        }
        catch (InvalidOperationException)
        {
            reasons.Add(PlanInvalidationReason.UnsupportedSchema);
        }

        return reasons.ToArray();
    }

    private static bool EquivalentPreferences(OrganizationPreferences left, OrganizationPreferences right)
    {
        return left.Strategy == right.Strategy
            && left.UseTypeFolders == right.UseTypeFolders
            && left.UseCreatorFolders == right.UseCreatorFolders
            && string.Equals(left.FixedRootFolder ?? string.Empty, right.FixedRootFolder ?? string.Empty, StringComparison.Ordinal)
            && left.PreserveMeaningfulExistingFolders == right.PreserveMeaningfulExistingFolders
            && left.FlattenTemporarySourceFolders == right.FlattenTemporarySourceFolders
            && left.NormalizeCreatorAliases == right.NormalizeCreatorAliases
            && left.UnknownCreatorBehavior == right.UnknownCreatorBehavior
            && left.UnknownTypeBehavior == right.UnknownTypeBehavior
            && left.UncertainClassificationBehavior == right.UncertainClassificationBehavior
            && left.PreserveCurrentFolderWhenUncertain == right.PreserveCurrentFolderWhenUncertain
            && string.Equals(left.CustomPattern ?? string.Empty, right.CustomPattern ?? string.Empty, StringComparison.Ordinal)
            && left.FolderOrder.SequenceEqual(right.FolderOrder);
    }

    private static bool EquivalentSourceFiles(
        IReadOnlyList<DryRunSourceFileSnapshot> expected,
        IReadOnlyList<DryRunSourceFileSnapshot> current)
    {
        var expectedByPath = expected.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var currentByPath = current.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        if (expectedByPath.Count != currentByPath.Count)
            return false;

        foreach (var pair in expectedByPath)
        {
            if (!currentByPath.TryGetValue(pair.Key, out var currentFile))
                return false;

            if (pair.Value.Length != currentFile.Length ||
                !string.Equals(pair.Value.Sha256, currentFile.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pair.Value.SchemaFingerprint, currentFile.SchemaFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EquivalentOptionalSourceFile(DryRunSourceFileSnapshot? expected, DryRunSourceFileSnapshot? current)
    {
        if (expected is null && current is null)
            return true;
        if (expected is null || current is null)
            return false;

        return expected.Length == current.Length && string.Equals(expected.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EquivalentSchemaFingerprints(
        IReadOnlyList<SchemaFingerprint> expected,
        IReadOnlyList<SchemaFingerprint> current)
    {
        var expectedByName = expected.ToDictionary(fingerprint => fingerprint.FileName, StringComparer.OrdinalIgnoreCase);
        var currentByName = current.ToDictionary(fingerprint => fingerprint.FileName, StringComparer.OrdinalIgnoreCase);
        if (expectedByName.Count != currentByName.Count)
            return false;

        foreach (var pair in expectedByName)
        {
            if (!currentByName.TryGetValue(pair.Key, out var currentFingerprint))
                return false;

            if (!string.Equals(pair.Value.Fingerprint, currentFingerprint.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.DifferenceKind != currentFingerprint.DifferenceKind)
            {
                return false;
            }
        }

        return true;
    }
}
