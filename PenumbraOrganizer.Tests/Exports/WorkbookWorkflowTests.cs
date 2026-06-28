namespace PenumbraOrganizer.Tests.Exports;

using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

        var export = await service.ExportAsync(inventory, preferences, CancellationToken.None);
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
        imported.Rows[0].ResolvedDestination.Should().Be("Clothing/Bizu");
    }

    [Fact]
    public async Task Export_FormatsStableIdsAsText()
    {
        var service = CreateService();
        var inventory = CreateInventory(("00123", "Bizu Dress", "Bizu", "Old/Folder"));

        var export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);

        using var workbook = new XLWorkbook(export.WorkbookPath);
        var sheet = workbook.Worksheet("Edit Destinations");
        sheet.Cell(2, 1).Style.NumberFormat.Format.Should().Be("@");
        sheet.Cell(2, 1).GetString().Should().Be("00123");
    }

    [Fact]
    public async Task Import_RejectsDuplicateIds()
    {
        var service = CreateService();
        var inventory = CreateInventory(
            ("Dress01", "Bizu Dress", "Bizu", "Old/Folder"),
            ("Body01", "Gen3 Body", "Author", "Bodies/Old"));
        var export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.TypeOnly), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(3, 1).Value = "Dress01";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error => error.Contains("Duplicate id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_RejectsTraversalAndStaleCurrentFolders()
    {
        var service = CreateService();
        var exportedInventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(exportedInventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 7).Value = "../Bad";
            workbook.Save();
        }

        var changedInventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Moved/Folder"));
        var imported = await service.ImportAsync(export.WorkbookPath, changedInventory, CancellationToken.None);

        imported.Errors.Should().Contain(error => error.Contains("library changed", StringComparison.OrdinalIgnoreCase));
        imported.Errors.Should().Contain(error => error.Contains("current folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_RejectsFormulaDrivenEditableCells()
    {
        var service = CreateService();
        var inventory = CreateInventory(("Dress01", "Bizu Dress", "Bizu", "Old/Folder"));
        var export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);

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
        var export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);

        using (var archive = ZipFile.Open(export.WorkbookPath, ZipArchiveMode.Update))
        {
            archive.CreateEntry("xl/vbaProject.bin");
        }

        var macroAct = () => service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);
        await macroAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Macro-enabled*");

        export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);
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
        var export = await service.ExportAsync(inventory, Preferences(OrganizationStrategy.StartManually), CancellationToken.None);

        using (var workbook = new XLWorkbook(export.WorkbookPath))
        {
            var sheet = workbook.Worksheet("Edit Destinations");
            sheet.Cell(2, 6).Value = "TRUE";
            sheet.Cell(2, 7).Value = "1/Bizu";
            workbook.Save();
        }

        var imported = await service.ImportAsync(export.WorkbookPath, inventory, CancellationToken.None);

        imported.Errors.Should().Contain(error => error.Contains("Protected mod", StringComparison.OrdinalIgnoreCase));
    }

    private static WorkbookWorkflowService CreateService()
        => new(new CreatorCanonicalizer(), NullLogger<WorkbookWorkflowService>.Instance);

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
                SchemaFingerprints = Array.Empty<SchemaFingerprint>(),
                RawMetadata = JsonReadOnlyMemory.Empty,
            }).ToArray(),
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };
}
