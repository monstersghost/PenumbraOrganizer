namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;

public sealed class ProtectionService : IProtectionService
{
    private static readonly string[] ProtectedPrefixes =
    {
        ".Character specific mods/Akako Main Files",
        ".Character specific mods/Aki Dotharl",
        ".Character specific mods/sculpts",
        ".Character specific mods/old",
    };

    public bool IsProtectedPath(string currentVirtualFolder)
        => ProtectedPrefixes.Any(prefix =>
            currentVirtualFolder.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || currentVirtualFolder.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            || currentVirtualFolder.Equals(".Character specific mods/Bodies", StringComparison.OrdinalIgnoreCase);
}
