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
}
