namespace PenumbraOrganizer.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using PenumbraOrganizer.Core.Models;

public sealed class ModRowViewModel : ObservableObject
{
    private string _proposedVirtualFolder = string.Empty;
    private bool _protected;
    private string _proposalSource = "Preserved current";
    private string _effectiveCreator = string.Empty;
    private string _detectedType = string.Empty;

    // Editable metadata (the originals are the scanned values; an edit is any field that differs).
    private readonly string _originalName;
    private readonly string _originalAuthor;
    private readonly string _originalDescription;
    private readonly string _originalVersion;
    private readonly string _originalWebsite;
    private readonly IReadOnlyList<string> _originalModTags;
    private readonly bool _originalFavorite;
    private readonly IReadOnlyList<string> _originalLocalTags;
    private readonly string _originalNote;
    private readonly bool _hasLocalData;

    private string _editName;
    private string _editAuthor;
    private string _editDescription;
    private string _editVersion;
    private string _editWebsite;
    private string _editModTagsText;
    private bool _editFavorite;
    private string _editLocalTagsText;
    private string _editNote;

    public ModRowViewModel(ModScanResult mod)
    {
        StableScanId = mod.StableScanId;

        _originalName = mod.Name ?? string.Empty;
        _originalAuthor = mod.Author ?? string.Empty;
        _originalDescription = mod.Description ?? string.Empty;
        _originalVersion = mod.Version ?? string.Empty;
        _originalWebsite = mod.Website ?? string.Empty;
        _originalModTags = mod.Tags ?? Array.Empty<string>();
        _originalFavorite = mod.Favorite;
        _originalLocalTags = mod.LocalTags ?? Array.Empty<string>();
        _originalNote = mod.Note ?? string.Empty;
        _hasLocalData = mod.HasLocalData;

        _editName = _originalName;
        _editAuthor = _originalAuthor;
        _editDescription = _originalDescription;
        _editVersion = _originalVersion;
        _editWebsite = _originalWebsite;
        _editModTagsText = JoinTags(_originalModTags);
        _editFavorite = _originalFavorite;
        _editLocalTagsText = JoinTags(_originalLocalTags);
        _editNote = _originalNote;
        Proposal = new OrganizerModProposal
        {
            StableScanId = mod.StableScanId,
            Name = mod.Name,
            CurrentVirtualFolder = mod.CurrentVirtualFolder,
            ProposedVirtualFolder = mod.CurrentVirtualFolder,
            OriginalCreator = mod.Author,
            OrganizerCreatorLabel = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author,
            OrganizerTypeLabel = WorkbookCategoryCatalog.Detect(mod).Name,
            Protected = mod.Protected,
            OriginalProtected = mod.Protected,
            Source = OrganizerProposalSource.PreservedCurrent,
        };
        Name = mod.Name;
        Author = mod.Author;
        _effectiveCreator = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author;
        CurrentVirtualFolder = mod.CurrentVirtualFolder;
        _proposedVirtualFolder = mod.CurrentVirtualFolder;
        PhysicalDirectory = mod.PhysicalDirectory;
        _protected = mod.Protected;
        ContentSignalSummary = mod.ContentSignalSummary;
        CollectionCount = mod.CollectionStates.Count;
        WarningSummary = mod.Warnings.Count == 0 ? string.Empty : string.Join(" | ", mod.Warnings.Take(3));
        _detectedType = WorkbookCategoryCatalog.Detect(mod).Name;
    }

    public OrganizerModProposal Proposal { get; }
    public string StableScanId { get; }
    public string Name { get; }
    public string Author { get; }
    public string CurrentVirtualFolder { get; }
    public string PhysicalDirectory { get; }
    public bool Protected
    {
        get => _protected;
        set
        {
            if (SetProperty(ref _protected, value))
                Proposal.Protected = value;
        }
    }

    public string ContentSignalSummary { get; }
    public int CollectionCount { get; }
    public string WarningSummary { get; }
    public bool IsChanged => !string.Equals(CurrentVirtualFolder, ProposedVirtualFolder, StringComparison.Ordinal);

