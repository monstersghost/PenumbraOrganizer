namespace PenumbraOrganizer.Tests.Exports;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Exports;

public sealed class AiExchangeTests
{
    [Fact]
    public async Task CreateAiReviewPackageAsync_GeneratesUniqueExportIds()
    {
        var service = new InventoryExportService(NullLogger<InventoryExportService>.Instance);
        var inventory = CreateInventory("scan-1");

        var first = await service.CreateAiReviewPackageAsync(inventory, CancellationToken.None, CreatorOnlyPreferences());
        var second = await service.CreateAiReviewPackageAsync(inventory, CancellationToken.None, CreatorOnlyPreferences());

        first.SourceExportId.Should().NotBe(second.SourceExportId);
        first.SourceExportId.Should().StartWith("export-");
        second.SourceExportId.Should().StartWith("export-");
    }

    [Fact]
    public async Task CreateAiReviewPackageAsync_DuplicateScanIdsFailExportValidation()
    {
        var service = new InventoryExportService(NullLogger<InventoryExportService>.Instance);
        var inventory = CreateInventory("duplicate", "duplicate");

        var act = () => service.CreateAiReviewPackageAsync(inventory, CancellationToken.None, CreatorOnlyPreferences());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*duplicate scanId*");
    }

    [Fact]
    public async Task CreateAiReviewPackageAsync_SanitizesAbsoluteProfileAndModRootPaths()
    {
        var service = new InventoryExportService(NullLogger<InventoryExportService>.Instance);
        var root = NewTempRoot();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var modRoot = Path.Combine(root, "Mods");
        var modPath = Path.Combine(modRoot, "BizuDress");
        Directory.CreateDirectory(modPath);
        var inventory = CreateInventory(
            "scan-1",
            modRoot,
            mod => new ModScanResult
            {
                StableScanId = mod.StableScanId,
                PhysicalDirectory = modPath,
                PhysicalDirectoryName = mod.PhysicalDirectoryName,
                CurrentVirtualFolder = mod.CurrentVirtualFolder,
                Name = mod.Name,
                RecognizedMetadataFiles = [Path.Combine(modPath, "meta.json"), Path.Combine(profile, "profile-only.json")],
                UnknownMetadataFiles = [Path.Combine(modRoot, "BizuDress", "group_1.json"), @"C:\Unrelated\outside.json"],
                MalformedMetadataFiles = ["../escape.json", "safe.json"],
                ContentSignalSummary = $"Seen at {Path.Combine(modRoot, "BizuDress", "textures", "a.tex")} and {profile}",
                SchemaFingerprints =
                [
                    new SchemaFingerprint(Path.Combine(modPath, "default_mod.json"), "abc", SchemaDifferenceKind.None, [$"note {modRoot}"]),
                    new SchemaFingerprint(@"C:\Outside\bad.json", "def", SchemaDifferenceKind.None, []),
                ],
            });

        var result = await service.CreateAiReviewPackageAsync(inventory, CancellationToken.None, CreatorOnlyPreferences());
        var json = await File.ReadAllTextAsync(result.InventoryPath);
        var export = JsonSerializer.Deserialize<AiInventoryExport>(json)!;

        json.Should().NotContain(profile.Replace('\\', '/'));
        json.Should().NotContain(profile);
        json.Should().NotContain(modRoot.Replace('\\', '/'));
        json.Should().NotContain(modRoot);
        export.Mods[0].RecognizedMetadataFiles.Should().Contain("meta.json");
        export.Mods[0].RecognizedMetadataFiles.Should().NotContain(path => path.Contains("profile-only", StringComparison.OrdinalIgnoreCase));
        export.Mods[0].UnknownMetadataFiles.Should().Contain("group_1.json");
        export.Mods[0].UnknownMetadataFiles.Should().NotContain(path => path.Contains("Unrelated", StringComparison.OrdinalIgnoreCase));
        export.Mods[0].MalformedMetadataFiles.Should().BeEquivalentTo("safe.json");
        export.Mods[0].SchemaFingerprints.Should().ContainSingle(fp => fp.FileName == "default_mod.json");
    }

