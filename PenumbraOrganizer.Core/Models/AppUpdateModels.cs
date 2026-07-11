namespace PenumbraOrganizer.Core.Models;

public sealed record AppUpdatePrepareResult(bool Success, string? ExtractedFolderPath, string? ErrorMessage);
