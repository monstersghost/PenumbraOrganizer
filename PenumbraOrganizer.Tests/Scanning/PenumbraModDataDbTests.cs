namespace PenumbraOrganizer.Tests.Scanning;

using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class PenumbraModDataDbTests
{
    [Fact]
    public void Load_FileMissing_ReturnsNotFound()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();

        var result = PenumbraModDataDb.Load(fixture.PenumbraConfigPath, BuildInstallation(fixture));

        result.Status.Should().Be(PenumbraModDataDbLoadStatus.NotFound);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Load_EngineMissing_ReturnsEngineUnavailable()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.WriteModDataDb(("Mod A", "Clothing"));
        // Deliberately do NOT call CopyRealLiteDbAssembly() - no LiteDB.dll next to the plugin.

        var result = PenumbraModDataDb.Load(fixture.PenumbraConfigPath, BuildInstallation(fixture));

        result.Status.Should().Be(PenumbraModDataDbLoadStatus.EngineUnavailable);
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Load_ValidFile_ReadsFolderAndLocalData()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CopyRealLiteDbAssembly();
        fixture.WriteModDataDb(
            ("Mod A", "Clothing/Hats", true, new[] { "cute", "wip" }, "keep me"),
            ("Mod B", "", false, Array.Empty<string>(), string.Empty));

        var result = PenumbraModDataDb.Load(fixture.PenumbraConfigPath, BuildInstallation(fixture));

        result.Status.Should().Be(PenumbraModDataDbLoadStatus.Success);
        result.Data!.GetFolderFor("Mod A").Should().Be("Clothing/Hats");

        var entryA = result.Data.GetEntry("Mod A");
        entryA!.Favorite.Should().BeTrue();
        entryA.LocalTags.Should().Equal("cute", "wip");
        entryA.Note.Should().Be("keep me");

        result.Data.GetFolderFor("Mod B").Should().BeEmpty();
        result.Data.GetFolderFor("Unknown Mod").Should().BeEmpty();
        result.Data.GetEntry("Unknown Mod").Should().BeNull();
    }

    [Fact]
    public void Load_FileLockedByAnotherProcess_ReturnsFailed()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();
        fixture.CopyRealLiteDbAssembly();
        fixture.WriteModDataDb(("Mod A", "Clothing"));

        // An exclusive external lock (not another LiteDB connection) should defeat
        // "Connection=Shared" and force a read failure, exercising the same defensive path a real
        // corrupt/inaccessible file would.
        using var exclusiveLock = new FileStream(fixture.ModDataDbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = PenumbraModDataDb.Load(fixture.PenumbraConfigPath, BuildInstallation(fixture));

        result.Status.Should().Be(PenumbraModDataDbLoadStatus.Failed);
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static PenumbraInstallation BuildInstallation(TemporaryPenumbraFixture fixture)
        => new(
            fixture.PenumbraJsonPath,
            fixture.PenumbraConfigPath,
            fixture.ModRoot,
            fixture.PluginAssemblyPath,
            fixture.PluginManifestPath,
            "1.6.1.10",
            DiscoveryConfidence.High,
            Array.Empty<DiscoveryEvidence>(),
            Array.Empty<string>());
}