    public string EffectiveCreator
    {
        get => _effectiveCreator;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown creator" : value.Trim();
            if (SetProperty(ref _effectiveCreator, normalized))
                Proposal.OrganizerCreatorLabel = normalized;
        }
    }

    public string DetectedType
    {
        get => _detectedType;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown type" : value.Trim();
            if (SetProperty(ref _detectedType, normalized))
                Proposal.OrganizerTypeLabel = normalized;
        }
    }

    public string ProposalSource
    {
        get => _proposalSource;
        set
        {
            if (SetProperty(ref _proposalSource, value) && Enum.TryParse<OrganizerProposalSource>(value.Replace(" ", string.Empty), ignoreCase: true, out var parsed))
                Proposal.Source = parsed;
        }
    }

    public string ProposedVirtualFolder
    {
        get => _proposedVirtualFolder;
        set
        {
            if (SetProperty(ref _proposedVirtualFolder, value))
            {
                Proposal.ProposedVirtualFolder = value;
                RaisePropertyChanged(nameof(IsChanged));
            }
        }
    }

    public void RefreshFromProposal()
    {
        _proposedVirtualFolder = Proposal.ProposedVirtualFolder;
        _protected = Proposal.Protected;
        _proposalSource = DisplaySource(Proposal.Source);
        _effectiveCreator = string.IsNullOrWhiteSpace(Proposal.OrganizerCreatorLabel) ? "Unknown creator" : Proposal.OrganizerCreatorLabel;
        _detectedType = string.IsNullOrWhiteSpace(Proposal.OrganizerTypeLabel) ? "Unknown type" : Proposal.OrganizerTypeLabel;
        RaisePropertyChanged(nameof(ProposedVirtualFolder));
        RaisePropertyChanged(nameof(Protected));
        RaisePropertyChanged(nameof(ProposalSource));
        RaisePropertyChanged(nameof(EffectiveCreator));
        RaisePropertyChanged(nameof(DetectedType));
        RaisePropertyChanged(nameof(IsChanged));
    }

    // ---- Editable metadata --------------------------------------------------------------

    public string EditName
    {
        get => _editName;
        set { if (SetProperty(ref _editName, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditAuthor
    {
        get => _editAuthor;
        set { if (SetProperty(ref _editAuthor, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditDescription
    {
        get => _editDescription;
        set { if (SetProperty(ref _editDescription, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditVersion
    {
        get => _editVersion;
        set { if (SetProperty(ref _editVersion, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditWebsite
    {
        get => _editWebsite;
        set { if (SetProperty(ref _editWebsite, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditModTagsText
    {
        get => _editModTagsText;
        set { if (SetProperty(ref _editModTagsText, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public bool Favorite
    {
        get => _editFavorite;
        set { if (SetProperty(ref _editFavorite, value)) RaiseMetadataChanged(); }
    }

    public string EditLocalTagsText
    {
        get => _editLocalTagsText;
        set { if (SetProperty(ref _editLocalTagsText, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public string EditNote
    {
        get => _editNote;
        set { if (SetProperty(ref _editNote, value ?? string.Empty)) RaiseMetadataChanged(); }
    }

    public bool HasMetadataEdit => BuildMetadataEdit() is not null;

    public string MetadataSummary
    {
        get
        {
            var edit = BuildMetadataEdit();
            if (edit is null)
                return string.Empty;

            var parts = new List<string>();
            if (edit.Name is not null) parts.Add("name");
            if (edit.Author is not null) parts.Add("author");
            if (edit.Description is not null) parts.Add("description");
            if (edit.Version is not null) parts.Add("version");
            if (edit.Website is not null) parts.Add("website");
            if (edit.ModTags is not null) parts.Add("tags");
            if (edit.Favorite is not null) parts.Add("favorite");
            if (edit.LocalTags is not null) parts.Add("local tags");
            if (edit.Note is not null) parts.Add("note");
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Returns the pending metadata edit for this mod, or null when nothing differs from the
    /// scanned values. Only changed fields are populated; everything else is left null so the
    /// writer leaves those values untouched.
    /// </summary>
    public ModMetadataEdit? BuildMetadataEdit()
    {
        var name = StringEdit(_editName, _originalName);
        var author = StringEdit(_editAuthor, _originalAuthor);
        var description = StringEdit(_editDescription, _originalDescription);
        var version = StringEdit(_editVersion, _originalVersion);
        var website = StringEdit(_editWebsite, _originalWebsite);
        var modTags = TagEdit(_editModTagsText, _originalModTags);
        bool? favorite = _editFavorite != _originalFavorite ? _editFavorite : null;
        var localTags = TagEdit(_editLocalTagsText, _originalLocalTags);
        var note = StringEdit(_editNote, _originalNote);

        if (name is null && author is null && description is null && version is null && website is null
            && modTags is null && favorite is null && localTags is null && note is null)
        {
            return null;
        }

        return new ModMetadataEdit(
            StableScanId,
            Name: name,
            Author: author,
            Description: description,
            Version: version,
            Website: website,
            ModTags: modTags,
            Favorite: favorite,
            LocalTags: localTags,
            Note: note);
    }

    /// <summary>Replaces the originals with a restored edit (used when resuming a saved session).</summary>
    public void ApplyRestoredMetadata(OrganizerSessionMetadataEdit edit)
    {
        _editName = edit.Name ?? _originalName;
        _editAuthor = edit.Author ?? _originalAuthor;
        _editDescription = edit.Description ?? _originalDescription;
        _editVersion = edit.Version ?? _originalVersion;
        _editWebsite = edit.Website ?? _originalWebsite;
        _editModTagsText = edit.ModTags is not null ? JoinTags(edit.ModTags) : JoinTags(_originalModTags);
        _editFavorite = edit.Favorite ?? _originalFavorite;
        _editLocalTagsText = edit.LocalTags is not null ? JoinTags(edit.LocalTags) : JoinTags(_originalLocalTags);
        _editNote = edit.Note ?? _originalNote;
        RaiseAllMetadata();
    }

    private void RaiseMetadataChanged()
    {
        RaisePropertyChanged(nameof(HasMetadataEdit));
        RaisePropertyChanged(nameof(MetadataSummary));
        MetadataEdited?.Invoke();
    }

    private void RaiseAllMetadata()
    {
        RaisePropertyChanged(nameof(EditName));
        RaisePropertyChanged(nameof(EditAuthor));
        RaisePropertyChanged(nameof(EditDescription));
        RaisePropertyChanged(nameof(EditVersion));
        RaisePropertyChanged(nameof(EditWebsite));
        RaisePropertyChanged(nameof(EditModTagsText));
        RaisePropertyChanged(nameof(Favorite));
        RaisePropertyChanged(nameof(EditLocalTagsText));
        RaisePropertyChanged(nameof(EditNote));
        RaisePropertyChanged(nameof(HasMetadataEdit));
        RaisePropertyChanged(nameof(MetadataSummary));
    }

    /// <summary>Raised when any metadata field changes so the owning view model can invalidate the dry run.</summary>
    public event Action? MetadataEdited;

    private static string? StringEdit(string current, string original)
        => string.Equals(current ?? string.Empty, original ?? string.Empty, StringComparison.Ordinal) ? null : (current ?? string.Empty);

    private static IReadOnlyList<string>? TagEdit(string currentText, IReadOnlyList<string> original)
    {
        var parsed = ParseTags(currentText);
        return parsed.SequenceEqual(original, StringComparer.Ordinal) ? null : parsed;
    }

    private static IReadOnlyList<string> ParseTags(string text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinTags(IReadOnlyList<string> tags) => string.Join(", ", tags);

    private static string DisplaySource(OrganizerProposalSource source)
        => source switch
        {
            OrganizerProposalSource.DeterministicRule => "Deterministic",
            OrganizerProposalSource.ImportedAi => "Imported AI",
            OrganizerProposalSource.PreservedCurrent => "Preserved current",
            OrganizerProposalSource.RestoredByUndo => "Restored by undo",
            _ => source.ToString(),
        };

}
