namespace PenumbraOrganizer.Tests.Scanning;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class PenumbraScanServiceTests
{
    [Fact]
    public async Task ScanAsync_ReadsModsFoldersCollectionsAndProtection()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CreateMod(
            "Sample Mod",
            """
            {
              "FileVersion": 3,
              "Name": "Sample Mod",
              "Author": "Bizu",
              "Version": "1.0.0",
              "Website": "https://example.com",
              "Description": "Test mod",
              "ModTags": [ "dress" ]
            }
            """,
            """
            {
              "Files": {
                "chara/equipment/e1234/model/c0201e1234_top.mdl": "files\\top.mdl"
              },
              "Manipulations": []
            }
            """);
        fixture.CreateMod(
            "Akako Locked",
            """
            {
              "FileVersion": 3,
              "Name": "Akako Locked",
              "Author": "Akako"
            }
            """);
        fixture.WriteSortOrder(
            ("Sample Mod", "Clothing/Bizu Mods/Sample Mod"),
            ("Akako Locked", ".Character specific mods/Akako Main Files/Akako Locked"));
        fixture.WriteCollection(
            "aqua.json",
            new
            {
                Version = 2,
                Name = "Aqua",
                Settings = new Dictionary<string, object>
                {
                    ["Sample Mod"] = new { Enabled = true, Priority = 3 },
                },
            });

        var installation = new PenumbraInstallation(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.High,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());

        var service = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, new ProtectionService());
        var inventory = await service.ScanAsync(installation, null, CancellationToken.None);

        inventory.Mods.Should().HaveCount(2);
        inventory.Mods.Should().ContainSingle(m => m.Name == "Sample Mod" && m.ContentSignalSummary.Contains("Clothing", StringComparison.Ordinal));
        inventory.Mods.Should().ContainSingle(m => m.Name == "Akako Locked" && m.Protected);
        inventory.Collections.Should().ContainSingle(c => c.Name == "Aqua");
        inventory.CurrentFolderTree.Should().Contain(node => node.Path == "Clothing/Bizu Mods");
    }

    [Fact]
    public async Task ScanAsync_ReadsLocalModData_FavoriteTagsAndNote()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CreateMod("Fav Mod", """{"FileVersion":3,"Name":"Fav Mod","Author":"Author"}""");
        fixture.CreateMod("Plain Mod", """{"FileVersion":3,"Name":"Plain Mod"}""");
        fixture.WriteModData(("Fav Mod", "Folder"), ("Plain Mod", "Folder"));
        fixture.WriteLocalModData("Fav Mod",
            """{"FileVersion":3,"ImportDate":1,"LocalTags":["cute","wip"],"Note":"keep me","Favorite":true}""");

        var installation = new PenumbraInstallation(
            fixture.PenumbraJsonPath, fixture.PenumbraConfigPath, fixture.ModRoot,
            fixture.PluginAssemblyPath, fixture.PluginManifestPath, "1.6.1.10",
            DiscoveryConfidence.High, Array.Empty<DiscoveryEvidence>(), Array.Empty<string>());

        var service = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, new ProtectionService());
        var inventory = await service.ScanAsync(installation, null, CancellationToken.None);

        var fav = inventory.Mods.Single(m => m.StableScanId == "Fav Mod");
        fav.Favorite.Should().BeTrue();
        fav.LocalTags.Should().Equal("cute", "wip");
        fav.Note.Should().Be("keep me");
        fav.HasLocalData.Should().BeTrue();

        var plain = inventory.Mods.Single(m => m.StableScanId == "Plain Mod");
        plain.Favorite.Should().BeFalse();
        plain.LocalTags.Should().BeEmpty();
        plain.Note.Should().BeEmpty();
        plain.HasLocalData.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_ModWithoutSortOrderEntry_IsAtRootWithoutWarning()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CreateMod("Placed Mod", """{"FileVersion":3,"Name":"Placed Mod"}""");
        fixture.CreateMod("Root Mod", """{"FileVersion":3,"Name":"Root Mod"}""");
        // Only "Placed Mod" has an explicit organization entry; "Root Mod" lives at the root.
        fixture.WriteSortOrder(("Placed Mod", "Clothing/Placed Mod"));

        var installation = new PenumbraInstallation(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.High,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());

        var service = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, new ProtectionService());
        var inventory = await service.ScanAsync(installation, null, CancellationToken.None);

        var placed = inventory.Mods.Single(m => m.Name == "Placed Mod");
        var root = inventory.Mods.Single(m => m.Name == "Root Mod");
        placed.CurrentVirtualFolder.Should().Be("Clothing");
        root.CurrentVirtualFolder.Should().BeEmpty();
        root.Warnings.Should().NotContain(w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_SurfacesExistingEmptyFolders()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CreateMod("Placed Mod", """{"FileVersion":3,"Name":"Placed Mod"}""");
        fixture.WriteSortOrder([("Placed Mod", "Clothing/Placed Mod")], ["telegram", "Reserved/Sub"]);

        var installation = new PenumbraInstallation(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.High,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());

        var service = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, new ProtectionService());
        var inventory = await service.ScanAsync(installation, null, CancellationToken.None);

        inventory.EmptyFolders.Should().BeEquivalentTo("telegram", "Reserved/Sub");
    }

    [Fact]
    public async Task ScanAsync_ToleratesMalformedJson()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        var modPath = fixture.CreateMod("Broken Mod", "{ invalid json", null);
        File.WriteAllText(Path.Combine(modPath, "default_mod.json"), "{ }");
        fixture.WriteSortOrder(("Broken Mod", "Review/Broken Mod"));

        var installation = new PenumbraInstallation(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.High,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());

        var service = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, new ProtectionService());
        var inventory = await service.ScanAsync(installation, null, CancellationToken.None);

        inventory.Mods.Should().ContainSingle();
        inventory.Mods[0].MalformedMetadataFiles.Should().Contain("meta.json");
        inventory.Mods[0].Warnings.Should().NotBeEmpty();
    }
}
