namespace PenumbraOrganizer.Core.Classification;

public static class ModPathClassifier
{
    private static readonly HashSet<string> GearManipulationSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Head", "Body", "Hands", "Legs", "Feet", "Ears", "Neck", "Wrists", "RFinger", "LFinger",
    };

    private static readonly HashSet<string> WeaponManipulationSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "MainHand", "OffHand",
    };

    // Rollup priority, highest first. A mod's overall category is the highest-priority category
    // found among any of its Targets.
    private static readonly IReadOnlyList<ModCategory> RollupPriority = new[]
    {
        ModCategory.Gear, ModCategory.Weapon, ModCategory.NPC, ModCategory.Face, ModCategory.Hair,
        ModCategory.Body, ModCategory.Skin, ModCategory.Minion, ModCategory.Mount, ModCategory.Pet,
        ModCategory.Ornament, ModCategory.Furniture, ModCategory.VFX, ModCategory.Sound,
        ModCategory.Animation, ModCategory.Others,
    };

    public static IReadOnlyList<ModTargetClassification> Classify(IReadOnlyList<string> contentPaths)
        => contentPaths.Select(ClassifyPath).ToArray();

    public static (ModCategory Category, string? Subcategory) Resolve(IReadOnlyList<ModTargetClassification> targets)
    {
        foreach (var category in RollupPriority)
        {
            var match = targets.FirstOrDefault(t => t.Category == category);
            if (match is not null)
                return (category, match.DerivedSlotName);
        }

        return (ModCategory.Others, null);
    }

    private static ModTargetClassification ClassifyPath(string path)
    {
        if (path.StartsWith("slot:", StringComparison.OrdinalIgnoreCase))
        {
            var slotName = path["slot:".Length..];
            return new ModTargetClassification(
                CanonicalTargetKind.MetaManipulation,
                ClassifyManipulationSlot(slotName),
                slotName,
                GameTarget: null,
                Notes: null);
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var target = BuildCanonicalTarget(normalized);
        var (category, subcategory, notes) = ClassifyGamePath(normalized, target);
        return new ModTargetClassification(CanonicalTargetKind.GameFile, category, subcategory, target, notes);
    }

    private static ModCategory ClassifyManipulationSlot(string slotName)
    {
        if (GearManipulationSlots.Contains(slotName))
            return ModCategory.Gear;
        if (WeaponManipulationSlots.Contains(slotName))
            return ModCategory.Weapon;
        return ModCategory.Others;
    }

    private static (ModCategory Category, string? Subcategory, string? Notes) ClassifyGamePath(string path, CanonicalGameTarget target)
    {
        if (target.Root.Equals("chara/equipment", StringComparison.OrdinalIgnoreCase)
            || target.Root.Equals("chara/accessory", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.Gear, target.Suffix, null);

        if (target.Root.Equals("chara/weapon", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.Weapon, target.Suffix, null);

        if (target.Root.Equals("chara/human", StringComparison.OrdinalIgnoreCase))
        {
            var humanResult = TryClassifyHumanCustomizationPath(path, target);
            if (humanResult is not null)
                return humanResult.Value;
        }

        if (target.Root.Equals("chara/monster", StringComparison.OrdinalIgnoreCase)
            || target.Root.Equals("chara/demihuman", StringComparison.OrdinalIgnoreCase))
            return ClassifyCreaturePath(target);

        if (path.StartsWith("bgcommon/hou/", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.Furniture, null, null);

        if (path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/vfx/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("vfx/", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.VFX, null, null);

        if (path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.Sound, null, null);

        if (path.EndsWith(".pap", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/animation/", StringComparison.OrdinalIgnoreCase))
            return (ModCategory.Animation, null, null);

        return (ModCategory.Others, null, null);
    }

    // Returns null when the path is under chara/human/ but isn't a recognized obj/ customization
    // subfolder (e.g. an animation path) — the caller falls through to the generic pattern checks.
    private static (ModCategory Category, string? Subcategory, string? Notes)? TryClassifyHumanCustomizationPath(string path, CanonicalGameTarget target)
    {
        var raceCode = target.PrimaryId?.TrimStart('c');
        if (raceCode is not null && IsNpcRaceCode(raceCode))
            return (ModCategory.NPC, null, null);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 || !segments[3].Equals("obj", StringComparison.OrdinalIgnoreCase))
            return null;

        return segments[4].ToLowerInvariant() switch
        {
            "face" => (ModCategory.Face, "face", null),
            "hair" => (ModCategory.Hair, "hair", null),
            "tail" => (ModCategory.Body, "tail", null),
            "zear" => (ModCategory.Body, "zear", null),
            "body" => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
                ? (ModCategory.Body, "body", null)
                : (ModCategory.Skin, "body", null),
            _ => null,
        };
    }

    // Playable race codes end "01" (0101 Hyur Midlander Male, 0201 Hyur Midlander Female, ...
    // 1801 Viera Female); every one has a matching NPC-only code ending "04". Plus two generic
    // NPC buckets, 9104 (NPC_Male) and 9204 (NPC_Female). Verified against xivModdingFramework's
    // XivRace enum during design (c1304 = AuRa_Male_NPC, c0804 = Miqote_Female_NPC).
    private static bool IsNpcRaceCode(string raceCode)
        => raceCode is "9104" or "9204" || raceCode.EndsWith("04", StringComparison.Ordinal);

    private static (ModCategory Category, string? Subcategory, string? Notes) ClassifyCreaturePath(CanonicalGameTarget target)
    {
        if (target.PrimaryId is not null && MonsterCategoryTable.TryGetCategory(target.PrimaryId, out var category))
            return (category, null, null);

        return (ModCategory.Others, null, $"{target.PrimaryId} not found in bundled monster ID table");
    }

    private static CanonicalGameTarget BuildCanonicalTarget(string normalizedPath)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var root = segments.Length >= 2 ? $"{segments[0]}/{segments[1]}" : segments.ElementAtOrDefault(0) ?? string.Empty;
        var primaryId = segments.Length >= 3 ? segments[2] : null;
        var fileName = segments.Length > 0 ? segments[^1] : normalizedPath;
        var suffix = EquipmentSlotMapper.ExtractRawSuffixToken(fileName);
        var secondaryId = segments.FirstOrDefault(IsSecondaryIdSegment);
        return new CanonicalGameTarget(normalizedPath, root, suffix, primaryId, secondaryId);
    }

    private static bool IsSecondaryIdSegment(string segment)
        => segment.Length > 1
           && segment[0] is 'b' or 'f' or 'h' or 't' or 'z'
           && segment[1..].All(char.IsDigit);

}
