namespace PenumbraOrganizer.App.Dialogs;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using PenumbraOrganizer.App.ViewModels;
using PenumbraOrganizer.Core.Models;

public partial class ControlledTestDialog : Window, INotifyPropertyChanged
{
    public ControlledTestDialog(ControlledTestSetup setup)
    {
        InitializeComponent();
        _maximumSelectedModCount = setup.Options.MaximumSelectedModCount;
        Candidates = new ObservableCollection<ControlledTestCandidateRowViewModel>(
            setup.Candidates.Select(candidate => new ControlledTestCandidateRowViewModel(candidate, RefreshSelectionStatus)));
        _testFolderName = setup.Options.TestFolderName;
        _footerStatus = setup.Errors.Count > 0
            ? string.Join(Environment.NewLine, setup.Errors)
            : setup.Warnings.Count > 0
                ? string.Join(Environment.NewLine, setup.Warnings)
                : "A fresh controlled dry run is required after you confirm this selection.";
        DataContext = this;
        SelectedCandidate = Candidates.FirstOrDefault();
        RefreshSelectionStatus();
    }

    private readonly int _maximumSelectedModCount;
    private string _testFolderName;
    private string _selectionStatus = string.Empty;
    private string _footerStatus;
    private ControlledTestCandidateRowViewModel? _selectedCandidate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ControlledTestCandidateRowViewModel> Candidates { get; }

    public ControlledTestRequest? Request { get; private set; }

    public string TestFolderName
    {
        get => _testFolderName;
        set
        {
            if (_testFolderName == value)
                return;

            _testFolderName = value;
            foreach (var candidate in Candidates)
                candidate.ProposedTestFolder = value;
            RefreshSelectionStatus();
        }
    }

    public string SelectionStatus
    {
        get => _selectionStatus;
        private set
        {
            _selectionStatus = value;
            RaisePropertyChanged(nameof(SelectionStatus));
        }
    }

    public string FooterStatus
    {
        get => _footerStatus;
        private set
        {
            _footerStatus = value;
            RaisePropertyChanged(nameof(FooterStatus));
        }
    }

    public ControlledTestCandidateRowViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (_selectedCandidate == value)
                return;

            _selectedCandidate = value;
            RaisePropertyChanged(nameof(SelectedCandidate));
            RaisePropertyChanged(nameof(SelectedCandidateDetails));
        }
    }

    public string SelectedCandidateDetails
        => SelectedCandidate is null
            ? "Select a row to inspect its exact authoritative target record and read-only live path details."
            : $"Mod: {SelectedCandidate.ModName}{Environment.NewLine}" +
               $"Current folder: {SelectedCandidate.CurrentVirtualFolder}{Environment.NewLine}" +
               $"Current proposal: {SelectedCandidate.CurrentProposedFolder}{Environment.NewLine}" +
               $"Controlled test folder: {SelectedCandidate.ProposedTestFolder}{Environment.NewLine}" +
               $"Authoritative target: mod_data.db / LocalModData.Folder{Environment.NewLine}" +
               $"Target record key: {SelectedCandidate.RecordKey}{Environment.NewLine}" +
               $"Target path: {SelectedCandidate.TargetPath}{Environment.NewLine}" +
               $"Physical mod path: {SelectedCandidate.PhysicalDirectory}{Environment.NewLine}" +
               $"Selection status: {SelectedCandidate.Status} - {SelectedCandidate.StatusMessage}";

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = Candidates.Where(candidate => candidate.Selected).Select(candidate => candidate.StableScanId).ToArray();
        if (selectedIds.Length == 0)
        {
            MessageBox.Show(this, "Select at least one eligible mod for the controlled live test.", "Controlled Test Apply", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selectedIds.Length > _maximumSelectedModCount)
        {
            MessageBox.Show(this, $"Controlled Test Apply is limited to {_maximumSelectedModCount} mods by default.", "Controlled Test Apply", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Candidates.Any(candidate => candidate.Selected && !candidate.CanSelect))
        {
            MessageBox.Show(this, "One or more selected mods are not eligible for Controlled Test Apply.", "Controlled Test Apply", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Request = new ControlledTestRequest(TestFolderName, selectedIds, _maximumSelectedModCount);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RefreshSelectionStatus()
    {
        var selectedCount = Candidates.Count(candidate => candidate.Selected);
        var eligibleCount = Candidates.Count(candidate => candidate.CanSelect);
        SelectionStatus =
            $"{selectedCount} selected. Limit: {_maximumSelectedModCount}. " +
            $"{eligibleCount} eligible candidates are currently available.";
        RaisePropertyChanged(nameof(SelectedCandidateDetails));
    }

    private void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public sealed class ControlledTestCandidateRowViewModel : ObservableObject
    {
        private readonly Action _onSelectionChanged;
        private bool _selected;
        private string _proposedTestFolder;

        public ControlledTestCandidateRowViewModel(ControlledTestCandidate candidate, Action onSelectionChanged)
        {
            StableScanId = candidate.StableScanId;
            ModName = candidate.ModName;
            CurrentVirtualFolder = candidate.CurrentVirtualFolder;
            CurrentProposedFolder = candidate.CurrentProposedFolder;
            _proposedTestFolder = candidate.ProposedTestFolder;
            PhysicalDirectory = candidate.PhysicalDirectory;
            TargetPath = candidate.TargetPath;
            RecordKey = candidate.RecordKey;
            Status = candidate.Status.ToString();
            StatusMessage = candidate.StatusMessage;
            CanSelect = candidate.CanSelect;
            _onSelectionChanged = onSelectionChanged;
        }

        public string StableScanId { get; }
        public string ModName { get; }
        public string CurrentVirtualFolder { get; }
        public string CurrentProposedFolder { get; }
        public string PhysicalDirectory { get; }
        public string TargetPath { get; }
        public string RecordKey { get; }
        public string Status { get; }
        public string StatusMessage { get; }
        public bool CanSelect { get; }

        public string ProposedTestFolder
        {
            get => _proposedTestFolder;
            set => SetProperty(ref _proposedTestFolder, value);
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                var normalized = CanSelect && value;
                if (SetProperty(ref _selected, normalized))
                    _onSelectionChanged();
            }
        }
    }
}
