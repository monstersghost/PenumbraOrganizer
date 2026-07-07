namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class OrganizerMutationService : IOrganizerMutationService
{
    public OrganizerMutationResult AssignToFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, IReadOnlyList<string> stableScanIds, string proposedFolder)
    {
        var normalized = VirtualFolderPath.Normalize(proposedFolder);
        if (!VirtualFolderPath.IsValid(normalized, out var error))
            return OrganizerMutationResult.Blocked(error);

        EnsureFolderExists(folders, normalized, manuallyCreated: true);
        var selected = ResolveSelectedMods(mods, stableScanIds);
        var changes = selected
            .Where(mod => !mod.Protected && !string.Equals(mod.ProposedVirtualFolder, normalized, StringComparison.Ordinal))
            .Select(mod => BuildRowChange(mod, normalized, mod.Protected, OrganizerProposalSource.Manual, mod.NeedsReview))
            .ToArray();

        if (changes.Length == 0)
            return OrganizerMutationResult.Blocked("No selected movable mods need that folder.");

        ApplyRowChanges(mods, changes, after: true);
        return OrganizerMutationResult.Success(
            $"Assign {changes.Length} mods to {normalized}",
            CreateHistory(OrganizerOperationType.AssignToFolder, $"Assign {changes.Length} mods to {normalized}", changes, []));
    }

    public OrganizerMutationResult ReturnToCurrent(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds)
    {
        var changes = ResolveSelectedMods(mods, stableScanIds)
            .Where(mod => !mod.Protected && !string.Equals(mod.ProposedVirtualFolder, mod.CurrentVirtualFolder, StringComparison.Ordinal))
            .Select(mod => BuildRowChange(mod, mod.CurrentVirtualFolder, mod.Protected, OrganizerProposalSource.PreservedCurrent, false))
            .ToArray();

        if (changes.Length == 0)
            return OrganizerMutationResult.Blocked("Selected mods are already keeping their current folders.");

        ApplyRowChanges(mods, changes, after: true);
        return OrganizerMutationResult.Success(
            $"Return {changes.Length} mods to current folders",
            CreateHistory(OrganizerOperationType.ReturnToCurrent, $"Return {changes.Length} mods to current folders", changes, []));
    }

    public OrganizerMutationResult Protect(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds)
    {
        var changes = ResolveSelectedMods(mods, stableScanIds)
            .Where(mod => !mod.Protected)
            .Select(mod => BuildRowChange(mod, mod.CurrentVirtualFolder, true, OrganizerProposalSource.Manual, false))
            .ToArray();

        if (changes.Length == 0)
            return OrganizerMutationResult.Blocked("Selected mods are already protected.");

        ApplyRowChanges(mods, changes, after: true);
        return OrganizerMutationResult.Success(
            $"Protect {changes.Length} mods",
            CreateHistory(OrganizerOperationType.Protect, $"Protect {changes.Length} mods", changes, []));
    }

    public OrganizerMutationResult Unprotect(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds)
    {
        var changes = ResolveSelectedMods(mods, stableScanIds)
            .Where(mod => mod.Protected)
            .Select(mod => BuildRowChange(mod, mod.ProposedVirtualFolder, false, OrganizerProposalSource.Manual, mod.NeedsReview))
            .ToArray();

        if (changes.Length == 0)
            return OrganizerMutationResult.Blocked("Selected mods are already unprotected.");

        ApplyRowChanges(mods, changes, after: true);
        return OrganizerMutationResult.Success(
            $"Unprotect {changes.Length} mods",
            CreateHistory(OrganizerOperationType.Unprotect, $"Unprotect {changes.Length} mods", changes, []));
    }

    public OrganizerMutationResult CreateFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string proposedFolder)
    {
        var normalized = VirtualFolderPath.Normalize(proposedFolder);
        if (!VirtualFolderPath.IsValid(normalized, out var error))
            return OrganizerMutationResult.Blocked(error);
        if (FolderExists(folders, normalized))
            return OrganizerMutationResult.Blocked("That proposed folder already exists.");
        if (HasSiblingCollision(folders, normalized, oldPath: null))
            return OrganizerMutationResult.Blocked("A folder with that name already exists beside it.");

        folders.Add(new OrganizerFolder(normalized, ManuallyCreated: true));
        var folderChange = new OrganizerFolderChange(normalized, normalized, BeforeExists: false, AfterExists: true, BeforeManuallyCreated: false, AfterManuallyCreated: true);
        return OrganizerMutationResult.Success(
            $"Create folder {normalized}",
            CreateHistory(OrganizerOperationType.CreateFolder, $"Create folder {normalized}", [], [folderChange]));
    }

    public OrganizerMutationResult RenameFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string oldPath, string newPath)
    {
        oldPath = VirtualFolderPath.Normalize(oldPath);
        newPath = VirtualFolderPath.Normalize(newPath);
        if (!FolderExists(folders, oldPath))
            return OrganizerMutationResult.Blocked("Choose an existing proposed folder to rename.");
        if (!VirtualFolderPath.IsValid(newPath, out var error))
            return OrganizerMutationResult.Blocked(error);
        if (oldPath.Equals(newPath, StringComparison.Ordinal))
            return OrganizerMutationResult.Blocked("The folder already has that name.");
        if (IsProtectedSubtree(mods, oldPath))
            return OrganizerMutationResult.Blocked("This folder contains protected mods and cannot be renamed.");
        if (HasSiblingCollision(folders, newPath, oldPath))
            return OrganizerMutationResult.Blocked("A folder with that name already exists beside it.");

        var rowChanges = mods
            .Where(mod => IsSameOrDescendant(mod.ProposedVirtualFolder, oldPath))
            .Select(mod => BuildRowChange(mod, RewritePrefix(mod.ProposedVirtualFolder, oldPath, newPath), mod.Protected, OrganizerProposalSource.Manual, mod.NeedsReview))
            .ToArray();

        var folderChanges = folders
            .Where(folder => IsSameOrDescendant(folder.Path, oldPath))
            .Select(folder => new OrganizerFolderChange(
                folder.Path,
                RewritePrefix(folder.Path, oldPath, newPath),
                BeforeExists: true,
                AfterExists: true,
                folder.ManuallyCreated,
                folder.ManuallyCreated))
            .ToArray();

        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            if (IsSameOrDescendant(folder.Path, oldPath))
                folders[i] = folder with { Path = RewritePrefix(folder.Path, oldPath, newPath) };
        }

        ApplyRowChanges(mods, rowChanges, after: true);
        return OrganizerMutationResult.Success(
            $"Rename {oldPath} to {newPath}",
            CreateHistory(OrganizerOperationType.RenameFolder, $"Rename {oldPath} to {newPath}", rowChanges, folderChanges));
    }

    public OrganizerMutationResult DeleteEmptyFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string path)
    {
        path = VirtualFolderPath.Normalize(path);
        var folder = folders.FirstOrDefault(existing => existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
            return OrganizerMutationResult.Blocked("Choose an existing proposed folder to delete.");
        if (folder.Protected || mods.Any(mod => mod.ProposedVirtualFolder.Equals(path, StringComparison.OrdinalIgnoreCase) || mod.ProposedVirtualFolder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
            return OrganizerMutationResult.Blocked("Only empty proposed folders can be deleted.");
        if (folders.Any(existing => !existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && existing.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
            return OrganizerMutationResult.Blocked("Only empty proposed folders can be deleted.");

        folders.Remove(folder);
        var folderChange = new OrganizerFolderChange(path, path, BeforeExists: true, AfterExists: false, folder.ManuallyCreated, AfterManuallyCreated: false);
        return OrganizerMutationResult.Success(
            $"Delete folder {path}",
            CreateHistory(OrganizerOperationType.DeleteFolder, $"Delete folder {path}", [], [folderChange]));
    }

    public void ApplyUndo(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, OrganizerHistoryEntry entry)
    {
        ApplyFolderChanges(folders, entry.FolderChanges, after: false);
        ApplyRowChanges(mods, entry.RowChanges, after: false);
    }

    public void ApplyRedo(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, OrganizerHistoryEntry entry)
    {
        ApplyFolderChanges(folders, entry.FolderChanges, after: true);
        ApplyRowChanges(mods, entry.RowChanges, after: true);
    }

    private static OrganizerHistoryEntry CreateHistory(
        OrganizerOperationType operationType,
        string description,
        IReadOnlyList<OrganizerOperationChange> rowChanges,
        IReadOnlyList<OrganizerFolderChange> folderChanges)
        => new(
            Guid.NewGuid(),
            operationType,
            description,
            DateTimeOffset.UtcNow,
            rowChanges.Select(change => change.StableScanId).Distinct(StringComparer.Ordinal).ToArray(),
            rowChanges,
            folderChanges);

    private static OrganizerOperationChange BuildRowChange(
        OrganizerModProposal mod,
        string newFolder,
        bool newProtected,
        OrganizerProposalSource newSource,
        bool newNeedsReview)
        => new(
            mod.StableScanId,
            mod.ProposedVirtualFolder,
            newFolder,
            mod.Protected,
            newProtected,
            mod.Source,
            newSource,
            mod.NeedsReview,
            newNeedsReview);

    private static void ApplyRowChanges(IList<OrganizerModProposal> mods, IReadOnlyList<OrganizerOperationChange> changes, bool after)
    {
        var byId = mods.ToDictionary(mod => mod.StableScanId, StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!byId.TryGetValue(change.StableScanId, out var mod))
                continue;
            mod.ProposedVirtualFolder = after ? change.AfterProposedVirtualFolder : change.BeforeProposedVirtualFolder;
            mod.Protected = after ? change.AfterProtected : change.BeforeProtected;
            mod.Source = after ? change.AfterSource : OrganizerProposalSource.RestoredByUndo;
            mod.NeedsReview = after ? change.AfterNeedsReview : change.BeforeNeedsReview;
        }
    }

    private static void ApplyFolderChanges(IList<OrganizerFolder> folders, IReadOnlyList<OrganizerFolderChange> changes, bool after)
    {
        foreach (var change in changes)
        {
            var before = folders.FirstOrDefault(folder => folder.Path.Equals(change.BeforePath, StringComparison.OrdinalIgnoreCase));
            var afterFolder = new OrganizerFolder(change.AfterPath, change.AfterManuallyCreated);
            if (after)
            {
                if (change.AfterExists && before is null)
                    folders.Add(afterFolder);
                else if (change.AfterExists && before is not null)
                    folders[folders.IndexOf(before)] = afterFolder;
                else if (before is not null)
                    folders.Remove(before);
            }
            else
            {
                var current = folders.FirstOrDefault(folder => folder.Path.Equals(change.AfterPath, StringComparison.OrdinalIgnoreCase));
                var beforeFolder = new OrganizerFolder(change.BeforePath, change.BeforeManuallyCreated);
                if (change.BeforeExists && current is null)
                    folders.Add(beforeFolder);
                else if (change.BeforeExists && current is not null)
                    folders[folders.IndexOf(current)] = beforeFolder;
                else if (current is not null)
                    folders.Remove(current);
            }
        }
    }

    private static OrganizerModProposal[] ResolveSelectedMods(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds)
    {
        var ids = stableScanIds.ToHashSet(StringComparer.Ordinal);
        return mods.Where(mod => ids.Contains(mod.StableScanId)).ToArray();
    }

    private static void EnsureFolderExists(IList<OrganizerFolder> folders, string path, bool manuallyCreated)
    {
        if (!FolderExists(folders, path))
            folders.Add(new OrganizerFolder(path, manuallyCreated));
    }

    private static bool FolderExists(IList<OrganizerFolder> folders, string path)
        => folders.Any(folder => folder.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static bool HasSiblingCollision(IList<OrganizerFolder> folders, string path, string? oldPath)
    {
        var parent = ParentPath(path);
        var name = LeafName(path);
        return folders.Any(folder =>
            !folder.Path.Equals(oldPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            ParentPath(folder.Path).Equals(parent, StringComparison.OrdinalIgnoreCase) &&
            LeafName(folder.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProtectedSubtree(IList<OrganizerModProposal> mods, string path)
        => mods.Any(mod => mod.Protected && IsSameOrDescendant(mod.ProposedVirtualFolder, path));

    private static bool IsSameOrDescendant(string candidate, string root)
        => candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
           candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static string RewritePrefix(string value, string oldPrefix, string newPrefix)
        => value.Equals(oldPrefix, StringComparison.OrdinalIgnoreCase)
            ? newPrefix
            : newPrefix + value[oldPrefix.Length..];

    private static string ParentPath(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    private static string LeafName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }
}

public static class VirtualFolderPath
{
    public static string Normalize(string path)
        => path.Trim().Replace('\\', '/').Trim('/');

    public static bool IsValid(string path, out string error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Folder name is required.";
            return false;
        }

        if (path != Normalize(path))
        {
            error = "Folder names cannot start or end with a separator.";
            return false;
        }

        if (Path.IsPathRooted(path) ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.Any(char.IsControl) ||
            path.StartsWith('/') ||
            path.EndsWith('/'))
        {
            error = "Use a relative Penumbra folder name.";
            return false;
        }

        var parts = path.Split('/', StringSplitOptions.None);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            error = "Folder names cannot be empty or use . or ...";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
