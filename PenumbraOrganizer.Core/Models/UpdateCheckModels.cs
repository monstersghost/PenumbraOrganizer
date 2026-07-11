namespace PenumbraOrganizer.Core.Models;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string? ErrorMessage,
    string? ZipDownloadUrl = null,
    string? ChecksumsDownloadUrl = null);
