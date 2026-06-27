namespace PenumbraOrganizer.Infrastructure.Compatibility;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class PenumbraCompatibilityService : IPenumbraCompatibilityService
{
    private readonly ILogger<PenumbraCompatibilityService> _logger;

    public PenumbraCompatibilityService(ILogger<PenumbraCompatibilityService> logger)
    {
        _logger = logger;
    }

    public Task<CompatibilityReport> EvaluateAsync(PenumbraInstallation installation, ScanInventory inventory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var fingerprints = inventory.Mods.SelectMany(m => m.SchemaFingerprints).ToList();
        var installedVersion = ReReadInstalledVersion(installation);
        var scannedVersion = inventory.Installation.InstalledVersion ?? string.Empty;

        var severeSchema = fingerprints.Where(f =>
                f.DifferenceKind is SchemaDifferenceKind.RootStructureChange or SchemaDifferenceKind.MissingKnownRequiredField or SchemaDifferenceKind.TypeChange)
            .ToList();

        CompatibilityStatus status;
        if (string.IsNullOrWhiteSpace(installation.ConfigurationPath) || !File.Exists(installation.ConfigurationPath))
        {
            status = CompatibilityStatus.ConfigurationInvalid;
            warnings.Add("The Penumbra configuration file is missing.");
        }
        else if (string.IsNullOrWhiteSpace(installedVersion))
        {
            status = CompatibilityStatus.PenumbraNotFound;
            warnings.Add("The installed Penumbra version could not be read.");
        }
        else if (severeSchema.Count > 0)
        {
            status = CompatibilityStatus.UnknownSchema;
            warnings.Add("One or more metadata files use a damaged or unsupported structure.");
        }
        else if (!string.Equals(installedVersion, scannedVersion, StringComparison.OrdinalIgnoreCase))
        {
            status = CompatibilityStatus.VersionChangedNeedsReview;
            warnings.Add("Penumbra changed after the scan. A fresh scan is required before any writes.");
        }
        else
        {
            status = CompatibilityStatus.Compatible;
        }

        if (severeSchema.Count > 0)
            _logger.LogWarning("Compatibility check found {Count} unsupported schema fingerprints", severeSchema.Count);

        return Task.FromResult(new CompatibilityReport(
            status,
            installedVersion ?? string.Empty,
            scannedVersion,
            fingerprints,
            warnings));
    }

    private static string? ReReadInstalledVersion(PenumbraInstallation installation)
    {
        if (!string.IsNullOrWhiteSpace(installation.PluginManifestPath) && File.Exists(installation.PluginManifestPath))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(installation.PluginManifestPath));
            if (doc.RootElement.TryGetProperty("AssemblyVersion", out var version))
                return version.GetString();
        }

        if (!string.IsNullOrWhiteSpace(installation.PluginAssemblyPath) && File.Exists(installation.PluginAssemblyPath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(installation.PluginAssemblyPath);
            return fileVersion.ProductVersion ?? fileVersion.FileVersion;
        }

        return null;
    }
}
