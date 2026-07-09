namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

public enum OrganizationCleanupCandidateKind
{
    /// <summary>No occupying mod, and no color/sort-mode/separator customization set.</summary>
    PlainEmpty,

    /// <summary>No occupying mod, but has a color/sort-mode/separator the user set deliberately.</summary>
    CustomizedEmpty,
}

public sealed record OrganizationCleanupCandidate(
    string Path,
    OrganizationCleanupCandidateKind Kind,
    PenumbraOrganizationFolderEntry Entry);

/// <summary>
/// Classifies <c>organization.json</c> folder entries as prunable (no mod currently proposed into
/// that path or a descendant of it) or not, splitting prunable entries into plain-empty and
/// customized-empty so callers can default-select only the former. See
/// docs/superpowers/specs/2026-07-09-organization-json-cleanup-design.md.
/// </summary>
public static class OrganizationCleanupAnalyzer
{
    /// <summary>
    /// Finds <c>organization.json</c> folder entries with no occupying mod under
    /// <paramref name="proposals"/>. A folder counts as occupied if it equals or prefixes any
    /// proposal's <see cref="OrganizerModProposal.ProposedVirtualFolder"/> -- the same
    /// equal-or-prefix semantics <c>PenumbraVirtualFolderWriter.IsFolderOccupied</c> uses for
    /// <c>sort_order.json</c>'s <c>EmptyFolders</c>, so a folder with only occupied descendants is
    /// never treated as prunable even if nothing occupies that literal path.
    /// </summary>
    public static IReadOnlyList<OrganizationCleanupCandidate> FindCandidates(
        PenumbraOrganizationJson organizationJson,
        IReadOnlyList<OrganizerModProposal> proposals)
    {
        var proposedFolders = proposals
            .Select(proposal => proposal.ProposedVirtualFolder)
            .Where(folder => !string.IsNullOrEmpty(folder))
            .ToArray();

        var candidates = new List<OrganizationCleanupCandidate>();
        foreach (var (path, entry) in organizationJson.Folders)
        {
            if (string.IsNullOrEmpty(path) || IsOccupied(path, proposedFolders))
                continue;

            var kind = entry.IsCustomized ? OrganizationCleanupCandidateKind.CustomizedEmpty : OrganizationCleanupCandidateKind.PlainEmpty;
            candidates.Add(new OrganizationCleanupCandidate(path, kind, entry));
        }

        return candidates
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsOccupied(string folderPath, IReadOnlyList<string> proposedFolders)
        => proposedFolders.Any(proposed =>
            string.Equals(proposed, folderPath, StringComparison.Ordinal) ||
            proposed.StartsWith(folderPath + "/", StringComparison.Ordinal));
}
