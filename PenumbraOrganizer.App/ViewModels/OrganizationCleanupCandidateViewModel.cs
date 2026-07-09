namespace PenumbraOrganizer.App.ViewModels;

public sealed class OrganizationCleanupCandidateViewModel : ObservableObject
{
    private bool _isSelected;

    public OrganizationCleanupCandidateViewModel(string path, bool isCustomized, string customizationSummary, bool isSelected)
    {
        Path = path;
        IsCustomized = isCustomized;
        Kind = isCustomized ? "Customized" : "Plain empty";
        CustomizationSummary = customizationSummary;
        _isSelected = isSelected;
    }

    public string Path { get; }
    public bool IsCustomized { get; }
    public string Kind { get; }
    public string CustomizationSummary { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
