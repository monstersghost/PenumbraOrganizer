namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;

public sealed class CreatorCanonicalizer : ICreatorCanonicalizer
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enni"] = "Enni",
        ["etherealsins"] = "EtherealSins",
        ["soft bun"] = "Soft Bun",
        ["soft bun "] = "Soft Bun",
        ["illy does things"] = "Soft Bun",
        ["illy does things "] = "Soft Bun",
        ["illy does things".ToUpperInvariant()] = "Soft Bun",
        ["nini"] = "Nini",
        ["_nini_"] = "Nini",
        ["hanzo"] = "Hanzo Dojo",
        ["hanzo dojo"] = "Hanzo Dojo",
        ["konekomods"] = "Koneko",
        ["koneko"] = "Koneko",
    };

    public string Canonicalize(string creatorName)
    {
        if (string.IsNullOrWhiteSpace(creatorName))
            return string.Empty;

        var trimmed = creatorName.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }
}
