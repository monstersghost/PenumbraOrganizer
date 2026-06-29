namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Diagnostics;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Recovery;

public sealed class WritePermissionPreflightService : IWritePermissionPreflightService
{
    private static readonly string[] BlockingProcessNames =
    [
        "ffxiv",
        "ffxiv_dx11",
        "ffxivlauncher",
        "ffxivboot",
        "XIVLauncher",
        "XIVLauncherCN",
        "Dalamud.Injector",
        "Dalamud.Boot"
    ];

    private readonly RecoveryStorageLayout _layout;
    private readonly Func<IReadOnlyList<string>> _processProvider;
    private readonly Func<IReadOnlyList<string>, long?> _freeSpaceProvider;

    public WritePermissionPreflightService()
        : this(new RecoveryStorageLayout(), GetBlockingProcesses, GetAvailableBytes)
    {
    }

    public WritePermissionPreflightService(string backupsRoot)
        : this(new RecoveryStorageLayout(backupsRoot), GetBlockingProcesses, GetAvailableBytes)
    {
    }

    public WritePermissionPreflightService(
        string backupsRoot,
        Func<IReadOnlyList<string>> processProvider,
        Func<IReadOnlyList<string>, long?> freeSpaceProvider)
        : this(new RecoveryStorageLayout(backupsRoot), processProvider, freeSpaceProvider)
    {
    }

    internal WritePermissionPreflightService(
        RecoveryStorageLayout layout,
        Func<IReadOnlyList<string>> processProvider,
        Func<IReadOnlyList<string>, long?> freeSpaceProvider)
    {
        _layout = layout;
        _processProvider = processProvider;
        _freeSpaceProvider = freeSpaceProvider;
    }

    public Task<WritePermissionPreflightResult> CheckAsync(
        DryRunPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checks = new List<WritePermissionCheckItem>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var requiredBytes = plan.SourceFiles.Sum(file => file.Length) + plan.FileChanges.Sum(change => change.ExpectedLength);

        foreach (var fileChange in plan.FileChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = RecoveryStorageLayout.ValidateAbsoluteTargetPath(fileChange.TargetPath);
            if (!File.Exists(targetPath))
            {
                errors.Add($"The authoritative write target is missing: {targetPath}");
                checks.Add(new WritePermissionCheckItem(targetPath, "Readable", WritePermissionStatus.Blocked, "The authoritative target file is missing."));
                continue;
            }

            checks.Add(ProbeReadable(targetPath));
            checks.Add(ProbeReadOnly(targetPath));
            checks.Add(ProbeExclusiveLock(targetPath));
            checks.Add(ProbeAtomicSupport(targetPath, fileChange.AtomicReplaceSupported));

            var parent = Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("Every target path must have a parent directory.");
            checks.Add(ProbeTempFile(parent, targetPath));
        }

        Directory.CreateDirectory(_layout.BackupsRoot);
        checks.Add(ProbeTempFile(_layout.BackupsRoot, _layout.BackupsRoot));

        var blockingProcesses = _processProvider();
        if (blockingProcesses.Count > 0)
        {
            warnings.Add("Close FFXIV and XIVLauncher before applying virtual-folder changes.");
            errors.Add($"Apply is blocked while these processes are running: {string.Join(", ", blockingProcesses)}");
        }

        foreach (var check in checks.Where(check => check.Status == WritePermissionStatus.Blocked))
            errors.Add($"{check.Target}: {check.Message}");

        long? availableBytes = null;
        try
        {
            var driveRoots = plan.FileChanges
                .Select(change => Path.GetPathRoot(change.TargetPath))
                .Append(Path.GetPathRoot(_layout.BackupsRoot))
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            availableBytes = _freeSpaceProvider(driveRoots!);

            if (availableBytes < requiredBytes)
                errors.Add($"Not enough free disk space for backup and temporary output. Required {requiredBytes} bytes but only {availableBytes} bytes are available.");
        }
        catch (Exception ex)
        {
            warnings.Add("Available disk space could not be confirmed: " + ex.Message);
        }

        return Task.FromResult(new WritePermissionPreflightResult(
            Succeeded: errors.Count == 0,
            Checks: checks,
            BlockingProcesses: blockingProcesses,
            RequiredBytes: requiredBytes,
            AvailableBytes: availableBytes,
            Errors: errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static WritePermissionCheckItem ProbeReadable(string targetPath)
    {
        try
        {
            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new WritePermissionCheckItem(targetPath, "Readable", WritePermissionStatus.Passed, "The authoritative target is readable.");
        }
        catch (Exception ex)
        {
            return new WritePermissionCheckItem(targetPath, "Readable", WritePermissionStatus.Blocked, ex.Message);
        }
    }

    private static WritePermissionCheckItem ProbeReadOnly(string targetPath)
    {
        try
        {
            var attributes = File.GetAttributes(targetPath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                return new WritePermissionCheckItem(targetPath, "ReadOnly", WritePermissionStatus.Blocked, "The authoritative target is read-only.");

            return new WritePermissionCheckItem(targetPath, "ReadOnly", WritePermissionStatus.Passed, "The authoritative target is not read-only.");
        }
        catch (Exception ex)
        {
            return new WritePermissionCheckItem(targetPath, "ReadOnly", WritePermissionStatus.Blocked, ex.Message);
        }
    }

    private static WritePermissionCheckItem ProbeExclusiveLock(string targetPath)
    {
        try
        {
            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return new WritePermissionCheckItem(targetPath, "ExclusiveLock", WritePermissionStatus.Passed, "The authoritative target is not exclusively locked.");
        }
        catch (Exception ex)
        {
            return new WritePermissionCheckItem(targetPath, "ExclusiveLock", WritePermissionStatus.Blocked, ex.Message);
        }
    }

    private static WritePermissionCheckItem ProbeAtomicSupport(string targetPath, bool atomicReplaceSupported)
        => atomicReplaceSupported
            ? new WritePermissionCheckItem(targetPath, "AtomicReplace", WritePermissionStatus.Passed, "Atomic replacement is supported for this target.")
            : new WritePermissionCheckItem(targetPath, "AtomicReplace", WritePermissionStatus.Blocked, "Atomic replacement is not supported for this target.");

    private static WritePermissionCheckItem ProbeTempFile(string directoryPath, string targetLabel)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var tempPath = Path.Combine(directoryPath, $".penumbraorganizer-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0x42);
                stream.Flush(flushToDisk: true);
            }

            File.Delete(tempPath);
            return new WritePermissionCheckItem(targetLabel, "TempFileProbe", WritePermissionStatus.Passed, "A same-directory temporary file can be created and cleaned up.");
        }
        catch (Exception ex)
        {
            return new WritePermissionCheckItem(targetLabel, "TempFileProbe", WritePermissionStatus.Blocked, ex.Message);
        }
    }

    private static IReadOnlyList<string> GetBlockingProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Select(process =>
                {
                    try
                    {
                        return process.ProcessName;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                })
                .Where(name => BlockingProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static long? GetAvailableBytes(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0)
            return null;

        return roots
            .Select(root => new DriveInfo(root))
            .Min(drive => drive.AvailableFreeSpace);
    }
}
