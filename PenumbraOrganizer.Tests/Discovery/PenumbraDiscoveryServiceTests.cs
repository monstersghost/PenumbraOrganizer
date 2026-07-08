namespace PenumbraOrganizer.Tests.Discovery;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Discovery;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class PenumbraDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_FindsInstallationFromKnownBasePath()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance, new[] { fixture.BasePath });
        var result = await service.DiscoverAsync(CancellationToken.None);

        result.Installations.Should().ContainSingle();
        result.Installations[0].ModRoot.Should().Be(fixture.ModRoot);
        result.Installations[0].Confidence.Should().Be(DiscoveryConfidence.High);
        result.Installations[0].InstalledVersion.Should().Be("1.6.1.10");
    }

    [Fact]
    public async Task ValidateManualSelectionAsync_ReturnsNullForMissingStructure()
    {
        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var result = await service.ValidateManualSelectionAsync(@"C:\missing\Penumbra.json", null, null, CancellationToken.None);
        result.Should().BeNull();
    }

    // Linux/Wine support: a Wine-style config path that does not resolve is now treated like any
    // other missing path (null), rather than being fabricated into a "not supported in version 1"
    // installation as it was before the refusal was removed.
    [Fact]
    public async Task ValidateManualSelectionAsync_WinePath_NoLongerFabricatesUnsupportedResult()
    {
        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var result = await service.ValidateManualSelectionAsync(
            @"Z:\home\deck\.xlcore\pluginConfigs\Penumbra.json", null, null, CancellationToken.None);
        result.Should().BeNull();
    }

    // Linux/Wine support: a POSIX mod root from a Linux Penumbra.json is mapped onto the Wine Z:
    // drive when this app runs under Wine (Windows runtime), and no Wine-unsupported warning is
    // emitted. Asserted on Windows, which is where this WPF app (and its Wine host) runs.
    [Fact]
    public async Task DiscoverAsync_NormalizesPosixModRootToWineDrive()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig("/home/deck/Mods");
        fixture.WritePluginManifest();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance, new[] { fixture.BasePath });
        var result = await service.DiscoverAsync(CancellationToken.None);

        result.Installations.Should().ContainSingle();
        result.Installations[0].ModRoot.Should().Be(@"Z:\home\deck\Mods");
        result.Installations[0].Warnings.Should().NotContain(warning => warning.Contains("version 1"));
    }

    [Fact]
    public void ResolveConfigPathFromFolder_FindsPenumbraJsonInPluginConfigsFolder()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var resolved = service.ResolveConfigPathFromFolder(fixture.PluginConfigsPath);

        resolved.Should().Be(fixture.PenumbraJsonPath);
    }

    [Fact]
    public void ResolveConfigPathFromFolder_FindsPenumbraJsonWhenPointedAtXivLauncherBaseFolder()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var resolved = service.ResolveConfigPathFromFolder(fixture.BasePath);

        resolved.Should().Be(fixture.PenumbraJsonPath);
    }

    [Fact]
    public void ResolveConfigPathFromFolder_FindsPenumbraJsonWhenPointedAtPenumbraSubfolder()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var resolved = service.ResolveConfigPathFromFolder(fixture.PenumbraConfigPath);

        resolved.Should().Be(fixture.PenumbraJsonPath);
    }

    [Fact]
    public void ResolveConfigPathFromFolder_ReturnsNullWhenNothingFound()
    {
        using var fixture = new TemporaryPenumbraFixture();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var resolved = service.ResolveConfigPathFromFolder(fixture.RootPath);

        resolved.Should().BeNull();
    }
}
