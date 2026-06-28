namespace PenumbraOrganizer.Core.Models;

public sealed record WorkbookCategoryDefinition(
    int Code,
    string Name,
    string Description,
    string ExampleDestination);

public sealed record WorkbookExportResult(
    string WorkbookPath,
    string SourceExportId,
    DateTimeOffset GeneratedAtUtc,
    string ScanIdentity,
    string InstallationIdentity,
    string StrategyLabel,
    int RowCount,
    string Summary);

public sealed record WorkbookImportRow(
    string StableScanId,
    string ModName,
    string Author,
    string CurrentVirtualFolder,
    string ModType,
    bool Protected,
    string Destination,
    string ResolvedModType,
    string? ResolvedDestination);

public sealed record WorkbookImportResult(
    string WorkbookPath,
    string SourceExportId,
    DateTimeOffset GeneratedAtUtc,
    string ScanIdentity,
    string InstallationIdentity,
    IReadOnlyList<WorkbookImportRow> Rows,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string Summary);

public static class WorkbookCategoryCatalog
{
    public const int CurrentFormatVersion = 1;
    public const string BlankDestinationRule = "Blank destination leaves the mod in its current folder.";
    public const string ReviewRule = "Use mod type Review or destination 7/Review when a row still needs manual attention.";

    public static IReadOnlyList<WorkbookCategoryDefinition> Definitions { get; } =
    [
        new(1, "Clothing", "Gear, outfits, dresses, tops, bottoms, shoes, and similar wearable mods.", "1/Bizu"),
        new(2, "Accessories", "Accessories, jewelry, horns, ears, tails, hats, and similar add-ons.", "2/Creator"),
        new(3, "Bodies", "Body replacements, body scales, body shapes, hands, and feet.", "3/Creator"),
        new(4, "Skin", "Skin textures, makeup, tattoos, freckles, scales, and similar appearance overlays.", "4/Creator"),
        new(5, "VFX and Animation", "Animations, VFX, emotes, action swaps, PAP/TMB/AVFX content.", "5/Creator"),
        new(6, "Minions", "Minions, companions, and pet-style cosmetic mods.", "6/Creator"),
        new(7, "Review", "Rows that need a human decision because the type or destination still needs attention.", "7/Review"),
        new(8, "Others", "Anything valid that does not fit the broader categories cleanly.", "8/Creator"),
    ];

    public static WorkbookCategoryDefinition Detect(ModScanResult mod)
    {
        var haystack = string.Join(
            ' ',
            new[]
            {
                mod.Name,
                mod.Author,
                mod.Description,
                mod.ContentSignalSummary,
            }.Concat(mod.Tags)).ToLowerInvariant();

        if (ContainsAny(haystack, "dress", "outfit", "cloth", "clothing", "heels", "shoe", "boots", "top", "bottom", "jacket", "shirt", "skirt", "pants"))
            return GetRequiredByCode(1);

        if (ContainsAny(haystack, "accessory", "horn", "ear", "tail", "glasses", "jewelry", "ring", "bracelet", "necklace", "hat", "piercing"))
            return GetRequiredByCode(2);

        if (ContainsAny(haystack, "body", "bibo", "gen3", "tbse", "bodyscale", "bodyscale", "hands", "feet", "torso", "muscle"))
            return GetRequiredByCode(3);

        if (ContainsAny(haystack, "skin", "makeup", "tattoo", "freckle", "scale", "body paint", "face texture", "face"))
            return GetRequiredByCode(4);

        if (ContainsAny(haystack, "animation", "anim", "pap", "avfx", "vfx", "tmb", "emote", "pose", "action"))
            return GetRequiredByCode(5);

        if (ContainsAny(haystack, "minion", "companion", "pet"))
            return GetRequiredByCode(6);

        if (mod.Warnings.Count > 0 || string.IsNullOrWhiteSpace(mod.Author))
            return GetRequiredByCode(7);

        return GetRequiredByCode(8);
    }

    public static bool TryGetByCode(int code, out WorkbookCategoryDefinition category)
    {
        category = Definitions.FirstOrDefault(item => item.Code == code) ?? GetRequiredByCode(8);
        return Definitions.Any(item => item.Code == code);
    }

    public static bool TryGetByName(string name, out WorkbookCategoryDefinition category)
    {
        category = Definitions.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? GetRequiredByCode(8);
        return Definitions.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static WorkbookCategoryDefinition GetRequiredByCode(int code)
        => Definitions.First(item => item.Code == code);

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));
}
