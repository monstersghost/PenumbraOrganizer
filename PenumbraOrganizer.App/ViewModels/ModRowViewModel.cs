namespace PenumbraOrganizer.App.ViewModels;

using PenumbraOrganizer.Core.Models;

public sealed class ModRowViewModel : ObservableObject
{
    private string _proposedVirtualFolder = string.Empty;
    private bool _protected;
    private string _proposalSource = "Preserved current";
    private string _effectiveCreator = string.Empty;
    private string _detectedType = string.Empty;

    public ModRowViewModel(ModScanResult mod)
    {
        StableScanId = mod.StableScanId;
        Proposal = new OrganizerModProposal
        {
            StableScanId = mod.StableScanId,
            Name = mod.Name,
            CurrentVirtualFolder = mod.CurrentVirtualFolder,
            ProposedVirtualFolder = mod.CurrentVirtualFolder,
            OriginalCreator = mod.Author,
            OrganizerCreatorLabel = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author,
            OrganizerTypeLabel = DetectType(mod),
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
        _detectedType = DetectType(mod);
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

    private static string DisplaySource(OrganizerProposalSource source)
        => source switch
        {
            OrganizerProposalSource.DeterministicRule => "Deterministic",
            OrganizerProposalSource.ImportedAi => "Imported AI",
            OrganizerProposalSource.PreservedCurrent => "Preserved current",
            OrganizerProposalSource.RestoredByUndo => "Restored by undo",
            _ => source.ToString(),
        };

    private static string DetectType(ModScanResult mod)
        => WorkbookCategoryCatalog.Detect(mod).Name;
}
