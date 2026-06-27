namespace PenumbraOrganizer.Infrastructure.Exports;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class AiProposalValidationService : IAiProposalValidationService
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    {
        "keep",
        "move",
        "review",
    };

    private static readonly HashSet<string> AllowedConfidenceValues = new(StringComparer.Ordinal)
    {
        "high",
        "medium",
        "low",
        "protected",
    };

    public AiProposalValidationResult Validate(AiInventoryExport inventory, AiProposalDocument proposal)
    {
        var errors = new List<AiProposalValidationIssue>();
        var warnings = new List<AiProposalValidationIssue>();
        var accepted = new List<AiProposalRow>();
        var rejected = new List<AiProposalRow>();

        if (inventory.FormatVersion != AiExchangeFormat.CurrentFormatVersion)
            errors.Add(Global("UnsupportedInventoryFormat", "The inventory export uses an unsupported format version."));
        if (proposal.FormatVersion != AiExchangeFormat.CurrentFormatVersion)
            errors.Add(Global("UnsupportedProposalFormat", "The proposal uses an unsupported format version."));
        if (!string.Equals(inventory.SourceExportId, proposal.SourceExportId, StringComparison.Ordinal))
            errors.Add(Global("SourceExportIdMismatch", "The proposal sourceExportId does not match the inventory."));

        var inventoryById = inventory.Mods
            .GroupBy(mod => mod.ScanId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var duplicate in inventoryById.Where(pair => pair.Value.Length > 1))
            errors.Add(new AiProposalValidationIssue(duplicate.Key, "DuplicateInventoryScanId", "The inventory contains a duplicate scanId."));

        if (proposal.Proposals.Count != inventory.Mods.Count)
            errors.Add(Global("RowCountMismatch", "The proposal row count does not match the inventory row count."));

        var proposalGroups = proposal.Proposals
            .GroupBy(row => row.ScanId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var duplicate in proposalGroups.Where(pair => pair.Value.Length > 1))
            errors.Add(new AiProposalValidationIssue(duplicate.Key, "DuplicateProposalScanId", "The proposal contains a duplicate scanId."));

        foreach (var inventoryRow in inventory.Mods)
        {
            if (!proposalGroups.ContainsKey(inventoryRow.ScanId))
                errors.Add(new AiProposalValidationIssue(inventoryRow.ScanId, "MissingProposalRow", "The proposal omitted an inventory row."));
        }

        foreach (var proposalGroup in proposalGroups)
        {
            if (!inventoryById.ContainsKey(proposalGroup.Key))
                errors.Add(new AiProposalValidationIssue(proposalGroup.Key, "UnknownProposalScanId", "The proposal contains an unknown scanId."));
        }

        foreach (var row in proposal.Proposals)
        {
            var rowErrorsBefore = errors.Count;
            if (!inventoryById.TryGetValue(row.ScanId, out var inventoryMatches) || inventoryMatches.Length != 1)
            {
                rejected.Add(row);
                continue;
            }

            var inventoryRow = inventoryMatches[0];
            ValidateRow(inventory, inventoryRow, row, errors, warnings);

            if (errors.Count == rowErrorsBefore)
                accepted.Add(row);
            else
                rejected.Add(row);
        }

        return new AiProposalValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            AcceptedProposals = accepted,
            RejectedProposals = rejected,
            Summary = new AiProposalValidationSummary(
                inventory.Mods.Count,
                proposal.Proposals.Count,
                accepted.Count,
                rejected.Count,
                errors.Count,
                warnings.Count),
        };
    }

    private static void ValidateRow(
        AiInventoryExport inventory,
        AiInventoryMod inventoryRow,
        AiProposalRow row,
        List<AiProposalValidationIssue> errors,
        List<AiProposalValidationIssue> warnings)
    {
        if (!string.Equals(row.CurrentVirtualFolder, inventoryRow.CurrentVirtualFolder, StringComparison.Ordinal))
            errors.Add(Issue(row, "CurrentFolderMismatch", "The proposal did not copy currentVirtualFolder exactly."));

        if (!AllowedActions.Contains(row.Action))
            errors.Add(Issue(row, "InvalidAction", "The proposal action is not supported."));

        if (!AllowedConfidenceValues.Contains(row.Confidence))
            errors.Add(Issue(row, "InvalidConfidence", "The proposal confidence is not supported."));

        if (!IsLogicalVirtualPath(row.ProposedVirtualFolder))
            errors.Add(Issue(row, "InvalidProposedPath", "The proposed folder is not a valid relative Penumbra virtual path."));

        if (inventoryRow.ProtectedRow)
        {
            if (!row.Protected)
                errors.Add(Issue(row, "ProtectedFlagMismatch", "The proposal did not preserve the protected flag."));
            if (!string.Equals(row.ProposedVirtualFolder, inventoryRow.CurrentVirtualFolder, StringComparison.Ordinal))
                errors.Add(Issue(row, "ProtectedRowChanged", "Protected rows must remain in their current Penumbra folder."));
            if (!string.Equals(row.Action, "keep", StringComparison.Ordinal))
                errors.Add(Issue(row, "ProtectedActionInvalid", "Protected rows must use action = keep."));
            if (!string.Equals(row.Confidence, "protected", StringComparison.Ordinal))
                errors.Add(Issue(row, "ProtectedConfidenceInvalid", "Protected rows must use confidence = protected."));
            return;
        }

        if (string.Equals(row.Action, "keep", StringComparison.Ordinal) &&
            !string.Equals(row.CurrentVirtualFolder, row.ProposedVirtualFolder, StringComparison.Ordinal))
            errors.Add(Issue(row, "KeepChangedFolder", "Rows with action = keep must keep the same folder."));

        if (string.Equals(row.Action, "move", StringComparison.Ordinal) &&
            string.Equals(row.CurrentVirtualFolder, row.ProposedVirtualFolder, StringComparison.Ordinal))
            errors.Add(Issue(row, "MoveUnchangedFolder", "Rows with action = move must change the folder."));

        if (string.Equals(row.Action, "review", StringComparison.Ordinal) &&
            !IsReviewDestination(row, inventory.OrganizationPreferences))
            errors.Add(Issue(row, "InvalidReviewDestination", "Rows sent to review must preserve the current folder or use a valid Review destination."));

        ValidateStrategyCompliance(inventory.OrganizationPreferences, row, errors, warnings);
    }

    private static bool IsReviewDestination(AiProposalRow row, AiOrganizationPreferences preferences)
        => string.Equals(row.ProposedVirtualFolder, row.CurrentVirtualFolder, StringComparison.Ordinal) ||
           row.ProposedVirtualFolder.Split('/', StringSplitOptions.RemoveEmptyEntries)
               .Any(component => string.Equals(component, "Review", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(component, "Needs Review", StringComparison.OrdinalIgnoreCase)) ||
           string.Equals(preferences.UncertainClassificationBehavior, "Review", StringComparison.Ordinal);

    private static void ValidateStrategyCompliance(
        AiOrganizationPreferences preferences,
        AiProposalRow row,
        List<AiProposalValidationIssue> errors,
        List<AiProposalValidationIssue> warnings)
    {
        if (string.Equals(row.Action, "review", StringComparison.Ordinal))
            return;

        var components = row.ProposedVirtualFolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (preferences.Strategy)
        {
            case "StartManually":
                if (!string.Equals(row.ProposedVirtualFolder, row.CurrentVirtualFolder, StringComparison.Ordinal))
                    errors.Add(Issue(row, "StartManuallyChanged", "StartManually AI proposals must preserve current folders unless a future explicit override is supplied."));
                break;
            case "CreatorOnly":
                if (components.Length > 1)
                    errors.Add(Issue(row, "CreatorOnlyTypeLayer", "CreatorOnly proposals must not create type folders or nested type layers."));
                if (!string.IsNullOrWhiteSpace(row.ProposedType))
                    warnings.Add(Issue(row, "CreatorOnlyIgnoredType", "CreatorOnly mode does not require proposedType."));
                break;
            case "TypeOnly":
                if (components.Length > 1)
                    errors.Add(Issue(row, "TypeOnlyCreatorLayer", "TypeOnly proposals must not create creator subfolders."));
                if (!string.IsNullOrWhiteSpace(row.ProposedCreator))
                    warnings.Add(Issue(row, "TypeOnlyIgnoredCreator", "TypeOnly mode does not require proposedCreator."));
                break;
            case "TypeThenCreator":
                ValidateOrderedPath(row, errors, ["Type", "Creator"]);
                break;
            case "CreatorThenType":
                ValidateOrderedPath(row, errors, ["Creator", "Type"]);
                break;
            case "PreserveAndClean":
                break;
            case "Custom":
                ValidateCustomPath(preferences, row, errors);
                break;
            default:
                errors.Add(Issue(row, "UnknownStrategy", $"Unknown organization strategy {preferences.Strategy}."));
                break;
        }
    }

    private static void ValidateOrderedPath(AiProposalRow row, List<AiProposalValidationIssue> errors, IReadOnlyList<string> order)
    {
        var expected = new List<string>();
        foreach (var component in order)
        {
            var value = component == "Type" ? row.ProposedType : row.ProposedCreator;
            if (!string.IsNullOrWhiteSpace(value))
                expected.Add(value);
        }

        if (expected.Count != 2)
        {
            errors.Add(Issue(row, "MissingResolvedComponent", "Combined strategies require both proposedType and proposedCreator."));
            return;
        }

        var expectedPath = string.Join('/', expected);
        if (!string.Equals(row.ProposedVirtualFolder, expectedPath, StringComparison.Ordinal))
            errors.Add(Issue(row, "StrategyPathOrderMismatch", $"The proposed folder must match {string.Join("/", order)} order."));
    }

    private static void ValidateCustomPath(AiOrganizationPreferences preferences, AiProposalRow row, List<AiProposalValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(preferences.CustomPattern))
        {
            errors.Add(Issue(row, "MissingCustomPattern", "Custom strategy requires a validated custom pattern."));
            return;
        }

        var expected = preferences.CustomPattern
            .Replace("{Creator}", row.ProposedCreator ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Type}", row.ProposedType ?? string.Empty, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(preferences.FixedRootFolder) &&
            !expected.StartsWith(preferences.FixedRootFolder, StringComparison.Ordinal))
            expected = $"{preferences.FixedRootFolder}/{expected}";

        expected = string.Join('/', expected.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!string.Equals(row.ProposedVirtualFolder, expected, StringComparison.Ordinal))
            errors.Add(Issue(row, "CustomPatternMismatch", "The proposed folder does not match the selected custom pattern."));
    }

    private static bool IsLogicalVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Any(char.IsControl) ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.StartsWith('\\') ||
            path.StartsWith('/') ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            Path.IsPathRooted(path))
            return false;

        var components = path.Replace('\\', '/').Split('/', StringSplitOptions.None);
        return components.All(component =>
            !string.IsNullOrWhiteSpace(component) &&
            component != "." &&
            component != "..");
    }

    private static AiProposalValidationIssue Issue(AiProposalRow row, string code, string message)
        => new(row.ScanId, code, message);

    private static AiProposalValidationIssue Global(string code, string message)
        => new(string.Empty, code, message);
}
