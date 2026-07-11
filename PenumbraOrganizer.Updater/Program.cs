// This file implements a self-relaunch dance before doing the actual file swap. This is
// load-bearing, not incidental complexity -- do NOT "simplify" it away. It was added after a
// real-install failure was found during the final whole-branch review: the whole-directory swap
// in UpdateApplier.Apply always failed on a real system, because this exe's own running image
// (and its inherited current working directory) locks the very directory Directory.Move needs to
// rename. This was empirically confirmed and the fix empirically validated against a real
// self-contained published build run as a real separate process against a real install
// directory -- see .superpowers/sdd/task-7-critical-fix-brief.md for the full repro.
using System.Diagnostics;
using PenumbraOrganizer.Updater;

const string StagedEnvVar = "PENUMBRA_UPDATER_STAGED";
const string LauncherPidEnvVar = "PENUMBRA_UPDATER_LAUNCHER_PID";

var options = CommandLineArgs.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: PenumbraOrganizer.Updater.exe --pid <pid> --source <dir> --dest <dir>");
    return 1;
}

// This process's own running image cannot live inside the directory it is about to rename --
// Directory.Move on `dest` fails while a self-contained single-file exe is running from
// within it (confirmed empirically against a real published build). Relaunch a copy of
// ourselves from a temp staging location first, so by the time the actual swap runs, nothing
// under `dest` is open by this process.
if (Environment.GetEnvironmentVariable(StagedEnvVar) != "1")
{
    var currentExePath = Process.GetCurrentProcess().MainModule!.FileName!;
    var stagingDir = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerUpdaterStaging", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stagingDir);
    var stagedExePath = Path.Combine(stagingDir, Path.GetFileName(currentExePath));
    File.Copy(currentExePath, stagedExePath, overwrite: true);

    var stagedStartInfo = new ProcessStartInfo(stagedExePath)
    {
        UseShellExecute = false,
        // A child process inherits its parent's current working directory by default. If that
        // were still `dest` (e.g. because this process itself was launched with `dest` as its
        // CWD), the staged copy would hold the same implicit directory lock this whole
        // relaunch exists to avoid. Force it away from `dest` explicitly.
        WorkingDirectory = stagingDir,
    };
    foreach (var arg in args)
        stagedStartInfo.ArgumentList.Add(arg);
    stagedStartInfo.Environment[StagedEnvVar] = "1";
    // Process.Start doesn't wait for the child to exit -- the staged copy must explicitly wait
    // for THIS process to fully exit (not just assume it has) before touching `dest`, since this
    // process's own image/CWD lock on `dest` isn't released until it actually exits.
    stagedStartInfo.Environment[LauncherPidEnvVar] = Environment.ProcessId.ToString();

    Process.Start(stagedStartInfo);
    return 0;
}

var launcherPidRaw = Environment.GetEnvironmentVariable(LauncherPidEnvVar);
if (launcherPidRaw is not null && int.TryParse(launcherPidRaw, out var launcherPid))
{
    try
    {
        Process.GetProcessById(launcherPid).WaitForExit();
    }
    catch (ArgumentException)
    {
        // Already exited — nothing to wait for.
    }
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
