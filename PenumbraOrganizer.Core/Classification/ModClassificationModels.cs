namespace PenumbraOrganizer.Core.Classification;

public enum ModCategory
{
    Gear = 1,
    Weapon = 2,
    Face = 3,
    Hair = 4,
    Body = 5,
    Skin = 6,
    NPC = 7,
    Minion = 8,
    Mount = 9,
    Pet = 10,
    Ornament = 11,
    Furniture = 12,
    VFX = 13,
    Sound = 14,
    Animation = 15,
    Others = 16,
}

public enum CanonicalTargetKind
{
    GameFile,
    MetaManipulation,
}

public sealed record CanonicalGameTarget(
    string GamePath,
    string Root,
    string? Suffix,
    string? PrimaryId,
    string? SecondaryId);

public sealed record ModTargetClassification(
    CanonicalTargetKind TargetKind,
    ModCategory Category,
    string? DerivedSlotName,
    CanonicalGameTarget? GameTarget,
    string? Notes);