    [Fact]
    public async Task ValidateExportPackageAsync_VerifiesZipContentsMatchStandaloneFiles()
    {
        var service = new InventoryExportService(NullLogger<InventoryExportService>.Instance);
        var result = await service.CreateAiReviewPackageAsync(CreateInventory("scan-1"), CancellationToken.None, CreatorOnlyPreferences());

        await service.ValidateExportPackageAsync(result.ExportFolder, CancellationToken.None);

        using var archive = ZipFile.OpenRead(result.ZipPath);
        foreach (var entry in archive.Entries)
        {
            await using var zipStream = entry.Open();
            using var memory = new MemoryStream();
            await zipStream.CopyToAsync(memory);
            var zippedBytes = memory.ToArray();
            var standaloneBytes = await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, entry.Name));
            zippedBytes.Should().Equal(standaloneBytes);
        }
    }

    [Fact]
    public async Task ValidateExportPackageAsync_RejectsUnexpectedZipEntries()
    {
        var service = new InventoryExportService(NullLogger<InventoryExportService>.Instance);
        var result = await service.CreateAiReviewPackageAsync(CreateInventory("scan-1"), CancellationToken.None, CreatorOnlyPreferences());
        File.Delete(result.ZipPath);
        using (var archive = ZipFile.Open(result.ZipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(result.InventoryPath, "Penumbra_Mod_Inventory.json");
            archive.CreateEntryFromFile(result.InstructionsPath, "AI_INSTRUCTIONS.txt");
            archive.CreateEntryFromFile(result.HowToUsePath, "HOW_TO_USE.txt");
            var extra = archive.CreateEntry("extra.txt");
            await using var writer = extra.Open();
            await writer.WriteAsync(Encoding.UTF8.GetBytes("nope"));
        }

        var act = () => service.ValidateExportPackageAsync(result.ExportFolder, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unexpected file*");
    }

    [Fact]
    public void ProposalValidation_ProtectedRowMutationFails()
    {
        var inventory = CreateAiInventory(CreatorOnlyPreferences(), new AiInventoryMod
        {
            ScanId = "protected-1",
            ProtectedRow = true,
            CurrentVirtualFolder = "Keep",
            Name = "Protected Mod",
        });
        var proposal = CreateProposal(inventory, new AiProposalRow
        {
            ScanId = "protected-1",
            Protected = true,
            CurrentVirtualFolder = "Keep",
            ProposedVirtualFolder = "Changed",
            Action = "move",
            Confidence = "high",
            Reason = "bad",
        });

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Code == "ProtectedRowChanged");
    }

    [Fact]
    public void ProposalValidation_MissingRowFails()
    {
        var inventory = CreateAiInventory(CreatorOnlyPreferences(), InventoryMod("a"), InventoryMod("b"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "A", "A", "keep"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.Errors.Should().Contain(error => error.Code == "RowCountMismatch");
        result.Errors.Should().Contain(error => error.Code == "MissingProposalRow" && error.ScanId == "b");
    }

    [Fact]
    public void ProposalValidation_UnknownRowFails()
    {
        var inventory = CreateAiInventory(CreatorOnlyPreferences(), InventoryMod("a"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "A", "A", "keep"), ProposalRow("unknown", "A", "A", "keep"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.Errors.Should().Contain(error => error.Code == "UnknownProposalScanId");
    }

    [Fact]
    public void ProposalJson_MalformedJsonFailsNormalImport()
    {
        var act = () => JsonSerializer.Deserialize<AiProposalDocument>("{not-json");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ProposalJson_MarkdownWrappedJsonFailsNormalImport()
    {
        var wrapped = """
        ```json
        {"formatVersion":1}
        ```
        """;
        var act = () => JsonSerializer.Deserialize<AiProposalDocument>(wrapped);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(@"C:\Mods\Creator")]
    [InlineData("../Creator")]
    [InlineData("Creator/../Other")]
    public void ProposalValidation_InvalidLogicalPathFails(string proposedPath)
    {
        var inventory = CreateAiInventory(CreatorOnlyPreferences(), InventoryMod("a"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Current", proposedPath, "move"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.Errors.Should().Contain(error => error.Code == "InvalidProposedPath");
    }

    [Fact]
    public void ProposalValidation_CreatorOnlyRejectsGeneratedTypeLayers()
    {
        var inventory = CreateAiInventory(CreatorOnlyPreferences(), InventoryMod("a"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Old", "Clothing/Bizu Mods", "move", proposedType: "Clothing", proposedCreator: "Bizu Mods"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.Errors.Should().Contain(error => error.Code == "CreatorOnlyTypeLayer");
    }

    [Fact]
    public void ProposalValidation_TypeOnlyRejectsGeneratedCreatorLayers()
    {
        var inventory = CreateAiInventory(TypeOnlyPreferences(), InventoryMod("a"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Old", "Clothing/Bizu Mods", "move", proposedType: "Clothing", proposedCreator: "Bizu Mods"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.Errors.Should().Contain(error => error.Code == "TypeOnlyCreatorLayer");
    }

    [Fact]
    public void ProposalValidation_TypeThenCreatorAcceptsCorrectOrder()
    {
        var inventory = CreateAiInventory(TypeThenCreatorPreferences(), InventoryMod("a", "Old"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Old", "Clothing/Bizu Mods", "move", proposedType: "Clothing", proposedCreator: "Bizu Mods"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProposalValidation_CreatorThenTypeAcceptsCorrectOrder()
    {
        var inventory = CreateAiInventory(CreatorThenTypePreferences(), InventoryMod("a", "Old"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Old", "Bizu Mods/Clothing", "move", proposedType: "Clothing", proposedCreator: "Bizu Mods"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProposalValidation_PreserveAndCleanPermitsUnchangedRows()
    {
        var inventory = CreateAiInventory(PreserveAndCleanPreferences(), InventoryMod("a", "Existing/Folder"));
        var proposal = CreateProposal(inventory, ProposalRow("a", "Existing/Folder", "Existing/Folder", "keep"));

        var result = new AiProposalValidationService().Validate(inventory, proposal);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProposalValidation_CustomPatternCompliance()
    {
        var inventory = CreateAiInventory(CustomPreferences("My Mods/{Creator}"), InventoryMod("a", "Old"));
        var valid = CreateProposal(inventory, ProposalRow("a", "Old", "My Mods/Bizu Mods", "move", proposedCreator: "Bizu Mods"));
        var invalid = CreateProposal(inventory, ProposalRow("a", "Old", "Other/Bizu Mods", "move", proposedCreator: "Bizu Mods"));

        new AiProposalValidationService().Validate(inventory, valid).IsValid.Should().BeTrue();
        new AiProposalValidationService().Validate(inventory, invalid).Errors.Should().Contain(error => error.Code == "CustomPatternMismatch");
    }

    private static ScanInventory CreateInventory(params string[] scanIds)
        => CreateInventory(scanIds, Path.Combine(NewTempRoot(), "Mods"));

    private static ScanInventory CreateInventory(string[] scanIds, string modRoot)
        => CreateInventory(scanIds, modRoot, mod => mod);

    private static ScanInventory CreateInventory(string scanId, string modRoot, Func<ModScanResult, ModScanResult> mutate)
        => CreateInventory([scanId], modRoot, mutate);

    private static ScanInventory CreateInventory(string[] scanIds, string modRoot, Func<ModScanResult, ModScanResult> mutate)
    {
        Directory.CreateDirectory(modRoot);
        var mods = scanIds.Select(id =>
        {
            var physical = Path.Combine(modRoot, id);
            Directory.CreateDirectory(physical);
            return mutate(new ModScanResult
            {
                StableScanId = id,
                PhysicalDirectory = physical,
                PhysicalDirectoryName = id,
                CurrentVirtualFolder = "Current",
                Name = id,
                RecognizedMetadataFiles = ["meta.json"],
                SchemaFingerprints = [new SchemaFingerprint("meta.json", "abc", SchemaDifferenceKind.None, [])],
            });
        }).ToArray();

        return new ScanInventory
        {
            Installation = new PenumbraInstallation(
                Path.Combine(NewTempRoot(), "Penumbra.json"),
                Path.Combine(NewTempRoot(), "Penumbra"),
                modRoot,
                null,
                null,
                "1.0",
                DiscoveryConfidence.High,
                [],
                []),
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Mods = mods,
            CurrentFolderTree = [],
            Collections = [],
            Warnings = [],
        };
    }

    private static AiInventoryExport CreateAiInventory(OrganizationPreferences preferences, params AiInventoryMod[] mods)
        => new()
        {
            FormatVersion = 1,
            SourceExportId = "export-test",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            OrganizationPreferences = ToAiPreferences(preferences),
            Mods = mods,
        };

    private static AiInventoryMod InventoryMod(string scanId, string currentFolder = "Current")
        => new()
        {
            ScanId = scanId,
            ProtectedRow = false,
            CurrentVirtualFolder = currentFolder,
            Name = scanId,
        };

    private static AiProposalDocument CreateProposal(AiInventoryExport inventory, params AiProposalRow[] rows)
        => new()
        {
            FormatVersion = 1,
            SourceExportId = inventory.SourceExportId,
            GeneratedBy = new AiProposalGeneratedBy { Provider = "test", Model = "test" },
            Summary = new AiProposalSummary
            {
                TotalRowsReceived = inventory.Mods.Count,
                TotalRowsReturned = rows.Length,
                ProtectedRows = rows.Count(row => row.Protected),
                ChangedRows = rows.Count(row => row.Action == "move"),
                UnchangedRows = rows.Count(row => row.Action == "keep"),
                ReviewRows = rows.Count(row => row.Action == "review"),
            },
            CreatorAliases = [],
            Proposals = rows,
        };

    private static AiProposalRow ProposalRow(
        string scanId,
        string current,
        string proposed,
        string action,
        string? proposedType = null,
        string? proposedCreator = null)
        => new()
        {
            ScanId = scanId,
            Protected = false,
            CurrentVirtualFolder = current,
            ProposedVirtualFolder = proposed,
            ProposedType = proposedType,
            ProposedCreator = proposedCreator,
            Action = action,
            Confidence = "high",
            Reason = "test",
            Evidence = [],
            Warnings = [],
        };

    private static AiOrganizationPreferences ToAiPreferences(OrganizationPreferences preferences)
        => new()
        {
            Strategy = preferences.Strategy.ToString(),
            UseTypeFolders = preferences.UseTypeFolders,
            UseCreatorFolders = preferences.UseCreatorFolders,
            FolderOrder = preferences.FolderOrder.Select(component => component.ToString()).ToArray(),
            FixedRootFolder = preferences.FixedRootFolder,
            PreserveMeaningfulExistingFolders = preferences.PreserveMeaningfulExistingFolders,
            FlattenTemporarySourceFolders = preferences.FlattenTemporarySourceFolders,
            NormalizeCreatorAliases = preferences.NormalizeCreatorAliases,
            UnknownCreatorBehavior = preferences.UnknownCreatorBehavior.ToString(),
            UnknownTypeBehavior = preferences.UnknownTypeBehavior.ToString(),
            UncertainClassificationBehavior = preferences.UncertainClassificationBehavior.ToString(),
            PreserveCurrentFolderWhenUncertain = preferences.PreserveCurrentFolderWhenUncertain,
            CustomPattern = preferences.CustomPattern,
        };

    private static OrganizationPreferences CreatorOnlyPreferences()
        => new(
            OrganizationStrategy.CreatorOnly,
            false,
            true,
            [OrganizationFolderComponent.Creator],
            null,
            true,
            true,
            true,
            UnknownCreatorBehavior.PreserveCurrent,
            UnknownTypeBehavior.NotApplicable,
            UncertainClassificationBehavior.Review,
            true,
            null);

    private static OrganizationPreferences TypeOnlyPreferences()
        => new(
            OrganizationStrategy.TypeOnly,
            true,
            false,
            [OrganizationFolderComponent.Type],
            null,
            true,
            true,
            true,
            UnknownCreatorBehavior.NotApplicable,
            UnknownTypeBehavior.PreserveCurrent,
            UncertainClassificationBehavior.Review,
            true,
            null);

    private static OrganizationPreferences TypeThenCreatorPreferences()
        => new(
            OrganizationStrategy.TypeThenCreator,
            true,
            true,
            [OrganizationFolderComponent.Type, OrganizationFolderComponent.Creator],
            null,
            true,
            true,
            true,
            UnknownCreatorBehavior.Review,
            UnknownTypeBehavior.Review,
            UncertainClassificationBehavior.Review,
            true,
            null);

    private static OrganizationPreferences CreatorThenTypePreferences()
        => new(
            OrganizationStrategy.CreatorThenType,
            true,
            true,
            [OrganizationFolderComponent.Creator, OrganizationFolderComponent.Type],
            null,
            true,
            true,
            true,
            UnknownCreatorBehavior.Review,
            UnknownTypeBehavior.Review,
            UncertainClassificationBehavior.Review,
            true,
            null);

    private static OrganizationPreferences PreserveAndCleanPreferences()
        => OrganizationPreferences.DefaultManual;

    private static OrganizationPreferences CustomPreferences(string pattern)
        => new(
            OrganizationStrategy.Custom,
            pattern.Contains("{Type}", StringComparison.Ordinal),
            pattern.Contains("{Creator}", StringComparison.Ordinal),
            [OrganizationFolderComponent.FixedRoot, OrganizationFolderComponent.Creator],
            null,
            true,
            true,
            true,
            UnknownCreatorBehavior.Review,
            UnknownTypeBehavior.Review,
            UncertainClassificationBehavior.Review,
            true,
            pattern);

    private static string NewTempRoot()
        => Path.Combine(Path.GetTempPath(), "PenumbraOrganizerAiTests", Guid.NewGuid().ToString("N"));
}
