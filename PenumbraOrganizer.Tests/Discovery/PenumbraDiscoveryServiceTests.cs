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

    // Regression: manually browsing to Penumbra.json (ChoosePenumbraConfigAsync) only ever supplies
    // a config path, never a plugin assembly path. Without deriving it from the config's base
    // directory, PluginAssemblyPath stayed null even though the installed plugin (and LiteDB.dll)
    // was sitting right there — silently breaking the ModDataDb backend for anyone who had to
    // browse manually, e.g. a Linux/Proton setup where auto-discovery didn't find the install.
    [Fact]
    public async Task ValidateManualSelectionAsync_ResolvesPluginAssemblyFromConfigPathWhenNotProvided()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteMainConfig();
        fixture.WritePluginManifest();

        var service = new PenumbraDiscoveryService(NullLogger<PenumbraDiscoveryService>.Instance);
        var result = await service.ValidateManualSelectionAsync(fixture.PenumbraJsonPath, null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.PluginAssemblyPath.Should().Be(fixture.PluginAssemblyPath);
        result.InstalledVersion.Should().Be("1.6.1.10");
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
}
