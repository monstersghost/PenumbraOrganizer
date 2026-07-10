namespace PenumbraOrganizer.Updater;

public sealed record UpdateApplyResult(bool Success, string? ErrorMessage);

public static class UpdateApplier
{
    public static UpdateApplyResult Apply(string sourceDirectory, string destinationDirectory)
    {
        var backupDirectory = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".old";

        try
        {
            // Clear any stale backup left behind by a previous failed/interrupted update.
            TryDeleteDirectory(backupDirectory);

            // Whole-directory swap: move the current install aside first, so a failure
            // partway through the copy loop leaves either the fully-old or fully-new
            // install, never a mix -- and restoring is a single directory move, not
            // dependent on how far the copy loop got or what order it visited files in.
            Directory.Move(destinationDirectory, backupDirectory);
            Directory.CreateDirectory(destinationDirectory);

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                var destFile = Path.Combine(destinationDirectory, relativePath);
                var destFileDirectory = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDirectory))
                    Directory.CreateDirectory(destFileDirectory);
                File.Copy(sourceFile, destFile, overwrite: true);
            }

            // Cleanup is best-effort and must never turn an already-successful swap into
            // a reported failure -- a leftover backup or temp folder is harmless, unlike
            // reporting "update failed" and skipping the relaunch after it actually worked
            // (this is Finding 2 -- see below).
            TryDeleteDirectory(backupDirectory);
            TryDeleteDirectory(sourceDirectory);

            return new UpdateApplyResult(true, null);
        }
        catch (Exception ex)
        {
            RestoreBackup(destinationDirectory, backupDirectory);
            return new UpdateApplyResult(false, ex.Message);
        }
    }

    private static void RestoreBackup(string destinationDirectory, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            return; // Nothing to restore -- the swap never got far enough to move anything aside.

        try
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
            Directory.Move(backupDirectory, destinationDirectory);
        }
        catch
        {
            // Best-effort restore -- the original failure message is more useful to the
            // user than a secondary failure from the restore attempt itself.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup -- a leftover temp/backup folder is harmless.
        }
    }
}
