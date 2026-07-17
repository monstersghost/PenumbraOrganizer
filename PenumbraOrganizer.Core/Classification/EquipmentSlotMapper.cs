using System.Text.RegularExpressions;

namespace PenumbraOrganizer.Core.Classification;

public static class EquipmentSlotMapper
{
    // Matches a known slot code as a delimited token anywhere in a filename, not just as the
    // final segment — "text after the last underscore" fails on real texture/material
    // filenames with a trailing token (e.g. "..._sho_b_d.tex"), confirmed against two
    // independent real mod libraries (~2,280 mods combined) before this was written.
    private static readonly Regex SlotTokenPattern = new(
        @"(?:^|_)(met|top|glv|dwn|sho|ear|nek|wrs|ril|rir)(?:_|\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Extracts the raw, lowercase slot token from a filename (e.g. "sho"). Returns null if no
    // known slot token appears anywhere. This is the raw-string shape ModPathClassifier's own
    // Subcategory field already exposes and has real, passing tests pinning it to — kept
    // separate from the enum-returning methods below so both consumers get the shape they
    // actually need.
    public static string? ExtractRawSuffixToken(string fileName)
    {
        var match = SlotTokenPattern.Match(fileName);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    // Maps a raw file-path suffix from a chara/equipment or chara/accessory path
    // (e.g. "top" from ".../c0101e0755_top.mdl") to its equipment slot.
    // Returns null for anything not a recognized equipment/accessory suffix.
    public static EquipmentSlot? MapPathSuffix(string suffix) => suffix.ToLowerInvariant() switch
    {
        "met" => EquipmentSlot.Head,
        "top" => EquipmentSlot.Top,
        "glv" => EquipmentSlot.Hands,
        "dwn" => EquipmentSlot.Legs,
        "sho" => EquipmentSlot.Feet,
        "ear" => EquipmentSlot.Ears,
        "nek" => EquipmentSlot.Neck,
        "wrs" => EquipmentSlot.Wrists,
        "ril" or "rir" => EquipmentSlot.Rings,
        _ => null,
    };

    // Composes the two methods above: "what slot does this file belong to," as a typed
    // EquipmentSlot rather than a raw string.
    public static EquipmentSlot? ExtractSlotFromFileName(string fileName) =>
        ExtractRawSuffixToken(fileName) is { } suffix ? MapPathSuffix(suffix) : null;

    // Maps a raw Manipulations[].Slot value (Penumbra's own internal slot names) to the same
    // slot. Note: Penumbra's own slot literally named "Body" means torso equipment here —
    // deliberately mapped to Top, not this plugin's unrelated Category.Body (Smallclothes/skin
    // bucket), to avoid conflating the two.
    public static EquipmentSlot? MapManipulationSlot(string slotName) => slotName switch
    {
        "Head" => EquipmentSlot.Head,
        "Body" => EquipmentSlot.Top,
        "Hands" => EquipmentSlot.Hands,
        "Legs" => EquipmentSlot.Legs,
        "Feet" => EquipmentSlot.Feet,
        "Ears" => EquipmentSlot.Ears,
        "Neck" => EquipmentSlot.Neck,
        "Wrists" => EquipmentSlot.Wrists,
        "RFinger" or "LFinger" => EquipmentSlot.Rings,
        _ => null,
    };

    public static string FolderName(EquipmentSlot slot) => slot.ToString();
}
