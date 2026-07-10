using System.Diagnostics;
using PenumbraOrganizer.Updater;

var options = CommandLineArgs.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: PenumbraOrganizer.Updater.exe --pid <pid> --source <dir> --dest <dir>");
    return 1;
}

try
{
    var process = Process.GetProcessById(options.ProcessId);
    process.WaitForExit();
}
catch (ArgumentException)
{
    // Already exited — nothing to wait for.
}

// Windows can briefly hold a file handle open for a moment after a process exits.
await Task.Delay(TimeSpan.FromMilliseconds(500));

var result = UpdateApplier.Apply(options.SourceDirectory, options.DestinationDirectory);

var logPath = Path.Combine(options.DestinationDirectory, "update-log.txt");
File.WriteAllText(logPath, result.Success
    ? $"Update applied successfully at {DateTimeOffset.UtcNow:O}."
    : $"Update failed at {DateTimeOffset.UtcNow:O}: {result.ErrorMessage}");

if (!result.Success)
    return 1;

var exePath = Path.Combine(options.DestinationDirectory, "PenumbraOrganizer.exe");
try
{
    Process.Start(new ProcessStartInfo(exePath)
    {
        UseShellExecute = true,
        WorkingDirectory = options.DestinationDirectory,
    });
}
catch (Exception ex)
{
    File.AppendAllText(logPath, $"{Environment.NewLine}Relaunch failed at {DateTimeOffset.UtcNow:O}: {ex.Message}");
}

return 0;
