namespace PenumbraOrganizer.Infrastructure.Recovery;

internal sealed class RecoveryStorageLayout
{
    private const string ProductFolderName = "PenumbraOrganizer";
    private const string BackupsFolderName = "Backups";

    public RecoveryStorageLayout(string? backupsRoot = null)
    {
        BackupsRoot = backupsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName,
            BackupsFolderName);
        HistoryIndexPath = Path.Combine(BackupsRoot, "history-index.json");
    }

    public string BackupsRoot { get; }
    public string HistoryIndexPath { get; }

    public string GetOperationDirectory(Guid operationId)
        => Path.Combine(BackupsRoot, operationId.ToString("N"));

    public string GetFilesDirectory(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "files");

    public string GetLogsDirectory(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "logs");

    public string GetOperationPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "operation.json");

    public string GetManifestPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "manifest.json");

    public string GetPlanPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "plan.json");

    public string GetApplyPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "apply.json");

    public string GetRollbackPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "rollback.json");

    public string GetVerificationPath(Guid operationId)
        => Path.Combine(GetOperationDirectory(operationId), "verification.json");

    public void EnsureOperationDirectories(Guid operationId)
    {
        Directory.CreateDirectory(GetOperationDirectory(operationId));
        Directory.CreateDirectory(GetFilesDirectory(operationId));
        Directory.CreateDirectory(GetLogsDirectory(operationId));
    }

    public string BuildRelativeBackupPath(string targetPath, int index)
    {
        var fullPath = ValidateAbsoluteTargetPath(targetPath);
        var segments = new List<string> { "files", index.ToString("D4") };

        if (fullPath.Length >= 2 && fullPath[1] == ':')
        {
            segments.Add($"drive-{char.ToUpperInvariant(fullPath[0])}");
            segments.AddRange(fullPath[3..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        }
        else if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            segments.Add("unc");
            segments.AddRange(fullPath.TrimStart('\\').Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            segments.AddRange(fullPath.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        }

        return string.Join('/', segments.Select(SanitizeRelativeSegment));
    }

    public string ResolveBackupFilePath(Guid operationId, string relativeBackupPath)
    {
        if (string.IsNullOrWhiteSpace(relativeBackupPath))
            throw new InvalidOperationException("The backup path is required.");
        if (Path.IsPathRooted(relativeBackupPath))
            throw new InvalidOperationException("Backup paths must remain relative to the operation package.");

        var normalized = relativeBackupPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("Backup paths cannot escape the operation package.");

        var operationDirectory = Path.GetFullPath(GetOperationDirectory(operationId));
        var combined = Path.GetFullPath(Path.Combine(operationDirectory, normalized));
        var allowedPrefix = operationDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup paths cannot escape the operation package.");

        return combined;
    }

    public static string ValidateAbsoluteTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("Target paths must be absolute.");
        if (!Path.IsPathRooted(targetPath))
            throw new InvalidOperationException("Target paths must be absolute.");

        var normalized = targetPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Target paths must have a valid root.");

        var relativePortion = normalized[root.Length..];
        var segments = relativePortion.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("Target path traversal is not allowed.");

        return Path.GetFullPath(normalized);
    }

    private static string SanitizeRelativeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "_";

        return segment
            .Replace(':', '_')
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
    }
}
