namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class RealInstallationValidationService : IRealInstallationValidationService
{
    private readonly IPenumbraScanService _scanService;
    private readonly IDryRunPlanner _dryRunPlanner;
    private readonly IWritePermissionPreflightService _preflightService;
    private readonly IApplyService _applyService;

    public RealInstallationValidationService(
        IPenumbraScanService scanService,
        IDryRunPlanner dryRunPlanner,
        IWritePermissionPreflightService preflightService,
        IApplyService applyService)
    {
        _scanService = scanService;
        _dryRunPlanner = dryRunPlanner;
        _preflightService = preflightService;
        _applyService = applyService;
    }

    public async Task<RealInstallationValidationResult> ValidateAsync(
        PenumbraInstallation installation,
        ProposalSnapshot? proposalSnapshot,
        RealInstallationValidationOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Authorized)
            throw new InvalidOperationException("Real-installation validation requires explicit user authorization.");

        var inventory = await _scanService.ScanAsync(installation, null, cancellationToken);
        var effectiveSnapshot = proposalSnapshot ?? BuildPreservedSnapshot(inventory);
        var plan = await _dryRunPlanner.CreatePlanAsync(installation, inventory, effectiveSnapshot, cancellationToken);
        var preflight = await _preflightService.CheckAsync(plan, cancellationToken);

        Guid? backupOperationId = null;
        if (options.CreateVerifiedBackup && plan.ApplyPermitted)
        {
            var applyOperation = await _applyService.PrepareAsync(plan, installation, effectiveSnapshot, cancellationToken);
            backupOperationId = applyOperation.OperationId;
        }

        var records = plan.Entries
            .OrderBy(entry => entry.StableScanId, StringComparer.Ordinal)
            .Select(entry => new ValidationMappedRecord(
                entry.StableScanId,
                inventory.Mods.FirstOrDefault(mod => mod.StableScanId == entry.StableScanId)?.Name ?? entry.PhysicalModIdentity,
                entry.CurrentVirtualFolder,
                entry.ProposedVirtualFolder,
                inventory.Mods.FirstOrDefault(mod => mod.StableScanId == entry.StableScanId)?.PhysicalDirectory ?? string.Empty,
                entry.TargetPath,
                entry.RecordKey,
                entry.Protected,
                entry.ValidationStatus,
                entry.RequiresWrite,
                entry.Warnings))
            .ToArray();

        var errors = plan.Validation.Errors.Concat(preflight.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var warnings = plan.Warnings.Concat(plan.Validation.Warnings).Concat(preflight.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingRecords = records.Count(record => record.ValidationStatus == OrganizerRowStatus.MissingMod);
        var ambiguousRecords = records.Count(record => record.Warnings.Any(warning =>
            warning.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)));
        var unsupportedRecords = records.Count(record => record.ValidationStatus is OrganizerRowStatus.MissingMod or OrganizerRowStatus.InvalidPath or OrganizerRowStatus.StaleScan);
        var unsupportedStructures = plan.SourceSchemaFingerprints.Count(fp => fp.DifferenceKind != SchemaDifferenceKind.None);
        var backupReadiness = options.CreateVerifiedBackup && backupOperationId.HasValue
            ? "Verified backup prepared."
            : plan.ApplyPermitted
                ? "Ready to prepare a verified backup."
                : "Blocked until the dry run is valid.";
        var rollbackReadiness = options.CreateVerifiedBackup && backupOperationId.HasValue
            ? "Rollback transaction prepared."
            : "Rollback will be prepared only after a verified backup is created.";
        var gameState = preflight.BlockingProcesses.Count == 0
            ? "FFXIV, XIVLauncher, and related processes appear closed."
            : $"Blocked by running processes: {string.Join(", ", preflight.BlockingProcesses)}";
        var writableTargetStatus = preflight.Succeeded
            ? "The authoritative write target is writable."
            : "The authoritative write target is blocked.";
        var safeForApply = plan.ApplyPermitted &&
                           preflight.Succeeded &&
                           missingRecords == 0 &&
                           ambiguousRecords == 0 &&
                           unsupportedRecords == 0 &&
                           options.CreateVerifiedBackup &&
                           backupOperationId.HasValue &&
                           preflight.BlockingProcesses.Count == 0;
        var report = new RealInstallationValidationReport(
            installation.ConfigDirectory,
            installation.ModRoot,
            installation.InstalledVersion ?? "Unknown",
            inventory.Mods.Count,
            plan.Summary.AffectedModCount,
            records.Count(record => !string.IsNullOrWhiteSpace(record.RecordKey)),
            missingRecords,
            ambiguousRecords,
            inventory.Mods.Count(mod => mod.Protected),
            unsupportedRecords,
            unsupportedStructures,
            writableTargetStatus,
            gameState,
            backupReadiness,
            rollbackReadiness,
            safeForApply);
        var summary =
            $"Penumbra state directory: {report.PenumbraStateDirectory}. " +
            $"Mod-library root: {report.ModLibraryRoot}. " +
            $"Installed Penumbra version: {report.InstalledPenumbraVersion}. " +
            $"Mods scanned: {report.ModsScanned}. " +
            $"Proposed changes: {report.ProposedChanges}. " +
            $"Records mapped: {report.MappedRecords}. " +
            $"Missing records: {missingRecords}. " +
            $"Ambiguous records: {ambiguousRecords}. " +
            $"Protected mods: {report.ProtectedMods}. " +
            $"Unsupported records: {unsupportedRecords}. " +
            $"Unsupported structures: {unsupportedStructures}. " +
            $"Writable target: {report.WritableTargetStatus}. " +
            $"Game state: {report.GameOrLauncherStatus}. " +
            $"Backup readiness: {report.BackupReadiness}. " +
            $"Rollback readiness: {report.RollbackReadiness}. " +
            $"Safe for Apply: {(safeForApply ? "Yes" : "No")}.";

        return new RealInstallationValidationResult(
            installation,
            inventory,
            effectiveSnapshot,
            plan,
            preflight,
            records,
            report,
            errors,
            warnings,
            options.CreateVerifiedBackup && backupOperationId.HasValue,
            backupOperationId,
            summary,
            safeForApply);
    }

    private static ProposalSnapshot BuildPreservedSnapshot(ScanInventory inventory)
    {
        var preferences = OrganizationPreferences.DefaultManual;
        var proposals = inventory.Mods
            .OrderBy(mod => mod.StableScanId, StringComparer.Ordinal)
            .Select(mod => new OrganizerModProposal
            {
                StableScanId = mod.StableScanId,
                Name = mod.Name,
                CurrentVirtualFolder = mod.CurrentVirtualFolder,
                ProposedVirtualFolder = mod.CurrentVirtualFolder,
                OriginalCreator = mod.Author,
                OrganizerCreatorLabel = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author,
                OrganizerTypeLabel = "Unknown type",
                Protected = mod.Protected,
                OriginalProtected = mod.Protected,
                Source = OrganizerProposalSource.PreservedCurrent,
                NeedsReview = false,
            })
            .ToArray();

        var validationRows = proposals
            .Select(proposal => new OrganizerValidationRow(
                proposal.StableScanId,
                proposal.Name,
                proposal.CurrentVirtualFolder,
                proposal.ProposedVirtualFolder,
                proposal.Source,
                OrganizerRowStatus.Unchanged,
                "Preserved current Penumbra folder."))
            .ToArray();

        var validation = new OrganizerValidationResult
        {
            Errors = Array.Empty<OrganizerValidationIssue>(),
            Warnings = Array.Empty<OrganizerValidationIssue>(),
            Rows = validationRows,
            Summary = new OrganizerValidationSummary(
                proposals.Length,
                Changed: 0,
                Unchanged: proposals.Length,
                Protected: proposals.Count(proposal => proposal.Protected),
                NeedsReview: 0,
                Invalid: 0,
                Warnings: 0),
        };

        var folders = proposals
            .Select(proposal => proposal.CurrentVirtualFolder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(folder => new OrganizerFolder(folder, false, false))
            .ToArray();
        var session = new OrganizerSessionDocument
        {
            ScanIdentity = OrganizerSessionService.BuildScanIdentity(inventory),
            ScanTimestampUtc = inventory.ScannedAtUtc,
            InstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(inventory.Installation),
            InstalledPenumbraVersion = inventory.Installation.InstalledVersion,
            OrganizationPreferences = preferences,
            ProposedFolders = folders.Select(folder => new OrganizerSessionFolder(folder.Path, folder.ManuallyCreated, folder.Protected)).ToArray(),
            Mods = proposals.Select(proposal => new OrganizerSessionMod(
                proposal.StableScanId,
                proposal.CurrentVirtualFolder,
                proposal.ProposedVirtualFolder,
                proposal.Protected,
                proposal.OrganizerCreatorLabel,
                proposal.OrganizerTypeLabel,
                proposal.Source,
                proposal.NeedsReview)).ToArray(),
        };

        return new ProposalSnapshot(
            OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences),
            OrganizerSessionService.BuildSessionIdentity(session),
            preferences,
            proposals,
            folders,
            validation);
    }
}
