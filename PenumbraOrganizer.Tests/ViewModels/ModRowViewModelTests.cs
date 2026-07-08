namespace PenumbraOrganizer.Tests.ViewModels;

using FluentAssertions;
using PenumbraOrganizer.App.ViewModels;
using PenumbraOrganizer.Core.Classification;
using PenumbraOrganizer.Core.Models;

public sealed class ModRowViewModelTests
{
    [Theory]
    [InlineData("sho", "Boots and Sandals")]
    [InlineData("dwn", "Pants and Shorts")]
    [InlineData("top", "Tops")]
    [InlineData("met", "Hats and Glasses")]
    [InlineData("glv", "Gloves")]
    [InlineData("ear", "Jewelry and Accessories")]
    [InlineData("ril", "Jewelry and Accessories")]
    [InlineData(null, null)]
    [InlineData("body", null)]
    public void DetectedSubcategory_MapsRawSuffixToFriendlyName(string? rawSuffix, string? expected)
    {
        var mod = BuildScanResult(rawSuffix);

        var row = new ModRowViewModel(mod);

        row.DetectedSubcategory.Should().Be(expected);
    }

    private static ModScanResult BuildScanResult(string? detectedSubcategory)
        => new()
        {
            StableScanId = "Test",
            PhysicalDirectory = @"C:\Mods\Test",
            PhysicalDirectoryName = "Test",
            CurrentVirtualFolder = string.Empty,
            Name = "Test Mod",
            DetectedCategory = ModCategory.Gear,
            DetectedSubcategory = detectedSubcategory,
        };
}
