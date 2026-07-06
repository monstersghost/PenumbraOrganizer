namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;

/// <summary>
/// A read snapshot of Penumbra's authoritative organization file (<c>mod_data.db</c>), used by the
/// dry-run, apply and verification paths. Mirrors <see cref="PenumbraModDataState"/>'s role for
/// <c>sort_order.json</c>.
/// </summary>
internal sealed record PenumbraModDataDbState(
    string SourcePath,
    DryRunSourceFileSnapshot SourceFile,
    SchemaFingerprint SchemaFingerprint,
    PenumbraModDataDb Data)
{
    /// <summary>Current containing folder for a mod (empty string = root / no entry).</summary>
    public string CurrentFolderFor(string modDirectoryName) => Data.GetFolderFor(modDirectoryName);
}
