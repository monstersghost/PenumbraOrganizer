namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class DryRunValidationService : IDryRunValidationService
{
    // Matches the existing ControlledTestOptions.MaximumSelectedModCount convention (3 mods per
    // controlled test) -- caps blast radius per Apply while this write target has not yet been
    // validated against a real Penumbra install. See docs/superpowers/plans/2026-07-09-organization-json-cleanup-plan-4-safety-gating.md.
    private const int MaxOrganizationCleanupFoldersPerApply = 3;

    private readonly IPlanInvalidationService _planInvalidationService;

    public DryRunValidationService(IPlanInvalidationService planInvalidationService)
    {
        _planInvalidationService = planInvalidationService;
    }

    public async Task<DryRunValidationResult> ValidateAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>(plan.Warnings);

        if (plan.PlanId == Guid.Empty)
            errors.Add("A finalized dry run must have a globally unique plan ID.");
        if (plan.FileChanges.Count == 0)
            warnings.Add("No writable file changes were produced by this dry run.");

        foreach (var protectedChange in plan.Entries.Where(entry => entry.Protected && entry.RequiresWrite))
            errors.Add($"Protected row {protectedChange.StableScanId} cannot generate a writable operation.");

        foreach (var invalidEntry in plan.Entries.Where(entry => entry.ValidationStatus is OrganizerRowStatus.InvalidPath or OrganizerRowStatus.BlockedProtected or OrganizerRowStatus.MissingMod or OrganizerRowStatus.StaleScan))
        {
            var message = $"Blocked row {invalidEntry.StableScanId}: {string.Join("; ", invalidEntry.Warnings)}";
            warnings.Add(message);
            errors.Add(message);
        }

        var duplicateTargets = plan.FileChanges
            .GroupBy(change => change.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var target in duplicateTargets)
            errors.Add($"Duplicate file write target detected: {target}");

        var duplicateRecords = plan.Entries
            .Where(entry => entry.RequiresWrite)
            .GroupBy(entry => $"{entry.TargetPath}|{entry.RecordKey}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var record in duplicateRecords)
            errors.Add($"Duplicate authoritative state write detected: {record}");

        var organizationCleanupChange = plan.FileChanges.FirstOrDefault(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson);
        if (organizationCleanupChange is not null && organizationCleanupChange.AffectedRecordKeys.Count > MaxOrganizationCleanupFoldersPerApply)
        {
            if (proposalSnapshot.OrganizationCleanupBypassSafetyCap)
            {
                warnings.Add(
                    $"Advanced Cleanup is active: the {MaxOrganizationCleanupFoldersPerApply}-folder-per-Apply safety cap was bypassed at your own risk. " +
                    $"{organizationCleanupChange.AffectedRecordKeys.Count} folder(s) will be pruned in this Apply.");
            }
            else
            {
                errors.Add(
                    $"Folder cleanup is limited to {MaxOrganizationCleanupFoldersPerApply} folder(s) per Apply while this feature is being validated on real installs. " +
                    $"{organizationCleanupChange.AffectedRecordKeys.Count} folder(s) would be pruned in this Apply -- uncheck some in the Folder Cleanup tab, or apply in smaller batches, " +
                    "or enable Advanced Cleanup in the Folder Cleanup tab to bypass this limit at your own risk.");
            }
        }

        var invalidationReasons = await _planInvalidationService.GetInvalidationReasonsAsync(
            plan,
            installation,
            inventory,
            proposalSnapshot,
            cancellationToken);

        foreach (var reason in invalidationReasons)
            warnings.Add(ToMessage(reason));

        var status = errors.Count > 0
            ? DryRunPlanValidationStatus.Invalid
            : invalidationReasons.Count > 0
                ? DryRunPlanValidationStatus.Stale
                : DryRunPlanValidationStatus.Valid;

        var applyPermitted =
            status == DryRunPlanValidationStatus.Valid &&
            plan.FileChanges.Count > 0 &&
            plan.Entries.All(entry => !entry.Protected || !entry.RequiresWrite);

        return new DryRunValidationResult(
            status,
            applyPermitted,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            invalidationReasons.Distinct().ToArray());
    }

    private static string ToMessage(PlanInvalidationReason reason)
        => reason switch
        {
            PlanInvalidationReason.ProposalChanged => "The proposal snapshot changed since this dry run was created.",
            PlanInvalidationReason.ProtectionChanged => "Protection settings changed since this dry run was created.",
            PlanInvalidationReason.OrganizationStrategyChanged => "Organization preferences changed since this dry run was created.",
            PlanInvalidationReason.NewScanCompleted => "A new scan or folder-state refresh changed the current snapshot.",
            PlanInvalidationReason.SessionChanged => "The organizer session changed since this dry run was created.",
            PlanInvalidationReason.PenumbraVersionChanged => "The installed Penumbra version changed.",
            PlanInvalidationReason.SourceFileHashChanged => "The authoritative Penumbra source file changed.",
            PlanInvalidationReason.SchemaFingerprintChanged => "The authoritative Penumbra schema fingerprint changed.",
            PlanInvalidationReason.ModLibraryIdentityChanged => "The Penumbra installation identity changed.",
            PlanInvalidationReason.TargetModMissing => "At least one targeted mod no longer resolves.",
            PlanInvalidationReason.ApplicationRestarted => "The app restarted, so this dry run must be recreated.",
            PlanInvalidationReason.UnsupportedSchema => "The authoritative Penumbra schema is unsupported for Apply.",
            PlanInvalidationReason.CurrentFolderChanged => "A mod's current Penumbra folder changed since scan.",
            _ => "The dry run is out of date.",
        };
}
