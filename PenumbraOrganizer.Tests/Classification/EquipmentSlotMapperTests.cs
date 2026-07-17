namespace PenumbraOrganizer.Tests.Classification;

using FluentAssertions;
using PenumbraOrganizer.Core.Classification;

public sealed class EquipmentSlotMapperTests
{
    [Theory]
    [InlineData("met", EquipmentSlot.Head)]
    [InlineData("top", EquipmentSlot.Top)]
    [InlineData("glv", EquipmentSlot.Hands)]
    [InlineData("dwn", EquipmentSlot.Legs)]
    [InlineData("sho", EquipmentSlot.Feet)]
    [InlineData("ear", EquipmentSlot.Ears)]
    [InlineData("nek", EquipmentSlot.Neck)]
    [InlineData("wrs", EquipmentSlot.Wrists)]
    [InlineData("ril", EquipmentSlot.Rings)]
    [InlineData("rir", EquipmentSlot.Rings)]
    [InlineData("MET", EquipmentSlot.Head)] // case-insensitive
    public void MapPathSuffix_RecognizedSuffix_ReturnsExpectedSlot(string suffix, EquipmentSlot expected)
    {
        EquipmentSlotMapper.MapPathSuffix(suffix).Should().Be(expected);
    }

    [Fact]
    public void MapPathSuffix_UnrecognizedSuffix_ReturnsNull()
    {
        EquipmentSlotMapper.MapPathSuffix("xyz").Should().BeNull();
    }

    [Theory]
    [InlineData("Head", EquipmentSlot.Head)]
    [InlineData("Body", EquipmentSlot.Top)] // Penumbra's "Body" manipulation slot means torso equipment
    [InlineData("Hands", EquipmentSlot.Hands)]
    [InlineData("Legs", EquipmentSlot.Legs)]
    [InlineData("Feet", EquipmentSlot.Feet)]
    [InlineData("Ears", EquipmentSlot.Ears)]
    [InlineData("Neck", EquipmentSlot.Neck)]
    [InlineData("Wrists", EquipmentSlot.Wrists)]
    [InlineData("RFinger", EquipmentSlot.Rings)]
    [InlineData("LFinger", EquipmentSlot.Rings)]
    public void MapManipulationSlot_RecognizedSlot_ReturnsExpectedSlot(string slotName, EquipmentSlot expected)
    {
        EquipmentSlotMapper.MapManipulationSlot(slotName).Should().Be(expected);
    }

    [Fact]
    public void MapManipulationSlot_CustomizationSlot_ReturnsNull()
    {
        // "Hair"/"Face" are real Manipulations[].Slot values too, but for customization (Est
        // manipulations), not equipment — must not be mistaken for an equipment slot.
        EquipmentSlotMapper.MapManipulationSlot("Hair").Should().BeNull();
        EquipmentSlotMapper.MapManipulationSlot("Face").Should().BeNull();
    }

    // Real filename shapes confirmed against two independent real mod libraries (~2,280 mods
    // combined) via a validation script — "last underscore" extraction fails on all but the
    // first of these; the token-search regex must match "sho" in every one.
    [Theory]
    [InlineData("c0101e6116_sho.mdl")]
    [InlineData("v01_c0101e6116_sho_m.tex")]
    [InlineData("mt_c0101e6116_sho_a.mtrl")]
    [InlineData("c0101e6116_sho_b_d.tex")] // two trailing tokens
    [InlineData("mt_c0101e6116_sho_b.mtrl")]
    public void ExtractRawSuffixToken_RealFilenameShapes_ExtractsSho(string fileName)
    {
        EquipmentSlotMapper.ExtractRawSuffixToken(fileName).Should().Be("sho");
    }

    [Fact]
    public void ExtractRawSuffixToken_NoKnownToken_ReturnsNull()
    {
        EquipmentSlotMapper.ExtractRawSuffixToken("c0101e6116_xyz.mdl").Should().BeNull();
    }

    [Fact]
    public void ExtractRawSuffixToken_NoUnderscoreAtAll_ReturnsNull()
    {
        EquipmentSlotMapper.ExtractRawSuffixToken("w0101b0117.mdl").Should().BeNull();
    }

    [Fact]
    public void ExtractSlotFromFileName_RealFilenameWithTrailingTokens_ResolvesFeet()
    {
        EquipmentSlotMapper.ExtractSlotFromFileName("c0101e6116_sho_b_d.tex").Should().Be(EquipmentSlot.Feet);
    }

    [Fact]
    public void ExtractSlotFromFileName_UnrecognizedToken_ReturnsNull()
    {
        EquipmentSlotMapper.ExtractSlotFromFileName("c0101e6116_xyz.mdl").Should().BeNull();
    }

    [Theory]
    [InlineData(EquipmentSlot.Head, "Head")]
    [InlineData(EquipmentSlot.Top, "Top")]
    [InlineData(EquipmentSlot.Rings, "Rings")]
    public void FolderName_ReturnsExpectedFriendlyName(EquipmentSlot slot, string expected)
    {
        EquipmentSlotMapper.FolderName(slot).Should().Be(expected);
    }
}
