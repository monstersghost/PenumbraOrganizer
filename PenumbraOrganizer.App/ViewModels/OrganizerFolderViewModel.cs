namespace PenumbraOrganizer.App.ViewModels;

public sealed class OrganizerFolderViewModel : ObservableObject
{
    private int _directModCount;
    private int _descendantModCount;
    private bool _protected;

    public OrganizerFolderViewModel(string path)
    {
        Path = path;
        DisplayName = string.IsNullOrWhiteSpace(path) ? "(Root)" : path;
    }

    public string Path { get; }
    public string DisplayName { get; }

    public int DirectModCount
    {
        get => _directModCount;
        set => SetProperty(ref _directModCount, value);
    }

    public int DescendantModCount
    {
        get => _descendantModCount;
        set => SetProperty(ref _descendantModCount, value);
    }

    public bool Protected
    {
        get => _protected;
        set => SetProperty(ref _protected, value);
    }
}
