namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Diagnostics;
using System.Text.Json;
using PenumbraOrganizer.Core.Models;

internal static class PenumbraInstalledVersionReader
{
    public static string? Read(PenumbraInstallation installation)
    {
        if (!string.IsNullOrWhiteSpace(installation.PluginManifestPath) && File.Exists(installation.PluginManifestPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(installation.PluginManifestPath));
            if (doc.RootElement.TryGetProperty("AssemblyVersion", out var version))
                return version.GetString();
        }

        if (!string.IsNullOrWhiteSpace(installation.PluginAssemblyPath) && File.Exists(installation.PluginAssemblyPath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(installation.PluginAssemblyPath);
            return fileVersion.ProductVersion ?? fileVersion.FileVersion;
        }

        return installation.InstalledVersion;
    }
}
