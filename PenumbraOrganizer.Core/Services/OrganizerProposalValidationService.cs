namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class OrganizerProposalValidationService : IOrganizerProposalValidationService
{
    public OrganizerValidationResult Validate(
        ScanInventory inventory,
        IReadOnlyList<OrganizerModProposal> proposals,
        IReadOnlyList<OrganizerFolder> folders,
        OrganizationPreferences organizationPreferences)
    {
        var errors = new List<OrganizerValidationIssue>();
        var warnings = new List<OrganizerValidationIssue>();
        var rows = new List<OrganizerValidationRow>();

        var scanById = inventory.Mods.ToDictionary(mod => mod.StableScanId, StringComparer.Ordinal);
        foreach (var duplicate in proposals.GroupBy(row => row.StableScanId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add(new OrganizerValidationIssue(duplicate.Key, "DuplicateProposal", "This mod appears more than once in the proposed plan."));

        foreach (var scanRow in inventory.Mods)
        {
            if (!proposals.Any(row => row.StableScanId.Equals(scanRow.StableScanId, StringComparison.Ordinal)))
            {
                rows.Add(new OrganizerValidationRow(scanRow.StableScanId, scanRow.Name, scanRow.CurrentVirtualFolder, scanRow.CurrentVirtualFolder, OrganizerProposalSource.PreservedCurrent, OrganizerRowStatus.MissingMod, "This mod is missing from the proposed plan."));
                errors.Add(new OrganizerValidationIssue(scanRow.StableScanId, "MissingMod", "This mod is missing from the proposed plan."));
            }
        }

        foreach (var proposal in proposals)
        {
            if (!scanById.TryGetValue(proposal.StableScanId, out var scanRow))
            {
                rows.Add(new OrganizerValidationRow(proposal.StableScanId, proposal.Name, proposal.CurrentVirtualFolder, proposal.ProposedVirtualFolder, proposal.Source, OrganizerRowStatus.MissingMod, "This mod is not part of the current scan."));
                errors.Add(new OrganizerValidationIssue(proposal.StableScanId, "UnknownMod", "This mod is not part of the current scan."));
                continue;
            }

            var status = ValidateRow(scanRow, proposal, organizationPreferences, errors, warnings);
            rows.Add(new OrganizerValidationRow(
                proposal.StableScanId,
                proposal.Name,
                proposal.CurrentVirtualFolder,
                proposal.ProposedVirtualFolder,
                proposal.Source,
                status.Status,
                status.Message));
        }

        var summary = new OrganizerValidationSummary(
            inventory.Mods.Count,
            rows.Count(row => row.Status == OrganizerRowStatus.ValidChange),
            rows.Count(row => row.Status == OrganizerRowStatus.Unchanged),
            rows.Count(row => row.Status == OrganizerRowStatus.Protected),
            rows.Count(row => row.Status == OrganizerRowStatus.NeedsReview),
            rows.Count(row => row.Status is OrganizerRowStatus.InvalidPath or OrganizerRowStatus.BlockedProtected or OrganizerRowStatus.MissingMod or OrganizerRowStatus.StaleScan),
            warnings.Count);

        return new OrganizerValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            Rows = rows,
            Summary = summary,
        };
    }

    private static (OrganizerRowStatus Status, string Message) ValidateRow(
        ModScanResult scanRow,
        OrganizerModProposal proposal,
        OrganizationPreferences preferences,
        List<OrganizerValidationIssue> errors,
        List<OrganizerValidationIssue> warnings)
    {
        if (!proposal.Source.IsDefined())
        {
            errors.Add(new OrganizerValidationIssue(proposal.StableScanId, "InvalidSource", "The proposed change source is not recognized."));
            return (OrganizerRowStatus.InvalidPath, "The proposed change source is not recognized.");
        }

        if (!string.Equals(scanRow.CurrentVirtualFolder, proposal.CurrentVirtualFolder, StringComparison.Ordinal))
        {
            errors.Add(new OrganizerValidationIssue(proposal.StableScanId, "StaleScan", "The current folder no longer matches the scan snapshot."));
            return (OrganizerRowStatus.StaleScan, "The current folder no longer matches the scan snapshot.");
        }

        // An empty proposed folder means the mod stays at (or returns to) the Penumbra root,
        // which is a valid location. Only validate the path shape when a folder is specified.
        if (!string.IsNullOrEmpty(proposal.ProposedVirtualFolder) &&
            !VirtualFolderPath.IsValid(proposal.ProposedVirtualFolder, out var pathError))
        {
            errors.Add(new OrganizerValidationIssue(proposal.StableScanId, "InvalidPath", pathError));
            return (OrganizerRowStatus.InvalidPath, pathError);
        }

        if ((scanRow.Protected || proposal.OriginalProtected || proposal.Protected) &&
            !string.Equals(proposal.ProposedVirtualFolder, scanRow.CurrentVirtualFolder, StringComparison.Ordinal))
        {
            errors.Add(new OrganizerValidationIssue(proposal.StableScanId, "ProtectedPathChanged", "This mod is protected and must stay in its current Penumbra folder."));
            return (OrganizerRowStatus.BlockedProtected, "This mod is protected and must stay in its current Penumbra folder.");
        }

        if (proposal.NeedsReview || proposal.ProposedVirtualFolder.Contains("Review", StringComparison.OrdinalIgnoreCase))
            return (OrganizerRowStatus.NeedsReview, "Review required before this can be applied.");

        var strategyWarning = ValidateStrategy(proposal, preferences);
        if (strategyWarning is not null)
            warnings.Add(new OrganizerValidationIssue(proposal.StableScanId, "StrategyWarning", strategyWarning));

        if (scanRow.Protected || proposal.Protected)
            return (OrganizerRowStatus.Protected, "Protected and unchanged.");

        if (string.Equals(proposal.CurrentVirtualFolder, proposal.ProposedVirtualFolder, StringComparison.Ordinal))
            return (OrganizerRowStatus.Unchanged, "No folder change.");

        return (OrganizerRowStatus.ValidChange, "Ready for Review Changes.");
    }

    private static string? ValidateStrategy(OrganizerModProposal proposal, OrganizationPreferences preferences)
    {
        var components = proposal.ProposedVirtualFolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return preferences.Strategy switch
        {
            OrganizationStrategy.CreatorOnly when components.Length > 1 => "Creator-only organization should not create type folders.",
            OrganizationStrategy.TypeOnly when components.Length > 1 => "Type-only organization should not create creator subfolders.",
            OrganizationStrategy.TypeThenCreator when components.Length is > 0 and not 2 => "Type and creator organization expects Type/Creator.",
            OrganizationStrategy.CreatorThenType when components.Length is > 0 and not 2 => "Creator and type organization expects Creator/Type.",
            _ => null,
        };
    }
}

file static class OrganizerProposalSourceExtensions
{
    public static bool IsDefined(this OrganizerProposalSource source)
        => Enum.IsDefined(source);
}
