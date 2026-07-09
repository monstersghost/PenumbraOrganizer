namespace PenumbraOrganizer.Tests.Apply;

using System.Text;
using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class OrganizationCleanupWriterTests
{
    private readonly OrganizationCleanupWriter _writer = new();

    private static PenumbraInstallation Installation(TemporaryPenumbraFixture fixture)
        => new(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.Manual,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());

    private static OrganizerModProposal Proposal(string id, string proposedFolder)
        => new()
        {
            StableScanId = id,
            Name = id,
            CurrentVirtualFolder = proposedFolder,
            ProposedVirtualFolder = proposedFolder,
            OriginalCreator = "Author",
        };

    private static ProposalSnapshot Snapshot(IReadOnlyList<OrganizerModProposal> proposals, IReadOnlyList<string> selections)
        => new(
            "snapshot",
            "session",
            OrganizationPreferences.DefaultManual,
            proposals,
            Array.Empty<OrganizerFolder>(),
            new OrganizerValidationResult
            {
                Errors = Array.Empty<OrganizerValidationIssue>(),
                Warnings = Array.Empty<OrganizerValidationIssue>(),
                Rows = Array.Empty<OrganizerValidationRow>(),
                Summary = new OrganizerValidationSummary(0, 0, 0, 0, 0, 0, 0),
            },
            selections);

    [Fact]
    public async Task CaptureSourceFileAsync_MissingFile_ReturnsNull()
    {
        using var fixture = new TemporaryPenumbraFixture();

        var snapshot = await _writer.CaptureSourceFileAsync(Installation(fixture), CancellationToken.None);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task CaptureSourceFileAsync_ExistingFile_ReturnsSnapshot()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""{"Version":1,"Folders":{},"Separators":{}}""");

        var snapshot = await _writer.CaptureSourceFileAsync(Installation(fixture), CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.Path.Should().Be(fixture.OrganizationJsonPath);
        snapshot.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task BuildFileChangeAsync_NoSelections_ReturnsNull()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{}},"Separators":{}}""");
        var snapshot = Snapshot(Array.Empty<OrganizerModProposal>(), Array.Empty<string>());

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().BeNull();
    }

    [Fact]
    public async Task BuildFileChangeAsync_MissingOrganizationJson_ReturnsNullEvenWithSelections()
    {
        using var fixture = new TemporaryPenumbraFixture();
        var snapshot = Snapshot(Array.Empty<OrganizerModProposal>(), ["Orphaned"]);

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().BeNull();
    }

    [Fact]
    public async Task BuildFileChangeAsync_UnsupportedVersion_ReturnsNull()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""{"Version":2,"Folders":{"Orphaned":{}},"Separators":{}}""");
        var snapshot = Snapshot(Array.Empty<OrganizerModProposal>(), ["Orphaned"]);

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().BeNull();
    }

    [Fact]
    public async Task BuildFileChangeAsync_SelectionNoLongerOrphaned_IsDroppedSilently_ReturnsNull()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"NowOccupied":{}},"Separators":{}}""");
        // A mod has since been proposed into "NowOccupied" -- the selection is stale.
        var snapshot = Snapshot([Proposal("Mod A", "NowOccupied")], ["NowOccupied"]);

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().BeNull();
    }

    [Fact]
    public async Task BuildFileChangeAsync_ConfirmedOrphan_PrunesOnlyThatEntry_LeavesOthersAndSeparatorsUntouched()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""
        {
          "Version": 1,
          "Folders": {
            "Orphaned/Empty": {},
            "Kept/Customized": { "ExpandedColor": 4294901760, "SortMode": "FoldersFirst" }
          },
          "Separators": {
            "Gear/---divider---": { "CreationDate": 638123456789 }
          }
        }
        """);
        var snapshot = Snapshot(Array.Empty<OrganizerModProposal>(), ["Orphaned/Empty"]);

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().NotBeNull();
        change!.WriteTargetKind.Should().Be(PenumbraWriteTargetKind.OrganizationJson);
        change.TargetPath.Should().Be(fixture.OrganizationJsonPath);
        change.AffectedRecordKeys.Should().ContainSingle().Which.Should().Be("Orphaned/Empty");

        var updatedJson = Encoding.UTF8.GetString(Convert.FromBase64String(change.ExpectedBytesBase64));
        var updatedResult = PenumbraOrganizationJson.Parse(updatedJson);
        updatedResult.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        var updated = updatedResult.Data!;
        updated.Folders.Should().NotContainKey("Orphaned/Empty");
        updated.Folders.Should().ContainKey("Kept/Customized");
        updated.Folders["Kept/Customized"].ExpandedColor.Should().Be(4294901760);
        updated.Folders["Kept/Customized"].SortMode.Should().Be("FoldersFirst");
        updated.SeparatorPaths.Should().ContainSingle().Which.Should().Be("Gear/---divider---");
    }

    [Fact]
    public async Task BuildFileChangeAsync_MultipleConfirmedOrphans_PrunesAllOfThem()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""
        {
          "Version": 1,
          "Folders": { "A/Orphan": {}, "B/Orphan": {}, "C/Kept": {} },
          "Separators": {}
        }
        """);
        var snapshot = Snapshot([Proposal("Mod A", "C/Kept")], ["A/Orphan", "B/Orphan"]);

        var change = await _writer.BuildFileChangeAsync(Installation(fixture), snapshot, CancellationToken.None);

        change.Should().NotBeNull();
        var updatedJson = Encoding.UTF8.GetString(Convert.FromBase64String(change!.ExpectedBytesBase64));
        var updated = PenumbraOrganizationJson.Parse(updatedJson).Data!;
        updated.Folders.Should().ContainSingle().Which.Key.Should().Be("C/Kept");
    }
}
