namespace PenumbraOrganizer.App;

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using PenumbraOrganizer.App.Dialogs;

internal static class StartupBootstrapLogger
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static string _logPath = string.Empty;
    private static string _logsDirectory = string.Empty;
    private static string _lastExceptionDetails = string.Empty;
    private static int _dialogShown;

    public static string LogPath => _logPath;
    public static string LogsDirectory => _logsDirectory;
    public static string LastExceptionDetails => _lastExceptionDetails;

    public static void Initialize(string[] args)
    {
        lock (Sync)
        {
            if (_writer is not null || !string.IsNullOrWhiteSpace(_logPath))
                return;

            var timestamp = DateTimeOffset.Now;
            _logPath = TryCreatePrimaryLog(timestamp) ?? TryCreateFallbackLog(timestamp) ?? string.Empty;
            WriteHeader(args);
        }
    }

    public static void Stage(string stage)
        => WriteLine($"STAGE: {stage}");

    public static void Note(string message)
        => WriteLine($"INFO: {message}");

    public static void RecordException(string stage, Exception exception)
    {
        _lastExceptionDetails = $"Stage: {stage}{Environment.NewLine}{Environment.NewLine}{FormatException(exception)}";
        WriteLine($"ERROR: {stage}");
        WriteLine(FormatException(exception));
    }

    public static void HandleFatal(string stage, Exception exception)
    {
        RecordException(stage, exception);
        ShowFatalDialog(stage, exception);
    }

    public static void OpenLogsFolder()
    {
        var target = Directory.Exists(_logsDirectory)
            ? _logsDirectory
            : (!string.IsNullOrWhiteSpace(_logPath) ? Path.GetDirectoryName(_logPath) : null);
        if (string.IsNullOrWhiteSpace(target))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        });
    }

    public static void CopyDetails()
    {
        var details = string.IsNullOrWhiteSpace(_lastExceptionDetails)
            ? $"Log path: {_logPath}"
            : $"{_lastExceptionDetails}{Environment.NewLine}{Environment.NewLine}Log path: {_logPath}";
        Clipboard.SetText(details);
    }

    private static void WriteHeader(string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Unknown";
        var currentDirectory = Environment.CurrentDirectory;
        var commandLine = args.Length == 0 ? "(none)" : string.Join(" ", args);

        WriteLine($"Launch UTC: {DateTimeOffset.UtcNow:O}");
        WriteLine($"Launch Local: {DateTimeOffset.Now:O}");
        WriteLine($"App Version: {assembly.Version?.ToString() ?? "Unknown"}");
        WriteLine($"Executable Path: {processPath}");
        WriteLine($"Working Directory: {currentDirectory}");
        WriteLine($"Windows Version: {RuntimeInformation.OSDescription}");
        WriteLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        WriteLine($"Elevated: {IsElevated()}");
        WriteLine($"Command-Line Arguments: {commandLine}");
    }

    private static string? TryCreatePrimaryLog(DateTimeOffset timestamp)
    {
        try
        {
            var logsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PenumbraOrganizer",
                "Logs");
            Directory.CreateDirectory(logsDirectory);
            _logsDirectory = logsDirectory;
            var path = Path.Combine(logsDirectory, $"startup-{timestamp:yyyyMMdd-HHmmss}.log");
            _writer = CreateWriter(path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryCreateFallbackLog(DateTimeOffset timestamp)
    {
        try
        {
            var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            var baseDirectory = string.IsNullOrWhiteSpace(processPath)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(processPath!) ?? AppContext.BaseDirectory;
            var path = Path.Combine(baseDirectory, "PenumbraOrganizer-startup-error.log");
            _writer = CreateWriter(path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    private static void WriteLine(string line)
    {
        lock (Sync)
        {
            if (_writer is null)
                return;

            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private static string FormatException(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;
        while (current is not null)
        {
            builder.AppendLine(depth == 0
                ? $"Exception: {current.GetType().FullName}: {current.Message}"
                : $"Inner Exception {depth}: {current.GetType().FullName}: {current.Message}");
            builder.AppendLine(current.StackTrace ?? "(no stack trace)");
            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void ShowFatalDialog(string stage, Exception exception)
    {
        if (Interlocked.Exchange(ref _dialogShown, 1) != 0)
            return;

        try
        {
            var window = new FatalErrorWindow(
                "Penumbra Organizer could not start.",
                stage,
                exception,
                _logsDirectory,
                _logPath)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            window.ShowDialog();
        }
        catch
        {
            try
            {
                MessageBox.Show(
                    "Penumbra Organizer could not start.",
                    "Penumbra Organizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }
}
