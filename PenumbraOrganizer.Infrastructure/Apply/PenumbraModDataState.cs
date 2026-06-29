namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// A read snapshot of Penumbra's authoritative organization file (<c>sort_order.json</c>),
/// used by the dry-run, apply and verification paths.
/// </summary>
internal sealed record PenumbraModDataState(
    string SourcePath,
    DryRunSourceFileSnapshot SourceFile,
    SchemaFingerprint SchemaFingerprint,
    PenumbraSortOrder SortOrder,
    int RecordCount)
{
    /// <summary>Current containing folder for a mod (empty string = root / no entry).</summary>
    public string CurrentFolderFor(string modDirectoryName) => SortOrder.GetFolderFor(modDirectoryName);

    /// <summary>Full sort path (folder + display leaf) when an explicit entry exists, else null.</summary>
    public string? CurrentFullPathFor(string modDirectoryName) => SortOrder.GetFullPathFor(modDirectoryName);
}
