namespace PenumbraOrganizer.Core.Models;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string? ErrorMessage);
