namespace PenumbraOrganizer.Tests.Classification;

using FluentAssertions;
using PenumbraOrganizer.Core.Classification;

public sealed class HeliosphereModDetectorTests
{
    [Theory]
    [InlineData("hs-Bizu-Dress-1.0.0-abc123", true)]
    [InlineData("HS-Bizu-Dress-1.0.0-abc123", true)]
    [InlineData("Bizu Dress", false)]
    public void IsHeliosphereManaged_DetectsHsDirectoryPrefix(string directoryName, bool expected)
        => HeliosphereModDetector.IsHeliosphereManaged(directoryName, Array.Empty<string>()).Should().Be(expected);

    [Fact]
    public void IsHeliosphereManaged_DetectsHeliosphereJsonFallback_ForExternallyImportedMods()
        => HeliosphereModDetector.IsHeliosphereManaged("Bizu Dress", ["meta.json", "heliosphere.json"]).Should().BeTrue();

    [Fact]
    public void IsHeliosphereManaged_IsFalse_WhenNeitherSignalPresent()
        => HeliosphereModDetector.IsHeliosphereManaged("Bizu Dress", ["meta.json", "default_mod.json"]).Should().BeFalse();
}
