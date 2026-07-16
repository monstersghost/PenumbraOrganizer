namespace PenumbraOrganizer.Core.Identity;

using System.Security.Cryptography;
using System.Text;
using PenumbraOrganizer.Core.Models;

/// <summary>
/// Pure hash-building functions used to detect whether a scanned Penumbra installation or its mod
/// library has changed since a workbook/session was produced. Extracted from
/// <c>PenumbraOrganizer.Infrastructure.Sessions.OrganizerSessionService</c> so consumers with no need
/// for session save/load file I/O (e.g. a Dalamud plugin linking only the workbook export/import
/// logic) can depend on this alone.
/// </summary>
public static class ScanIdentity
{
    public static string BuildScanIdentity(ScanInventory inventory)
    {
        var input = string.Join('\n', inventory.Mods.OrderBy(mod => mod.StableScanId, StringComparer.Ordinal).Select(mod => $"{mod.StableScanId}|{mod.CurrentVirtualFolder}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    public static string BuildInstallationIdentity(PenumbraInstallation installation)
    {
        var input = $"{NormalizeForIdentity(installation.ConfigDirectory)}|{NormalizeForIdentity(installation.ModRoot)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static string NormalizeForIdentity(string path)
        => path.Trim().Replace('\\', '/').ToUpperInvariant();
}
