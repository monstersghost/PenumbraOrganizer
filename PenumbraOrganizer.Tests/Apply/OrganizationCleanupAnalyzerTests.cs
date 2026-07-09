namespace PenumbraOrganizer.Tests.Apply;

using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Penumbra;

public sealed class OrganizationCleanupAnalyzerTests
{
    private static OrganizerModProposal Proposal(string id, string proposedFolder)
        => new()
        {
            StableScanId = id,
            Name = id,
            CurrentVirtualFolder = proposedFolder,
            ProposedVirtualFolder = proposedFolder,
            OriginalCreator = "Author",
            OriginalProtected = false,
        };

    private static PenumbraOrganizationJson Organization(string json)
    {
        var result = PenumbraOrganizationJson.Parse(json);
        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        return result.Data!;
    }

    [Fact]
    public void FindCandidates_FolderWithNoOccupyingProposal_IsPlainEmpty()
    {
        var organization = Organization("""{"Version":1,"Folders":{"Orphaned/Folder":{}}}""");
        var proposals = new[] { Proposal("Mod A", "Elsewhere") };

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().ContainSingle();
        candidates[0].Path.Should().Be("Orphaned/Folder");
        candidates[0].Kind.Should().Be(OrganizationCleanupCandidateKind.PlainEmpty);
    }

    [Fact]
    public void FindCandidates_FolderExactlyMatchingAProposal_IsExcluded()
    {
        var organization = Organization("""{"Version":1,"Folders":{"Occupied":{}}}""");
        var proposals = new[] { Proposal("Mod A", "Occupied") };

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void FindCandidates_FolderThatIsAPrefixOfAnOccupiedProposal_IsExcluded()
    {
        // A mod proposed into "Weapons/Alice" implicitly occupies the "Weapons" node too --
        // matches PenumbraVirtualFolderWriter.IsFolderOccupied's own semantics.
        var organization = Organization("""{"Version":1,"Folders":{"Weapons":{}}}""");
        var proposals = new[] { Proposal("Mod A", "Weapons/Alice") };

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void FindCandidates_FolderThatIsOnlyASiblingPrefixTextually_IsNotExcluded()
    {
        // "Weapons2" must not be treated as occupied just because "Weapons" is a proposal --
        // the match requires an exact path or a "<folder>/" prefix, not a bare string prefix.
        var organization = Organization("""{"Version":1,"Folders":{"Weapons2":{}}}""");
        var proposals = new[] { Proposal("Mod A", "Weapons2Sub") };

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().ContainSingle().Which.Path.Should().Be("Weapons2");
    }

    [Fact]
    public void FindCandidates_FolderWithCustomization_IsCustomizedEmpty()
    {
        var organization = Organization("""{"Version":1,"Folders":{"Fancy":{"ExpandedColor":123}}}""");
        var proposals = Array.Empty<OrganizerModProposal>();

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().ContainSingle().Which.Kind.Should().Be(OrganizationCleanupCandidateKind.CustomizedEmpty);
    }

    [Fact]
    public void FindCandidates_NoProposalsAtAll_EveryFolderIsACandidate()
    {
        var organization = Organization("""{"Version":1,"Folders":{"A":{},"B":{}}}""");

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, Array.Empty<OrganizerModProposal>());

        candidates.Should().HaveCount(2);
    }

    [Fact]
    public void FindCandidates_NoOrphanedFolders_ReturnsEmpty()
    {
        var organization = Organization("""{"Version":1,"Folders":{"Occupied":{}}}""");
        var proposals = new[] { Proposal("Mod A", "Occupied") };

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, proposals);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void FindCandidates_ResultsAreSortedByPath()
    {
        var organization = Organization("""{"Version":1,"Folders":{"Zeta":{},"Alpha":{},"Mid":{}}}""");

        var candidates = OrganizationCleanupAnalyzer.FindCandidates(organization, Array.Empty<OrganizerModProposal>());

        candidates.Select(candidate => candidate.Path).Should().Equal("Alpha", "Mid", "Zeta");
    }
}
