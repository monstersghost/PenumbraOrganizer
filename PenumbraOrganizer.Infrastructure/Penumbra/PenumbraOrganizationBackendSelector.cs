namespace PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// Which file actually holds Penumbra's authoritative per-mod virtual-folder organization.
/// Penumbra has shipped more than one storage format over time, and different installed versions
/// keep whichever one they last used on disk even after the plugin stops touching it — a stale
/// <c>sort_order.json.bak</c> can sit untouched for months while <c>mod_data.db</c> is the file the
/// plugin is actually writing to every session (or vice versa on an older install).
/// </summary>
public enum PenumbraOrganizationBackend
{
    SortOrderJson,
    ModDataDb,
}

/// <summary>
/// Picks the authoritative backend by comparing on-disk modification times: whichever of the two
/// storage formats Penumbra actually touched most recently wins. This is the only reliable signal
/// available — file *presence* alone doesn't tell you which one the currently-installed Penumbra
/// version is still writing to, since an abandoned format's files are rarely deleted.
/// </summary>
public static class PenumbraOrganizationBackendSelector
{
    public static PenumbraOrganizationBackend Detect(string configDirectory)
    {
        var sortOrderTime = LatestWriteTimeOrNull(PenumbraSortOrder.GetPath(configDirectory))
            ?? LatestWriteTimeOrNull(PenumbraSortOrder.GetBackupPath(configDirectory));
        var modDataDbTime = LatestWriteTimeOrNull(PenumbraModDataDb.GetPath(configDirectory));

        if (modDataDbTime is null)
            return PenumbraOrganizationBackend.SortOrderJson;

        if (sortOrderTime is null)
            return PenumbraOrganizationBackend.ModDataDb;

        return modDataDbTime > sortOrderTime
            ? PenumbraOrganizationBackend.ModDataDb
            : PenumbraOrganizationBackend.SortOrderJson;
    }

    private static DateTime? LatestWriteTimeOrNull(string path)
        => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
}
