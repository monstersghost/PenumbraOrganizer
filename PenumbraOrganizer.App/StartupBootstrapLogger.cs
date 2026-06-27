namespace PenumbraOrganizer.App;

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;

internal static class StartupBootstrapLogger
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static string _logPath = string.Empty;
    private static int _dialogShown;

    public static string LogPath => _logPath;

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
        WriteLine($"ERROR: {stage}");
        WriteLine(FormatException(exception));
    }

    public static void HandleFatal(string stage, Exception exception)
    {
        RecordException(stage, exception);
        ShowFatalDialog(exception);
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

    private static void ShowFatalDialog(Exception exception)
    {
        if (Interlocked.Exchange(ref _dialogShown, 1) != 0)
            return;

        var message = string.IsNullOrWhiteSpace(_logPath)
            ? $"Penumbra Organizer could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}"
            : $"Penumbra Organizer could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}Startup log: {_logPath}";

        try
        {
            MessageBox.Show(
                message,
                "Penumbra Organizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
