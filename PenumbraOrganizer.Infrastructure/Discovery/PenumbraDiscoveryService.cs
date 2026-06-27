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

        if (LooksLikeWinePath(configPath) || LooksLikeWinePath(modRoot) || LooksLikeWinePath(pluginAssemblyPath))
        {
            return Task.FromResult<PenumbraInstallation?>(new PenumbraInstallation(
                configPath,
                Path.GetDirectoryName(configPath) ?? string.Empty,
                modRoot ?? string.Empty,
                pluginAssemblyPath,
                GetPluginManifestPath(pluginAssemblyPath),
                null,
                DiscoveryConfidence.Manual,
                new[] { new DiscoveryEvidence("Manual selection", "Path was provided by the user.") },
                new[] { "Wine and Linux-style Penumbra locations are not supported in version 1." }));
        }

        if (!File.Exists(configPath))
            return Task.FromResult<PenumbraInstallation?>(null);

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!doc.RootElement.TryGetProperty("ModDirectory", out var modDirectoryProperty))
            return Task.FromResult<PenumbraInstallation?>(null);

        var resolvedModRoot = string.IsNullOrWhiteSpace(modRoot)
            ? modDirectoryProperty.GetString() ?? string.Empty
            : modRoot;

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
                 })
        {
            if (seen.Add(path))
                yield return path;
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

        var modRoot = modDirectoryProperty.GetString() ?? string.Empty;
        var evidence = new List<DiscoveryEvidence>
        {
            new("Configuration file", configPath),
            new("Configured mod directory", modRoot),
        };
        var warnings = new List<string>();
        var confidence = DiscoveryConfidence.Medium;

        if (LooksLikeWinePath(modRoot))
        {
            warnings.Add("Wine and Linux-style Penumbra mod roots are not supported in version 1.");
            confidence = DiscoveryConfidence.Low;
        }
        else if (!Directory.Exists(modRoot))
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

    private static bool LooksLikeWinePath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (path!.StartsWith("/home/", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("Z:\\", StringComparison.OrdinalIgnoreCase));
}
