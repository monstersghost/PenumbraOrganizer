namespace PenumbraOrganizer.Tests.Exports;

using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Classification;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Exports;
using System.IO.Compression;

public sealed class WorkbookWorkflowTests
{
    [Fact]
    public async Task ExportAndImport_RoundTripsStableIds_AndResolvesCategoryShorthand()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var preferences = Preferences(OrganizationStrategy.TypeThenCreator);

        var export = await service.ExportAsync(inventory, Proposals(inventory), preferences, CreateWorkbookPath(), CancellationToken.None);
        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).Value = "1/Bizu";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().ContainSingle();
        imported.Rows[0].StableScanId.Should().Be("Dress01");
        imported.Rows[0].ResolvedModType.Should().Be("Gear");
        imported.Rows[0].ResolvedDestination.Should().Be("Gear/Bizu");
    }

    [Fact]
    public async Task Import_AllowsEditableModTypeWithoutForcingMovement()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 5).Value = "16";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().ContainSingle();
        imported.Rows[0].ResolvedModType.Should().Be("Others");
        imported.Rows[0].ResolvedDestination.Should().BeNull();
    }

    [Fact]
    public async Task Import_TreatsNegativeNumberDestinationAsLiteralFolderName_NotCategoryCodeShorthand()
    {
        // Category codes only run 1-16 (see WorkbookCategoryCatalog.Definitions), so a negative
        // number can never legitimately be an attempted code shorthand -- it can only be a real
        // folder name that happens to start with a negative number.
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).Value = "-16/SomeCreator";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().ContainSingle();
        imported.Rows[0].ResolvedDestination.Should().Be("-16/SomeCreator");
    }

    [Fact]
    public async Task Import_DistinguishesBlankDestinationFromExplicitReviewDestination()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).Value = "16/Review";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().ContainSingle();
        imported.Rows[0].ResolvedDestination.Should().Be("Others/Review");
    }

    [Fact]
    public async Task Export_FormatsStableIdsAsText()
    {
        var service = CreateService();
        var inventory = CreateInventory(("00123", "Bizu Dress", "Bizu", "Old/Folder"));

        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using var workbook = new XLWorkbook(export.WorkbookPath);
        var sheet = workbook.Worksheet("Edit Destinations");
        sheet.Cell(2, 1).GetString().Should().Be("1");
        sheet.Cell(2, 8).Style.NumberFormat.Format.Should().Be("@");
        sheet.Cell(2, 8).GetString().Should().Be("00123");
        sheet.Column(8).IsHidden.Should().BeTrue();
        sheet.Cell(2, 5).GetString().Should().NotBe("Protected");
    }

    [Fact]
    public async Task Export_UsesRequestedSimpleStrategySuggestions()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Current/Folder"));

        (await ReadDestinationAsync(service, inventory, OrganizationStrategy.StartManually)).Should().BeEmpty();
        (await ReadDestinationAsync(service, inventory, OrganizationStrategy.TypeOnly)).Should().Be("1");
        (await ReadDestinationAsync(service, inventory, OrganizationStrategy.TypeThenCreator)).Should().Be("1/Bizu");
        (await ReadDestinationAsync(service, inventory, OrganizationStrategy.CreatorThenType)).Should().Be("Bizu/1");
        (await ReadDestinationAsync(service, inventory, OrganizationStrategy.PreserveAndClean)).Should().Be("Current/Folder");
    }

    [Fact]
    public async Task Import_RejectsDuplicateIds()
    {
        var service = CreateService();
        var inventory = CreateInventory(
            ("Dress01", "Bizu Dress", "Bizu", "Old/Folder"),
            ("Body01", "Gen3 Body", "Author", "Bodies/Old"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.TypeOnly), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(3, 8).Value = "Dress01";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error =>
            error.Contains("Row 3", StringComparison.OrdinalIgnoreCase)
            && error.Contains("duplicate id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_SkipsStaleCurrentFoldersWithWarning_InsteadOfBlocking()
    {
        var service = CreateService();
        var exportedInventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(exportedInventory, Proposals(exportedInventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).Value = "../Bad";
            workbook.Save();
        }

        var changedInventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Moved/Folder"));
        var imported = await service.ImportAsync(export.WorkbookPath, changedInventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().BeEmpty();
        imported.Warnings.Should().Contain(warning => warning.Contains("library changed", StringComparison.OrdinalIgnoreCase));
        imported.Warnings.Should().Contain(warning =>
            warning.Contains("Row 2", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("current folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_AppliesUnaffectedRows_WhenOnlyOneModDriftedSinceExport()
    {
        var service = CreateService();
        var exportedInventory = CreateInventory(
            ("Dress01", "Bizu Dress", "Bizu", "Old/Folder"),
            ("Body01", "Gen3 Body", "Author", "Bodies/Old"));
        var export = await service.ExportAsync(exportedInventory, Proposals(exportedInventory), Preferences(OrganizationStrategy.TypeOnly), CreateWorkbookPath(), CancellationToken.None);

        // Simulates closing the app, someone (or Penumbra itself) moving one mod, then reopening and rescanning.
        var driftedInventory = CreateInventory(
            ("Dress01", "Bizu Dress", "Bizu", "Old/Folder"),
            ("Body01", "Gen3 Body", "Author", "Bodies/Moved"));

        var imported = await service.ImportAsync(export.WorkbookPath, driftedInventory, CancellationToken.None);

        imported.Errors.Should().BeEmpty();
        imported.Rows.Should().ContainSingle(row => row.StableScanId == "Dress01");
        imported.Rows.Should().NotContain(row => row.StableScanId == "Body01");
        imported.Warnings.Should().Contain(warning => warning.Contains("Body01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_AppliesUnaffectedRows_WhenOneRowHasInvalidData()
    {
        var service = CreateService();
        var inventory = CreateInventory(
            ("Dress01", "Bizu Dress", "Bizu", "Old/Folder"),
            ("Body01", "Gen3 Body", "Author", "Bodies/Old"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.TypeOnly), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            var bodyRow = sheet.RowsUsed().Single(row => row.Cell(8).GetString() == "Body01").RowNumber();
            sheet.Cell(bodyRow, 6).Value = "MAYBE";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error => error.Contains("Body01", StringComparison.OrdinalIgnoreCase));
        imported.Rows.Should().ContainSingle(row => row.StableScanId == "Dress01");
        imported.Rows.Should().NotContain(row => row.StableScanId == "Body01");
    }

    [Fact]
    public async Task Import_RejectsFormulaDrivenEditableCells()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).FormulaA1 = "\"1/Bizu\"";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error => error.Contains("formula", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_RejectsMacroAndExternalLinkPackages()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var archive = ZipFile.Open(export.WorkbookPath, ZipArchiveMode.Update))
        {
            archive.CreateEntry("xl/vbaProject.bin");
        }

        var macroAct = () => service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);
        await macroAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Macro-enabled*");

        export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);
        using (var archive = ZipFile.Open(export.WorkbookPath, ZipArchiveMode.Update))
        {
            archive.CreateEntry("xl/externalLinks/externalLink1.xml");
        }

        var externalLinkAct = () => service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);
        await externalLinkAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*external links*");
    }

    [Fact]
    public async Task Import_RejectsProtectedRowsThatAlsoMove()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder", true));
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 6).Value = "TRUE";
            sheet.Cell(2, 7).Value = "1/Bizu";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error =>
            error.Contains("Row 2", StringComparison.OrdinalIgnoreCase)
            && error.Contains("protected mod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_ReflectsLiveProtectionState_EvenWhenScanSnapshotDisagrees()
    {
        var service = CreateService();
        // Simulates the real app: the scan snapshot says unprotected (the pre-fix state, always
        // false), but the user has since protected this mod in the live organizer proposal.
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder", false));
        var proposals = Proposals(inventory);
        proposals.Single(p => p.StableScanId == "Dress01").Protected = true;

        var export = await service.ExportAsync(inventory, proposals, Preferences(OrganizationStrategy.StartManually), CreateWorkbookPath(), CancellationToken.None);

        using var workbook = new XLWorkbook(export.WorkbookPath);
        var sheet = workbook.Worksheet("Edit Destinations");
        sheet.Cell(2, 6).GetString().Should().Be("TRUE");
    }

    private static WorkbookWorkflowService CreateService()
        => new(new CreatorCanonicalizer(), NullLogger<WorkbookWorkflowService>.Instance);

    private static IReadOnlyList<OrganizerModProposal> Proposals(ScanInventory inventory)
        => inventory.Mods.Select(mod => new OrganizerModProposal
        {
            StableScanId = mod.StableScanId,
            Name = mod.Name,
            CurrentVirtualFolder = mod.CurrentVirtualFolder,
            ProposedVirtualFolder = mod.CurrentVirtualFolder,
            OriginalCreator = mod.Author,
            OrganizerCreatorLabel = mod.Author,
            OrganizerTypeLabel = WorkbookCategoryCatalog.Detect(mod).Name,
            Protected = mod.Protected,
            OriginalProtected = mod.Protected,
            Source = OrganizerProposalSource.PreservedCurrent,
        }).ToList();

    private static async Task<string> ReadDestinationAsync(
        WorkbookWorkflowService service,
        ScanInventory inventory,
        OrganizationStrategy strategy)
    {
        var export = await service.ExportAsync(inventory, Proposals(inventory), Preferences(strategy), CreateWorkbookPath(), CancellationToken.None);
        using var workbook = new XLWorkbook(export.WorkbookPath);
        return workbook.Worksheet("Edit Destinations").Cell(2, 7).GetString();
    }

    private static string CreateWorkbookPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenumbraOrganizer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "workbook.xlsx");
    }

    private static OrganizationPreferences Preferences(OrganizationStrategy strategy)
        => new(
            strategy,
            UseTypeFolders: true,
            UseCreatorFolders: true,
            FolderOrder: [OrganizationFolderComponent.Type, OrganizationFolderComponent.Creator],
            FixedRootFolder: null,
            PreserveMeaningfulExistingFolders: true,
            FlattenTemporarySourceFolders: true,
            NormalizeCreatorAliases: true,
            UnknownCreatorBehavior.Review,
            UnknownTypeBehavior.Review,
            UncertainClassificationBehavior.Review,
            PreserveCurrentFolderWhenUncertain: true,
            CustomPattern: null);

    private static ScanInventory CreateInventory(params (string StableId, string Name, string Author, string CurrentFolder)[] mods)
        => CreateInventory(mods.Select(mod => (mod.StableId, mod.Name, mod.Author, mod.CurrentFolder, false)).ToArray());

    private static ScanInventory CreateInventory(params (string StableId, string Name, string Author, string CurrentFolder, bool Protected)[] mods)
        => new()
        {
            Installation = new PenumbraInstallation(
                ConfigurationPath: @"C:\Penumbra\pluginConfigs\Penumbra.json",
                ConfigDirectory: @"C:\Penumbra\pluginConfigs\Penumbra",
                ModRoot: @"C:\Penumbra\Mods",
                PluginAssemblyPath: null,
                PluginManifestPath: null,
                InstalledVersion: "1.0.0",
                Confidence: DiscoveryConfidence.High,
                Evidence: Array.Empty<DiscoveryEvidence>(),
                Warnings: Array.Empty<string>()),
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = mods.Select(mod => new ModScanResult
            {
                StableScanId = mod.StableId,
                PhysicalDirectory = Path.Combine(@"C:\Penumbra\Mods", mod.StableId),
                PhysicalDirectoryName = mod.StableId,
                CurrentVirtualFolder = mod.CurrentFolder,
                Name = mod.Name,
                Author = mod.Author,
                Tags = Array.Empty<string>(),
                RecognizedMetadataFiles = Array.Empty<string>(),
                UnknownMetadataFiles = Array.Empty<string>(),
                MalformedMetadataFiles = Array.Empty<string>(),
                CollectionStates = Array.Empty<ModCollectionState>(),
                Protected = mod.Protected,
                Warnings = Array.Empty<string>(),
                ContentSignalSummary = mod.Name,
                DetectedCategory = ModCategory.Gear,
                SchemaFingerprints = Array.Empty<SchemaFingerprint>(),
                RawMetadata = JsonReadOnlyMemory.Empty,
            }).ToArray(),
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };
}
