namespace PenumbraOrganizer.Infrastructure.Penumbra;

using System.Reflection;
using PenumbraOrganizer.Core.Models;

/// <summary>
/// Locates and loads the <c>LiteDB.dll</c> that ships next to the user's installed Penumbra
/// plugin, rather than referencing a NuGet-restored LiteDB package from this app. Different
/// Penumbra versions can bundle different LiteDB versions, and LiteDB's on-disk file format is not
/// guaranteed compatible across major versions — writing <c>mod_data.db</c> with a different LiteDB
/// build than the one Penumbra itself uses risks producing a file Penumbra can no longer read.
/// Dynamically loading the plugin's own copy guarantees exact format compatibility on any machine,
/// for whatever Penumbra version is actually installed there.
/// </summary>
public static class LiteDbAssemblyLoader
{
    public const string FileName = "LiteDB.dll";

    public static Assembly? TryLoad(PenumbraInstallation installation)
    {
        if (string.IsNullOrWhiteSpace(installation.PluginAssemblyPath))
            return null;

        var pluginDirectory = Path.GetDirectoryName(installation.PluginAssemblyPath);
        if (string.IsNullOrWhiteSpace(pluginDirectory))
            return null;

        var liteDbPath = Path.Combine(pluginDirectory, FileName);
        if (!File.Exists(liteDbPath))
            return null;

        try
        {
            return Assembly.LoadFrom(liteDbPath);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }
}
