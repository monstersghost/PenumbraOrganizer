namespace PenumbraOrganizer.Core.Services;

public static class AppVersionComparer
{
    public static bool IsNewer(string currentVersion, string candidateTag)
    {
        var current = ParseNumericVersion(currentVersion);
        var candidate = ParseNumericVersion(candidateTag);
        if (current is null || candidate is null)
            return false;

        return candidate > current;
    }

    private static Version? ParseNumericVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim().TrimStart('v', 'V');
        var numericPart = trimmed.Split('-')[0];
        return Version.TryParse(numericPart, out var version) ? version : null;
    }
}
