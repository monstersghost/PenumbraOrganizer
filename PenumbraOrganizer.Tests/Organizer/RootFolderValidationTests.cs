namespace PenumbraOrganizer.Tests.Organizer;

using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

public sealed class RootFolderValidationTests
{
    private static ModScanResult Mod(string id, string currentFolder) => new()
    {
        StableScanId = id,
        PhysicalDirectory = id,
        PhysicalDirectoryName = id,
        CurrentVirtualFolder = currentFolder,
        Name = id,
    };

    private static OrganizerModProposal Proposal(string id, string current, string proposed) => new()
    {
        StableScanId = id,
        Name = id,
        CurrentVirtualFolder = current,
        ProposedVirtualFolder = proposed,
        OriginalCreator = "Author",
        OrganizerCreatorLabel = "Author",
        OrganizerTypeLabel = "Unknown type",
        Source = OrganizerProposalSource.Manual,
    };

    [Fact]
    public void RootMod_StayingAtRoot_IsUnchanged_NotInvalid()
    {
        var inventory = new ScanInventory
        {
            Installation = null!,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = [Mod("Root Mod", string.Empty)],
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };
        var proposals = new[] { Proposal("Root Mod", string.Empty, string.Empty) };

        var result = new OrganizerProposalValidationService()
            .Validate(inventory, proposals, Array.Empty<OrganizerFolder>(), OrganizationPreferences.DefaultManual);

        result.Errors.Should().BeEmpty();
        result.Rows.Single().Status.Should().Be(OrganizerRowStatus.Unchanged);
    }

    [Fact]
    public void RootMod_MovingIntoFolder_IsValidChange()
    {
        var inventory = new ScanInventory
        {
            Installation = null!,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = [Mod("Root Mod", string.Empty)],
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };
        var proposals = new[] { Proposal("Root Mod", string.Empty, "Clothing") };

        var result = new OrganizerProposalValidationService()
            .Validate(inventory, proposals, [new OrganizerFolder("Clothing")], OrganizationPreferences.DefaultManual);

        result.Errors.Should().BeEmpty();
        result.Rows.Single().Status.Should().Be(OrganizerRowStatus.ValidChange);
    }
}
