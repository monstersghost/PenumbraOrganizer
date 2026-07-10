namespace PenumbraOrganizer.Updater;

public sealed record UpdateApplyResult(bool Success, string? ErrorMessage);

public static class UpdateApplier
{
    public static UpdateApplyResult Apply(string sourceDirectory, string destinationDirectory)
    {
        var backupDirectory = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".old";
        var movedDestinationAside = false;

        try
        {
            // Clear any stale backup left behind by a previous failed/interrupted update.
            // If this doesn't fully succeed, stop here -- do NOT proceed to Directory.Move
            // below (it would collide with the stale backup), and do not set
            // movedDestinationAside, since nothing has touched the working install yet.
            TryDeleteDirectory(backupDirectory);
            if (Directory.Exists(backupDirectory))
                return new UpdateApplyResult(false, "Could not clear a stale backup from a previous update attempt.");

            // Whole-directory swap: move the current install aside first, so a failure
            // partway through the copy loop leaves either the fully-old or fully-new
            // install, never a mix.
            Directory.Move(destinationDirectory, backupDirectory);
            movedDestinationAside = true;
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
            // a reported failure.
            TryDeleteDirectory(backupDirectory);
            TryDeleteDirectory(sourceDirectory);

            return new UpdateApplyResult(true, null);
        }
        catch (Exception ex)
        {
            // Only restore if THIS call actually moved the destination aside -- otherwise
            // the working install was never touched and must be left exactly as it was.
            if (movedDestinationAside)
                RestoreBackup(destinationDirectory, backupDirectory);

            return new UpdateApplyResult(false, ex.Message);
        }
    }

    private static void RestoreBackup(string destinationDirectory, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            return;

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
