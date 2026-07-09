namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class DryRunPlanner : IDryRunPlanner
{
    private readonly IPenumbraVirtualFolderWriter _writer;
    private readonly IDryRunValidationService _validationService;
    private readonly IOrganizationCleanupWriter? _organizationCleanupWriter;

    public DryRunPlanner(
        IPenumbraVirtualFolderWriter writer,
        IDryRunValidationService validationService,
        IOrganizationCleanupWriter? organizationCleanupWriter = null)
    {
        _writer = writer;
        _validationService = validationService;
        _organizationCleanupWriter = organizationCleanupWriter;
    }

    public async Task<DryRunPlan> CreatePlanAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installedVersion = PenumbraInstalledVersionReader.Read(installation) ?? installation.InstalledVersion ?? "Unknown";
        if (!string.Equals(installedVersion, inventory.Installation.InstalledVersion ?? "Unknown", StringComparison.Ordinal))
            throw new InvalidOperationException("The installed Penumbra version changed since scan. Run a new scan before creating a dry run.");

        var sourceFiles = await _writer.CaptureSourceFilesAsync(installation, cancellationToken);
        var schemaFingerprints = await _writer.CaptureSchemaFingerprintsAsync(installation, cancellationToken);
        if (schemaFingerprints.Any(fingerprint => fingerprint.DifferenceKind != SchemaDifferenceKind.None))
            throw new InvalidOperationException("The authoritative Penumbra schema is unsupported for virtual-folder Apply.");

        var entries = await _writer.MapPlanEntriesAsync(installation, inventory, proposalSnapshot, cancellationToken);
        var fileChanges = (await _writer.BuildExpectedFileChangesAsync(installation, entries, proposalSnapshot, cancellationToken)).ToList();

        // organization.json cleanup is a second, independent write target. Its own source file
        // and schema state deliberately never enter `sourceFiles`/`schemaFingerprints` above --
        // those feed the blocking schema-mismatch throw a few lines up, which exists for the
        // primary mod-placement writer only. A missing/malformed/unsupported-version
        // organization.json must never block sort_order.json/mod_data.db writes.
        DryRunSourceFileSnapshot? organizationCleanupSourceFile = null;
        if (_organizationCleanupWriter is not null)
        {
            try
            {
                organizationCleanupSourceFile = await _organizationCleanupWriter.CaptureSourceFileAsync(installation, cancellationToken);
                var organizationFileChange = await _organizationCleanupWriter.BuildFileChangeAsync(installation, proposalSnapshot, cancellationToken);
                if (organizationFileChange is not null)
                    fileChanges.Add(organizationFileChange);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // organization.json cleanup is a bonus, independent write target -- any failure here
                // (a race against Penumbra rewriting the file mid-read, an internal validation
                // failure) must never block the primary sort_order.json/mod_data.db plan from
                // being built. Degrade to "no cleanup this run", same as the already-handled
                // missing/malformed/unsupported-version cases.
                organizationCleanupSourceFile = null;
            }
        }

        var warnings = new List<string>();
        warnings.AddRange(proposalSnapshot.ValidationResult.Warnings.Select(warning => warning.Message));
        warnings.AddRange(entries.SelectMany(entry => entry.Warnings));

        var summary = new DryRunSummary(
            ProtectedRowCount: entries.Count(entry => entry.Protected),
            ChangedRowCount: entries.Count(entry => entry.ValidationStatus == OrganizerRowStatus.ValidChange),
            UnchangedRowCount: entries.Count(entry => entry.ValidationStatus == OrganizerRowStatus.Unchanged),
            InvalidRowCount: entries.Count(entry => entry.ValidationStatus is OrganizerRowStatus.InvalidPath or OrganizerRowStatus.BlockedProtected or OrganizerRowStatus.MissingMod or OrganizerRowStatus.StaleScan),
            AffectedModCount: entries.Count(entry => entry.RequiresWrite),
            WriteOperationCount: fileChanges.Count);

        var provisionalPlan = new DryRunPlan(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            typeof(DryRunPlanner).Assembly.GetName().Version?.ToString() ?? "dev",
            installedVersion,
            OrganizerSessionService.BuildScanIdentity(inventory),
            OrganizerSessionService.BuildInstallationIdentity(installation),
            proposalSnapshot.OrganizationSessionIdentity,
            proposalSnapshot.SnapshotIdentity,
            proposalSnapshot.OrganizationPreferences,
            schemaFingerprints,
            sourceFiles,
            entries,
            fileChanges,
            new DryRunValidationResult(DryRunPlanValidationStatus.Valid, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<PlanInvalidationReason>()),
            summary,
            ApplyPermitted: false,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            organizationCleanupSourceFile);

        var validation = await _validationService.ValidateAsync(provisionalPlan, installation, inventory, proposalSnapshot, cancellationToken);
        return provisionalPlan with
        {
            Validation = validation,
            ApplyPermitted = validation.ApplyPermitted,
            Warnings = provisionalPlan.Warnings.Concat(validation.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }
}
