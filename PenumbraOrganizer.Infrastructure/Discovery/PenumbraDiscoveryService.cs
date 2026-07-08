namespace PenumbraOrganizer.Infrastructure.Discovery;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class PenumbraDiscoveryService : IPenumbraDiscoveryService
{
    private readonly ILogger<PenumbraDiscoveryService> _logger;
    private readonly IReadOnlyList<string>? _candidateBasePaths;

    public PenumbraDiscoveryService(ILogger<PenumbraDiscoveryService> logger, IReadOnlyList<string>? candidateBasePaths = null)
    {
        _logger = logger;
        _candidateBasePaths = candidateBasePaths;
    }

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var installations = new List<PenumbraInstallation>();
        var errors = new List<string>();

        foreach (var basePath in _candidateBasePaths ?? GetCandidateBasePaths().ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var candidate = TryDiscoverFromBasePath(basePath);
                if (candidate is not null)
                    installations.Add(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Failed to inspect Penumbra candidate base path {BasePath}", basePath);
                errors.Add($"Could not fully inspect {basePath}.");
            }
        }

        var distinct = installations
            .GroupBy(i => $"{i.ConfigurationPath}|{i.ModRoot}|{i.PluginAssemblyPath}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.Confidence).First())
            .OrderByDescending(i => i.Confidence)
            .ToList();

        return Task.FromResult(new DiscoveryResult(
            distinct,
            distinct.Count != 1,
            errors));
    }

    public Task<PenumbraInstallation?> ValidateManualSelectionAsync(string configPath, string? modRoot, string? pluginAssemblyPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(configPath))
            return Task.FromResult<PenumbraInstallation?>(null);

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!doc.RootElement.TryGetProperty("ModDirectory", out var modDirectoryProperty))
            return Task.FromResult<PenumbraInstallation?>(null);

        var resolvedModRoot = NormalizeForRuntime(string.IsNullOrWhiteSpace(modRoot)
            ? modDirectoryProperty.GetString() ?? string.Empty
            : modRoot);

        if (!Directory.Exists(resolvedModRoot))
            return Task.FromResult<PenumbraInstallation?>(null);

        if (!IsPlausibleConfigDirectory(configPath))
            return Task.FromResult<PenumbraInstallation?>(null);

        var version = ReadInstalledVersion(pluginAssemblyPath, GetPluginManifestPath(pluginAssemblyPath));
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(version))
            warnings.Add("Penumbra plugin version could not be read from the selected plugin files.");

        var result = new PenumbraInstallation(
            configPath,
            Path.Combine(Path.GetDirectoryName(configPath)!, "Penumbra"),
            resolvedModRoot,
            pluginAssemblyPath,
            GetPluginManifestPath(pluginAssemblyPath),
            version,
            DiscoveryConfidence.Manual,
            new[] { new DiscoveryEvidence("Manual selection", "User selected Penumbra paths manually.") },
            warnings);

        return Task.FromResult<PenumbraInstallation?>(result);
    }

    public string? ResolveConfigPathFromFolder(string folderPath)
    {
        var candidates = new[]
        {
            Path.Combine(folderPath, "Penumbra.json"),
            Path.Combine(folderPath, "pluginConfigs", "Penumbra.json"),
            Path.Combine(folderPath, "..", "Penumbra.json"),
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetCandidateBasePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     Path.Combine(appData, "XIVLauncher"),
                     Path.Combine(localAppData, "XIVLauncher"),
                     Path.Combine(appData, "XIVLauncherCN"),
                     Path.Combine(localAppData, "XIVLauncherCN"),
                 }.Concat(GetXlCoreBasePaths()))
        {
            if (seen.Add(path))
                yield return path;
        }
    }

    // XIVLauncher.Core (the Linux / Steam Deck launcher) stores plugin configs under
    // $HOME/.xlcore/pluginConfigs/Penumbra.json — the same layout the Windows XIVLauncher uses
    // under %AppData%\XIVLauncher, so TryDiscoverFromBasePath handles it once we add the base.
    // We host the app under Wine, where the Linux root is exposed via the Z: drive mapping, so the
    // home directory is reachable as Z:\home\<user>\.xlcore. The raw POSIX form is also yielded so
    // a native (non-Wine) Linux run is covered too; non-existent candidates are simply skipped.
    private static IEnumerable<string> GetXlCoreBasePaths()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            yield break;

        if (OperatingSystem.IsWindows())
        {
            if (home.StartsWith('/'))
                yield return "Z:" + home.Replace('/', '\\') + "\\.xlcore";
        }
        else
        {
            yield return Path.Combine(home, ".xlcore");
        }
    }

    private PenumbraInstallation? TryDiscoverFromBasePath(string basePath)
    {
        var configPath = Path.Combine(basePath, "pluginConfigs", "Penumbra.json");
        if (!File.Exists(configPath))
            return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!doc.RootElement.TryGetProperty("ModDirectory", out var modDirectoryProperty))
            return null;

        var modRoot = NormalizeForRuntime(modDirectoryProperty.GetString() ?? string.Empty);
        var evidence = new List<DiscoveryEvidence>
        {
            new("Configuration file", configPath),
            new("Configured mod directory", modRoot),
        };
        var warnings = new List<string>();
        DiscoveryConfidence confidence;

        if (!Directory.Exists(modRoot))
        {
            warnings.Add("Configured mod directory does not currently exist.");
            confidence = DiscoveryConfidence.Low;
        }
        else
        {
            confidence = DiscoveryConfidence.High;
        }

        var configDirectory = Path.Combine(basePath, "pluginConfigs", "Penumbra");
        if (!Directory.Exists(configDirectory))
            warnings.Add("Penumbra configuration directory is missing.");

        var pluginDirectory = Path.Combine(basePath, "installedPlugins", "Penumbra");
        var pluginAssemblyPath = FindNewestPluginAssembly(pluginDirectory);
        var pluginManifestPath = GetPluginManifestPath(pluginAssemblyPath);
        var version = ReadInstalledVersion(pluginAssemblyPath, pluginManifestPath);
        if (!string.IsNullOrWhiteSpace(pluginAssemblyPath))
            evidence.Add(new DiscoveryEvidence("Installed plugin assembly", pluginAssemblyPath!));
        if (!string.IsNullOrWhiteSpace(version))
            evidence.Add(new DiscoveryEvidence("Installed version", version!));
        else
            warnings.Add("Installed Penumbra version could not be determined.");

        return new PenumbraInstallation(
            configPath,
            configDirectory,
            modRoot,
            pluginAssemblyPath,
            pluginManifestPath,
            version,
            confidence,
            evidence,
            warnings);
    }

    private static bool IsPlausibleConfigDirectory(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (directory is null)
            return false;

        return Directory.Exists(Path.Combine(directory, "Penumbra"));
    }

    private static string? FindNewestPluginAssembly(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
            return null;

        return Directory.EnumerateDirectories(pluginDirectory)
            .Select(path => Path.Combine(path, "Penumbra.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? GetPluginManifestPath(string? pluginAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            return null;

        var candidate = Path.Combine(Path.GetDirectoryName(pluginAssemblyPath)!, "Penumbra.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ReadInstalledVersion(string? pluginAssemblyPath, string? pluginManifestPath)
    {
        if (!string.IsNullOrWhiteSpace(pluginManifestPath) && File.Exists(pluginManifestPath))
        {
            using var manifestDoc = JsonDocument.Parse(File.ReadAllText(pluginManifestPath));
            if (manifestDoc.RootElement.TryGetProperty("AssemblyVersion", out var assemblyVersion))
                return assemblyVersion.GetString();
        }

        if (!string.IsNullOrWhiteSpace(pluginAssemblyPath) && File.Exists(pluginAssemblyPath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(pluginAssemblyPath);
            return fileVersion.ProductVersion
                ?? fileVersion.FileVersion
                ?? fileVersion.ProductMajorPart.ToString();
        }

        return null;
    }

    // Penumbra running under the game's Wine prefix normally stores Windows-style mod roots
    // (e.g. Z:\home\user\Mods), which a Wine-hosted instance of this app can stat directly. As a
    // safety net for configs that stored a raw POSIX path, translate a leading-slash path to the
    // Wine Z: drive mapping (Z: == /) when we are running on Windows/Wine. Native Linux runs keep
    // the path as-is.
    private static string NormalizeForRuntime(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (OperatingSystem.IsWindows() && path.StartsWith('/'))
            return "Z:" + path.Replace('/', '\\');

        return path;
    }
}
