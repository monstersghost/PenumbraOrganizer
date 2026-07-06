namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// Routes each <see cref="IPenumbraVirtualFolderWriter"/> call to whichever concrete writer
/// matches this installation's authoritative format (<see cref="PenumbraOrganizationBackendSelector"/>).
/// Everything downstream (<c>DryRunPlanner</c>, <c>PlanInvalidationService</c>, ...) depends only on
/// the interface, so this is the single place that needs to know two formats exist.
/// </summary>
public sealed class PenumbraOrganizationWriter : IPenumbraVirtualFolderWriter
{
    private readonly PenumbraVirtualFolderWriter _sortOrderWriter = new();
    private readonly ModDataDbVirtualFolderWriter _modDataDbWriter = new();

    public Task<IReadOnlyList<DryRunSourceFileSnapshot>> CaptureSourceFilesAsync(PenumbraInstallation installation, CancellationToken cancellationToken)
        => Resolve(installation).CaptureSourceFilesAsync(installation, cancellationToken);

    public Task<IReadOnlyList<SchemaFingerprint>> CaptureSchemaFingerprintsAsync(PenumbraInstallation installation, CancellationToken cancellationToken)
        => Resolve(installation).CaptureSchemaFingerprintsAsync(installation, cancellationToken);

    public Task<IReadOnlyList<DryRunPlanEntry>> MapPlanEntriesAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
        => Resolve(installation).MapPlanEntriesAsync(installation, inventory, proposalSnapshot, cancellationToken);

    public Task<IReadOnlyList<DryRunFileChange>> BuildExpectedFileChangesAsync(
        PenumbraInstallation installation,
        IReadOnlyList<DryRunPlanEntry> planEntries,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
        => Resolve(installation).BuildExpectedFileChangesAsync(installation, planEntries, proposalSnapshot, cancellationToken);

    private IPenumbraVirtualFolderWriter Resolve(PenumbraInstallation installation)
        => PenumbraOrganizationBackendSelector.Detect(installation.ConfigDirectory) switch
        {
            PenumbraOrganizationBackend.ModDataDb => _modDataDbWriter,
            _ => _sortOrderWriter,
        };
}
