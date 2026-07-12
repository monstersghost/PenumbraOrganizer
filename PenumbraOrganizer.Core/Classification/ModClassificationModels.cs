namespace PenumbraOrganizer.Core.Classification;

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
