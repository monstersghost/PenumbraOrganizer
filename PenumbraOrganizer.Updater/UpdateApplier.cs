namespace PenumbraOrganizer.Updater;

public sealed record UpdateApplyResult(bool Success, string? ErrorMessage);

public static class UpdateApplier
{
    private const string MainExeName = "PenumbraOrganizer.exe";

    public static UpdateApplyResult Apply(string sourceDirectory, string destinationDirectory)
    {
        var destExePath = Path.Combine(destinationDirectory, MainExeName);
        var backupExePath = destExePath + ".old";
        var renamedBackup = false;

        try
        {
            if (File.Exists(destExePath))
            {
                if (File.Exists(backupExePath))
                    File.Delete(backupExePath);
                File.Move(destExePath, backupExePath);
                renamedBackup = true;
            }

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                var destFile = Path.Combine(destinationDirectory, relativePath);
                var destFileDirectory = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDirectory))
                    Directory.CreateDirectory(destFileDirectory);
                File.Copy(sourceFile, destFile, overwrite: true);
            }

            if (File.Exists(backupExePath))
                File.Delete(backupExePath);

            if (Directory.Exists(sourceDirectory))
                Directory.Delete(sourceDirectory, recursive: true);

            return new UpdateApplyResult(true, null);
        }
        catch (Exception ex)
        {
            if (renamedBackup && File.Exists(backupExePath) && !File.Exists(destExePath))
            {
                try
                {
                    File.Move(backupExePath, destExePath);
                }
                catch
                {
                    // Best-effort restore — the original failure message below is more useful
                    // to the user than a secondary failure from the restore attempt itself.
                }
            }

            return new UpdateApplyResult(false, ex.Message);
        }
    }
}
