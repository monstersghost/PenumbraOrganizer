namespace PenumbraOrganizer.Infrastructure.Exports;

using System.Text.Json;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class AiProposalImportService : IAiProposalImportService
{
    private readonly IAiProposalValidationService _validationService;

    public AiProposalImportService(IAiProposalValidationService validationService)
    {
        _validationService = validationService;
    }

    public async Task<AiProposalImportResult> ImportAsync(
        string proposalPath,
        string inventoryPath,
        IReadOnlyList<OrganizerModProposal> currentProposals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var inventoryStream = File.OpenRead(inventoryPath);
        var inventory = await JsonSerializer.DeserializeAsync<AiInventoryExport>(inventoryStream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The exported inventory could not be read.");

        await using var proposalStream = File.OpenRead(proposalPath);
        var proposal = await JsonSerializer.DeserializeAsync<AiProposalDocument>(proposalStream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The AI proposal could not be read.");

        var validation = _validationService.Validate(inventory, proposal);
        var globalErrors = validation.Errors.Where(issue => string.IsNullOrWhiteSpace(issue.ScanId)).ToArray();
        if (globalErrors.Length > 0)
        {
            var rejectedDecisions = currentProposals
                .OrderBy(row => row.StableScanId, StringComparer.Ordinal)
                .Select(row => new AiProposalImportDecision(
                    row.StableScanId,
                    row.Name,
                    AiImportDecisionKind.RejectedSuggestion,
                    "The AI proposal failed global validation and was not imported."))
                .ToArray();

            return new AiProposalImportResult(
                proposalPath,
                proposal.SourceExportId,
                ImportedCount: 0,
                ManualOverrideCount: 0,
                RejectedCount: rejectedDecisions.Length,
                NeedsReviewCount: 0,
                ImportedRows: Array.Empty<ImportedProposalRow>(),
                Decisions: rejectedDecisions,
                Errors: validation.Errors,
                Warnings: validation.Warnings,
                Summary: $"AI import blocked. {globalErrors.Length} global validation error(s) were found.");
        }

        var acceptedById = validation.AcceptedProposals.ToDictionary(row => row.ScanId, StringComparer.Ordinal);
        var rejectedIds = validation.RejectedProposals.Select(row => row.ScanId).ToHashSet(StringComparer.Ordinal);

        var importedRows = new List<ImportedProposalRow>();
        var decisions = new List<AiProposalImportDecision>();
        var manualOverrideCount = 0;
        var needsReviewCount = 0;

        foreach (var current in currentProposals.OrderBy(row => row.StableScanId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rejectedIds.Contains(current.StableScanId))
            {
                decisions.Add(new AiProposalImportDecision(
                    current.StableScanId,
                    current.Name,
                    AiImportDecisionKind.RejectedSuggestion,
                    "The AI proposal row was rejected during validation."));
                continue;
            }

            if (!acceptedById.TryGetValue(current.StableScanId, out var accepted))
            {
                decisions.Add(new AiProposalImportDecision(
                    current.StableScanId,
                    current.Name,
                    AiImportDecisionKind.Unchanged,
                    "No validated AI proposal was available for this mod."));
                continue;
            }

            var manualOverride =
                current.Source == OrganizerProposalSource.Manual &&
                (!string.Equals(current.ProposedVirtualFolder, current.CurrentVirtualFolder, StringComparison.Ordinal) ||
                 current.Protected != current.OriginalProtected);
            if (manualOverride)
            {
                manualOverrideCount++;
                decisions.Add(new AiProposalImportDecision(
                    current.StableScanId,
                    current.Name,
                    AiImportDecisionKind.ManualOverride,
                    "The existing manual proposal was preserved instead of the imported AI suggestion."));
                continue;
            }

            var requiresReview =
                string.Equals(accepted.Action, "review", StringComparison.Ordinal) ||
                string.Equals(accepted.Confidence, "low", StringComparison.OrdinalIgnoreCase) ||
                accepted.Warnings.Count > 0;
            if (requiresReview)
                needsReviewCount++;

            importedRows.Add(new ImportedProposalRow(
                current.StableScanId,
                accepted.ProposedVirtualFolder,
                string.IsNullOrWhiteSpace(accepted.ProposedCreator) ? current.OrganizerCreatorLabel : accepted.ProposedCreator.Trim(),
                string.IsNullOrWhiteSpace(accepted.ProposedType) ? current.OrganizerTypeLabel : accepted.ProposedType.Trim(),
                OrganizerProposalSource.ImportedAi,
                current.Protected,
                requiresReview));

            decisions.Add(new AiProposalImportDecision(
                current.StableScanId,
                current.Name,
                requiresReview ? AiImportDecisionKind.NeedsReview : AiImportDecisionKind.ImportedSuggestion,
                requiresReview
                    ? "The AI proposal was imported and marked for review."
                    : "The AI proposal was imported into the current organizer session."));
        }

        var summary =
            $"Imported {importedRows.Count} AI suggestion(s). " +
            $"Manual overrides preserved: {manualOverrideCount}. " +
            $"Rejected suggestions: {validation.RejectedProposals.Count}. " +
            $"Needs review: {needsReviewCount}.";

        return new AiProposalImportResult(
            proposalPath,
            proposal.SourceExportId,
            importedRows.Count,
            manualOverrideCount,
            validation.RejectedProposals.Count,
            needsReviewCount,
            importedRows,
            decisions,
            validation.Errors,
            validation.Warnings,
            summary);
    }
}
