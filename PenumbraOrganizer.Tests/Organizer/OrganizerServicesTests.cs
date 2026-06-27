namespace PenumbraOrganizer.Tests.Organizer;

using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class OrganizerServicesTests
{
    [Fact]
    public void AssignToFolder_AffectsOnlySelectedIds()
    {
        var mods = Mods("a", "b", "c");
        var folders = new List<OrganizerFolder> { new("Target") };
        var service = new OrganizerMutationService();

        service.AssignToFolder(mods, folders, ["a", "c"], "Target");

        mods.Single(m => m.StableScanId == "a").ProposedVirtualFolder.Should().Be("Target");
        mods.Single(m => m.StableScanId == "b").ProposedVirtualFolder.Should().Be("Current");
        mods.Single(m => m.StableScanId == "c").ProposedVirtualFolder.Should().Be("Target");
    }

    [Fact]
    public void ProtectedSelectedRow_IsSkippedForAssignment()
    {
        var mods = Mods("a", "b");
        mods[0].Protected = true;
        var service = new OrganizerMutationService();

        var result = service.AssignToFolder(mods, new List<OrganizerFolder>(), ["a", "b"], "Target");

        result.Succeeded.Should().BeTrue();
        mods[0].ProposedVirtualFolder.Should().Be("Current");
        mods[1].ProposedVirtualFolder.Should().Be("Target");
        result.HistoryEntry!.AffectedStableScanIds.Should().BeEquivalentTo("b");
    }

    [Fact]
    public void BulkAssignmentCreatesOneUndoEntry_AndUndoRedoRestoresRows()
    {
        var mods = Mods("a", "b");
        var folders = new List<OrganizerFolder>();
        var service = new OrganizerMutationService();

        var result = service.AssignToFolder(mods, folders, ["a", "b"], "Target");

        result.HistoryEntry!.RowChanges.Should().HaveCount(2);
        service.ApplyUndo(mods, folders, result.HistoryEntry);
        mods.Should().OnlyContain(mod => mod.ProposedVirtualFolder == "Current");
        service.ApplyRedo(mods, folders, result.HistoryEntry);
        mods.Should().OnlyContain(mod => mod.ProposedVirtualFolder == "Target");
    }

    [Fact]
    public void NewActionAfterUndo_ClearsRedoBranchPolicy()
    {
        var undo = new Stack<OrganizerHistoryEntry>();
        var redo = new Stack<OrganizerHistoryEntry>();
        var mods = Mods("a", "b");
        var service = new OrganizerMutationService();
        var first = service.AssignToFolder(mods, new List<OrganizerFolder>(), ["a"], "One").HistoryEntry!;
        undo.Push(first);
        service.ApplyUndo(mods, new List<OrganizerFolder>(), undo.Pop());
        redo.Push(first);

        var second = service.AssignToFolder(mods, new List<OrganizerFolder>(), ["b"], "Two").HistoryEntry!;
        undo.Push(second);
        redo.Clear();

        redo.Should().BeEmpty();
        undo.Peek().Description.Should().Contain("Two");
    }

    [Fact]
    public void RenameFolder_UpdatesDescendantsAndAssignedMods()
    {
        var mods = Mods("a", "b");
        mods[0].ProposedVirtualFolder = "Clothing/Bizu";
        mods[1].ProposedVirtualFolder = "Clothing/Bizu/Sub";
        var folders = new List<OrganizerFolder> { new("Clothing/Bizu", true), new("Clothing/Bizu/Sub", true) };

        var result = new OrganizerMutationService().RenameFolder(mods, folders, "Clothing/Bizu", "Clothing/Bizu Mods");

        result.Succeeded.Should().BeTrue();
        mods[0].ProposedVirtualFolder.Should().Be("Clothing/Bizu Mods");
        mods[1].ProposedVirtualFolder.Should().Be("Clothing/Bizu Mods/Sub");
        folders.Select(f => f.Path).Should().Contain(["Clothing/Bizu Mods", "Clothing/Bizu Mods/Sub"]);
    }

    [Fact]
    public void RenameFolder_BlocksProtectedDescendants()
    {
        var mods = Mods("a");
        mods[0].Protected = true;
        mods[0].ProposedVirtualFolder = "Clothing/Bizu";
        var folders = new List<OrganizerFolder> { new("Clothing/Bizu", true) };

        var result = new OrganizerMutationService().RenameFolder(mods, folders, "Clothing/Bizu", "Clothing/Bizu Mods");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RenameFolder_RejectsDuplicateSiblingName()
    {
        var mods = Mods("a");
        var folders = new List<OrganizerFolder> { new("Clothing/Bizu", true), new("Clothing/Bizu Mods", true) };

        var result = new OrganizerMutationService().RenameFolder(mods, folders, "Clothing/Bizu", "Clothing/Bizu Mods");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void DeleteEmptyFolder_AllowsOnlyEmptyFolders()
    {
        var service = new OrganizerMutationService();
        var mods = Mods("a");
        var folders = new List<OrganizerFolder> { new("Empty", true), new("Full", true) };
        mods[0].ProposedVirtualFolder = "Full";

        service.DeleteEmptyFolder(mods, folders, "Empty").Succeeded.Should().BeTrue();
        service.DeleteEmptyFolder(mods, folders, "Full").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Rooted")]
    [InlineData(@"C:\Mods")]
    [InlineData("A//B")]
    [InlineData("A/../B")]
    [InlineData("A/.")]
    public void VirtualFolderPath_RejectsInvalidPaths(string path)
    {
        VirtualFolderPath.IsValid(path, out _).Should().BeFalse();
    }

    [Fact]
    public void ProposalValidation_ReportsProtectedMutationAndSummaryCounts()
    {
        var inventory = Inventory("a", "b");
        var mods = Mods("a", "b");
        mods[0].Protected = true;
        mods[0].ProposedVirtualFolder = "Changed";
        mods[1].ProposedVirtualFolder = "Changed";

        var result = new OrganizerProposalValidationService().Validate(inventory, mods, [], OrganizationPreferences.DefaultManual);

        result.Errors.Should().Contain(e => e.Code == "ProtectedPathChanged");
        result.Summary.TotalMods.Should().Be(2);
        result.Summary.Changed.Should().Be(1);
        result.Summary.Invalid.Should().Be(1);
    }

    [Fact]
    public async Task SessionSaveAndRestore_UsesStableIdsAndAtomicReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerSessionTests", Guid.NewGuid().ToString("N"));
        var service = new OrganizerSessionService(root);
        var inventory = Inventory("a", "b");
        var session = Session(inventory, new[]
        {
            new OrganizerSessionMod("a", "Current", "Target", false, "Creator", "Type", OrganizerProposalSource.Manual, false),
            new OrganizerSessionMod("b", "Current", "Current", false, "Creator", "Type", OrganizerProposalSource.PreservedCurrent, false),
        });

        await service.SaveLastSessionAsync(session, CancellationToken.None);
        var restored = await service.TryLoadLastSessionAsync(inventory, CancellationToken.None);

        File.Exists(service.LastSessionPath).Should().BeTrue();
        File.Exists(service.LastSessionPath + ".tmp").Should().BeFalse();
        restored.CanResume.Should().BeTrue();
        restored.Session!.Mods.Single(m => m.StableScanId == "a").ProposedVirtualFolder.Should().Be("Target");
    }

    [Fact]
    public async Task SessionRestore_DetectsStaleLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerSessionTests", Guid.NewGuid().ToString("N"));
        var service = new OrganizerSessionService(root);
        var inventory = Inventory("a", "b");
        await service.SaveLastSessionAsync(Session(inventory, [new OrganizerSessionMod("a", "Current", "Target", false, "Creator", "Type", OrganizerProposalSource.Manual, false)]), CancellationToken.None);

        var restored = await service.TryLoadLastSessionAsync(inventory, CancellationToken.None);

        restored.CanResume.Should().BeFalse();
        restored.IsStale.Should().BeTrue();
    }

    private static List<OrganizerModProposal> Mods(params string[] ids)
        => ids.Select(id => new OrganizerModProposal
        {
            StableScanId = id,
            Name = id,
            CurrentVirtualFolder = "Current",
            ProposedVirtualFolder = "Current",
            OriginalCreator = "Creator",
            OrganizerCreatorLabel = "Creator",
            OrganizerTypeLabel = "Type",
            Protected = false,
            OriginalProtected = false,
        }).ToList();

    private static ScanInventory Inventory(params string[] ids)
        => new()
        {
            Installation = new PenumbraInstallation(
                "config",
                Path.Combine(Path.GetTempPath(), "state"),
                Path.Combine(Path.GetTempPath(), "mods"),
                null,
                null,
                "1.0",
                DiscoveryConfidence.High,
                [],
                []),
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = ids.Select(id => new ModScanResult
            {
                StableScanId = id,
                PhysicalDirectory = id,
                PhysicalDirectoryName = id,
                CurrentVirtualFolder = "Current",
                Name = id,
            }).ToArray(),
            CurrentFolderTree = [],
            Collections = [],
            Warnings = [],
        };

    private static OrganizerSessionDocument Session(ScanInventory inventory, IReadOnlyList<OrganizerSessionMod> mods)
        => new()
        {
            ScanIdentity = OrganizerSessionService.BuildScanIdentity(inventory),
            ScanTimestampUtc = inventory.ScannedAtUtc,
            InstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(inventory.Installation),
            InstalledPenumbraVersion = inventory.Installation.InstalledVersion,
            OrganizationPreferences = OrganizationPreferences.DefaultManual,
            ProposedFolders = [new OrganizerSessionFolder("Target", true, false)],
            Mods = mods,
        };
}
