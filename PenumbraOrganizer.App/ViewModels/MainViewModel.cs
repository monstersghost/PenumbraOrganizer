namespace PenumbraOrganizer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.App;
using PenumbraOrganizer.App.Commands;
using PenumbraOrganizer.App.Dialogs;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class MainViewModel : ObservableObject
{
    private readonly IPenumbraDiscoveryService _discoveryService;
    private readonly IPenumbraScanService _scanService;
    private readonly IPenumbraCompatibilityService _compatibilityService;
    private readonly IWorkbookWorkflowService _workbookWorkflowService;
    private readonly IOrganizerMutationService _organizerMutationService;
    private readonly IOrganizerProposalValidationService _organizerValidationService;
    private readonly IOrganizerSessionService _organizerSessionService;
    private readonly IDryRunPlanner _dryRunPlanner;
    private readonly IApplyService _applyService;
    private readonly IControlledLiveTestService _controlledLiveTestService;
    private readonly IRealInstallationValidationService _realInstallationValidationService;
    private readonly IOperationRecoveryService _operationRecoveryService;
    private readonly IOperationObservationService _operationObservationService;
    private readonly IDiagnosticExportService _diagnosticExportService;
    private readonly IOperationHistoryService _historyService;
    private readonly BackupsViewModel _backups;
    private readonly ILogger<MainViewModel> _logger;
    private PenumbraInstallation? _installation;
    private ScanInventory? _inventory;
    private WorkbookExportResult? _lastWorkbookExport;
    private WorkbookImportResult? _lastWorkbookImport;
    private readonly Stack<OrganizerHistoryEntry> _undoStack = new();
    private readonly Stack<OrganizerHistoryEntry> _redoStack = new();
    private readonly ObservableCollection<OrganizerFolder> _organizerFolders = new();
    private CancellationTokenSource? _autosaveCts;
    private string _detectionSummary = "Penumbra has not been detected yet.";
    private string _compatibilitySummary = "Compatibility status will appear after a scan.";
    private string _progressMessage = "Ready.";
    private string _searchText = string.Empty;
    private string _activityLog = "Welcome. This app starts in read-only scan mode.";
    private string _selectedStrategy = "Start Manually";
    private string _organizeFilter = "All Mods";
    private string _manualConfigPath = string.Empty;
    private string _newFolderName = string.Empty;
    private string _renameFolderName = string.Empty;
    private string _reviewFilter = "All";
    private string _applyUnavailableReason = "Create a dry run before applying changes.";
    private string _dryRunStatus = "Create a dry run to preview the exact Penumbra write target and expected result.";
    private string _backupStatus = "Create a verified backup after the dry run is current.";
    private string _applyChecklist = "Readiness checks will appear after a dry run is created.";
    private string _controlledTestStatus = "Controlled Test Apply is not configured yet.";
    private string _controlledTestSelectionSummary = "Choose up to 3 eligible mods before preparing a live test dry run.";
    private string _recoveryStatus = "Incomplete operations will appear here if backup, Apply, verification, or rollback is interrupted.";
    private OrganizerFolderViewModel? _selectedProposedFolder;
    private OrganizerValidationResult? _reviewValidation;
    private DryRunPlan? _currentDryRunPlan;
    private ApplyOperation? _preparedApplyOperation;
    private ApplyResult? _latestApplyResult;
    private RealInstallationValidationResult? _lastRealInstallationValidation;
    private ControlledTestRequest? _controlledTestRequest;
    private IReadOnlyList<IncompleteOperationRecord> _incompleteOperations = Array.Empty<IncompleteOperationRecord>();
    private int _changedProposalCount;
    private int _needsReviewCount;
    private int _installedModCount;
    private int _protectedModCount;
    private int _collectionCount;
    private int _warningCount;
    private string _installationValidationStatus = "Real-installation validation has not been run yet.";
    private string _workbookStatus = "Scan your mods, then export one workbook, edit mod type / protected / destination, and import it back for review.";
    private string _workbookImportStatus = WorkbookCategoryCatalog.BlankDestinationRule + " " + WorkbookCategoryCatalog.ReviewRule;
    private string _diagnosticStatus = "Diagnostic export is available without touching your mod assets or live databases.";
    private bool _showAdvancedTools;
    private bool _isBusy;
    private ICollectionView _filteredMods = null!;
    private ICollectionView _selectedFolderMods = null!;
    private ICollectionView _changedMods = null!;
    private bool _suspendOrganizerRefresh;
    private bool _suspendSelectedFolderRefresh;
    private bool _suspendCollectionViewRefresh;

    public MainViewModel(
        IPenumbraDiscoveryService discoveryService,
        IPenumbraScanService scanService,
        IPenumbraCompatibilityService compatibilityService,
        IWorkbookWorkflowService workbookWorkflowService,
        IOrganizerMutationService organizerMutationService,
        IOrganizerProposalValidationService organizerValidationService,
        IOrganizerSessionService organizerSessionService,
        IDryRunPlanner dryRunPlanner,
        IApplyService applyService,
        IControlledLiveTestService controlledLiveTestService,
        IRealInstallationValidationService realInstallationValidationService,
        IOperationRecoveryService operationRecoveryService,
        IOperationObservationService operationObservationService,
        IDiagnosticExportService diagnosticExportService,
        IOperationHistoryService historyService,
        BackupsViewModel backups,
        ILogger<MainViewModel> logger)
    {
        _discoveryService = discoveryService;
        _scanService = scanService;
        _compatibilityService = compatibilityService;
        _workbookWorkflowService = workbookWorkflowService;
        _organizerMutationService = organizerMutationService;
        _organizerValidationService = organizerValidationService;
        _organizerSessionService = organizerSessionService;
        _dryRunPlanner = dryRunPlanner;
        _applyService = applyService;
        _controlledLiveTestService = controlledLiveTestService;
        _realInstallationValidationService = realInstallationValidationService;
        _operationRecoveryService = operationRecoveryService;
        _operationObservationService = operationObservationService;
        _diagnosticExportService = diagnosticExportService;
        _historyService = historyService;
        _backups = backups;
        _logger = logger;

        Mods = new ObservableCollection<ModRowViewModel>();
        Collections = new ObservableCollection<CollectionInventory>();
        FolderTree = new ObservableCollection<VirtualFolderNode>();
        ProposedFolders = new ObservableCollection<OrganizerFolderViewModel>();
        SelectedOrganizerMods = new ObservableCollection<ModRowViewModel>();
        ReviewRows = new ObservableCollection<OrganizerValidationRow>();
        ScanWarnings = new ObservableCollection<string>();
        _filteredMods = CreateCollectionView(FilterMod);
        _selectedFolderMods = CreateCollectionView(FilterSelectedFolderMod);
        _changedMods = CreateCollectionView(item => item is ModRowViewModel mod && mod.IsChanged);

        DetectCommand = new AsyncRelayCommand(DetectAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => _installation is not null && !IsBusy);
        ChoosePenumbraConfigCommand = new AsyncRelayCommand(ChoosePenumbraConfigAsync, () => !IsBusy);
        ExportWorkbookCommand = new AsyncRelayCommand(ExportWorkbookAsync, () => _inventory is not null && !IsBusy);
        ImportWorkbookCommand = new AsyncRelayCommand(ImportWorkbookAsync, () => _inventory is not null && !IsBusy);
        OpenWorkbookCommand = new AsyncRelayCommand(OpenWorkbookAsync, () => _lastWorkbookExport is not null && File.Exists(_lastWorkbookExport.WorkbookPath));
        OpenWorkbookFolderCommand = new AsyncRelayCommand(OpenWorkbookFolderAsync, () => _lastWorkbookExport is not null && File.Exists(_lastWorkbookExport.WorkbookPath));
        ValidateInstallationCommand = new AsyncRelayCommand(ValidateInstallationAsync);
        CreateDiagnosticPackageCommand = new AsyncRelayCommand(CreateDiagnosticPackageAsync);
        SelectStrategyCommand = new RelayCommand(SelectStrategy);
        CreateProposedFolderCommand = new RelayCommand(_ => CreateProposedFolder(), _ => CanCreateProposedFolder());
        AssignSelectedToSelectedFolderCommand = new RelayCommand(_ => AssignSelectedToSelectedFolder(), _ => SelectedProposedFolder is not null && SelectedOrganizerMods.Count > 0);
        AssignAllVisibleToSelectedFolderCommand = new RelayCommand(_ => AssignAllVisibleToSelectedFolder(), _ => SelectedProposedFolder is not null && FilteredMods.Cast<object>().Any());
        ResetSelectedToCurrentFolderCommand = new RelayCommand(_ => ResetSelectedToCurrentFolder(), _ => SelectedOrganizerMods.Count > 0);
        ResetAllVisibleToCurrentFolderCommand = new RelayCommand(_ => ResetAllVisibleToCurrentFolder(), _ => FilteredMods.Cast<object>().Any());
        MarkSelectedProtectedCommand = new RelayCommand(_ => MarkSelectedProtected(), _ => SelectedOrganizerMods.Count > 0);
        UnprotectSelectedCommand = new RelayCommand(_ => UnprotectSelected(), _ => SelectedOrganizerMods.Count > 0);
        EditMetadataCommand = new RelayCommand(_ => EditSelectedMetadata(), _ => SelectedOrganizerMods.Count > 0);
        RenameFolderCommand = new RelayCommand(_ => RenameSelectedFolder(), _ => SelectedProposedFolder is not null && !string.IsNullOrWhiteSpace(RenameFolderName));
        DeleteEmptyFolderCommand = new RelayCommand(_ => DeleteSelectedEmptyFolder(), _ => SelectedProposedFolder is not null);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => _inventory is not null);
        ResumeLastSessionCommand = new AsyncRelayCommand(ResumeLastSessionAsync, () => _inventory is not null);
        DiscardSessionCommand = new AsyncRelayCommand(DiscardSessionAsync);
        RefreshReviewCommand = new RelayCommand(_ => RefreshReviewChanges());
        ConfigureControlledTestCommand = new AsyncRelayCommand(ConfigureControlledTestAsync, () => _installation is not null || _inventory is not null);
        ClearControlledTestCommand = new RelayCommand(_ => ClearControlledTest(), _ => _controlledTestRequest is not null);
        SetOrganizeFilterCommand = new RelayCommand(SetOrganizeFilter);
        CreateDryRunCommand = new AsyncRelayCommand(CreateDryRunAsync, () => _inventory is not null && !IsBusy);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => _currentDryRunPlan?.ApplyPermitted == true && _preparedApplyOperation is null && !IsBusy);
        ApplyVirtualFolderChangesCommand = new AsyncRelayCommand(ApplyVirtualFolderChangesAsync, () => _currentDryRunPlan?.ApplyPermitted == true && _preparedApplyOperation is not null && !IsBusy);
        BackupAndApplyCommand = new AsyncRelayCommand(BackupAndApplyAsync, () => _inventory is not null && !IsBusy);
        ReverifyIncompleteOperationCommand = new AsyncRelayCommand(ReverifyIncompleteOperationAsync, () => _incompleteOperations.Count > 0);
        ContinueIncompleteVerificationCommand = new AsyncRelayCommand(ContinueIncompleteVerificationAsync, () => _incompleteOperations.Any(operation => operation.RecommendedActions.Contains(RecoveryRecommendedAction.ContinueVerification)));
        RollbackIncompleteOperationCommand = new AsyncRelayCommand(RollbackIncompleteOperationAsync, () => _incompleteOperations.Any(operation => operation.RecommendedActions.Contains(RecoveryRecommendedAction.RollBack)));
        ViewIncompleteOperationCommand = new AsyncRelayCommand(ViewIncompleteOperationAsync, () => _incompleteOperations.Count > 0);
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
        SelectedOrganizerMods.CollectionChanged += (_, _) => RefreshSelectionCommandState();
        _ = InitializeSidebarStateAsync();
    }

    public ICommand DetectCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ChoosePenumbraConfigCommand { get; }
    public AsyncRelayCommand ExportWorkbookCommand { get; }
    public AsyncRelayCommand ImportWorkbookCommand { get; }
    public AsyncRelayCommand OpenWorkbookCommand { get; }
    public AsyncRelayCommand OpenWorkbookFolderCommand { get; }
    public AsyncRelayCommand ValidateInstallationCommand { get; }
    public AsyncRelayCommand CreateDiagnosticPackageCommand { get; }
    public RelayCommand SelectStrategyCommand { get; }
    public RelayCommand CreateProposedFolderCommand { get; }
    public RelayCommand AssignSelectedToSelectedFolderCommand { get; }
    public RelayCommand AssignAllVisibleToSelectedFolderCommand { get; }
    public RelayCommand ResetSelectedToCurrentFolderCommand { get; }
    public RelayCommand ResetAllVisibleToCurrentFolderCommand { get; }
    public RelayCommand MarkSelectedProtectedCommand { get; }
    public RelayCommand UnprotectSelectedCommand { get; }
    public RelayCommand EditMetadataCommand { get; }
    public RelayCommand RenameFolderCommand { get; }
    public RelayCommand DeleteEmptyFolderCommand { get; }
    public AsyncRelayCommand SaveSessionCommand { get; }
    public AsyncRelayCommand ResumeLastSessionCommand { get; }
    public AsyncRelayCommand DiscardSessionCommand { get; }
    public RelayCommand RefreshReviewCommand { get; }
    public AsyncRelayCommand ConfigureControlledTestCommand { get; }
    public RelayCommand ClearControlledTestCommand { get; }
    public RelayCommand SetOrganizeFilterCommand { get; }
    public AsyncRelayCommand CreateDryRunCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand ApplyVirtualFolderChangesCommand { get; }
    public AsyncRelayCommand BackupAndApplyCommand { get; }
    public AsyncRelayCommand ReverifyIncompleteOperationCommand { get; }
    public AsyncRelayCommand ContinueIncompleteVerificationCommand { get; }
    public AsyncRelayCommand RollbackIncompleteOperationCommand { get; }
    public AsyncRelayCommand ViewIncompleteOperationCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public ObservableCollection<ModRowViewModel> Mods { get; }
    public ObservableCollection<CollectionInventory> Collections { get; }
    public ObservableCollection<VirtualFolderNode> FolderTree { get; }
    public ObservableCollection<OrganizerFolderViewModel> ProposedFolders { get; }
    public ObservableCollection<ModRowViewModel> SelectedOrganizerMods { get; }
    public ObservableCollection<OrganizerValidationRow> ReviewRows { get; }
    public ObservableCollection<string> ScanWarnings { get; }
    public ICollectionView FilteredMods
    {
        get => _filteredMods;
        private set => SetProperty(ref _filteredMods, value);
    }

    public ICollectionView SelectedFolderMods
    {
        get => _selectedFolderMods;
        private set => SetProperty(ref _selectedFolderMods, value);
    }

    public ICollectionView ChangedMods
    {
        get => _changedMods;
        private set => SetProperty(ref _changedMods, value);
    }
    public BackupsViewModel Backups => _backups;

    public string DetectionSummary
    {
        get => _detectionSummary;
        private set => SetProperty(ref _detectionSummary, value);
    }

    public string CompatibilitySummary
    {
        get => _compatibilitySummary;
        private set => SetProperty(ref _compatibilitySummary, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshCollectionViews();
            }
        }
    }

    public string ActivityLog
    {
        get => _activityLog;
        private set => SetProperty(ref _activityLog, value);
    }

    public string PenumbraStateDirectory => _installation?.ConfigDirectory ?? "Not found";

    public string ModLibraryRoot => _installation?.ModRoot ?? "Not found";

    public string InstalledPenumbraVersion => _installation?.InstalledVersion ?? "Unknown";

    public bool InstallationFound => _installation is not null;

    public bool InstallationMissing => _installation is null;

    public string ManualConfigPath
    {
        get => _manualConfigPath;
        private set => SetProperty(ref _manualConfigPath, value);
    }

    public int InstalledModCount
    {
        get => _installedModCount;
        private set => SetProperty(ref _installedModCount, value);
    }

    public int ProtectedModCount
    {
        get => _protectedModCount;
        private set => SetProperty(ref _protectedModCount, value);
    }

    public int CollectionCount
    {
        get => _collectionCount;
        private set => SetProperty(ref _collectionCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    public string SelectedStrategy
    {
        get => _selectedStrategy;
        private set => SetProperty(ref _selectedStrategy, value);
    }

    public string OrganizeFilter
    {
        get => _organizeFilter;
        private set => SetProperty(ref _organizeFilter, value);
    }

    public string NewFolderName
    {
        get => _newFolderName;
        set
        {
            if (SetProperty(ref _newFolderName, value))
                CreateProposedFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public string RenameFolderName
    {
        get => _renameFolderName;
        set
        {
            if (SetProperty(ref _renameFolderName, value))
                RenameFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public OrganizerFolderViewModel? SelectedProposedFolder
    {
        get => _selectedProposedFolder;
        set
        {
            if (SetProperty(ref _selectedProposedFolder, value))
            {
                if (!_suspendSelectedFolderRefresh)
                    RefreshSelectedFolderView();
                RenameFolderName = value is null ? string.Empty : value.Path;
                RefreshSelectionCommandState();
            }
        }
    }

    public int ChangedProposalCount
    {
        get => _changedProposalCount;
        private set => SetProperty(ref _changedProposalCount, value);
    }

    public int NeedsReviewCount
    {
        get => _needsReviewCount;
        private set => SetProperty(ref _needsReviewCount, value);
    }

    public string ReviewFilter
    {
        get => _reviewFilter;
        set
        {
            if (SetProperty(ref _reviewFilter, value))
                RefreshReviewChanges();
        }
    }

    public string ApplyUnavailableReason
    {
        get => _applyUnavailableReason;
        private set => SetProperty(ref _applyUnavailableReason, value);
    }

    public string DryRunStatus
    {
        get => _dryRunStatus;
        private set => SetProperty(ref _dryRunStatus, value);
    }

    public string BackupStatus
    {
        get => _backupStatus;
        private set => SetProperty(ref _backupStatus, value);
    }

    public string ApplyChecklist
    {
        get => _applyChecklist;
        private set => SetProperty(ref _applyChecklist, value);
    }

    public string ControlledTestStatus
    {
        get => _controlledTestStatus;
        private set => SetProperty(ref _controlledTestStatus, value);
    }

    public string ControlledTestSelectionSummary
    {
        get => _controlledTestSelectionSummary;
        private set => SetProperty(ref _controlledTestSelectionSummary, value);
    }

    public string RecoveryStatus
    {
        get => _recoveryStatus;
        private set => SetProperty(ref _recoveryStatus, value);
    }

    public string InstallationValidationStatus
    {
        get => _installationValidationStatus;
        private set => SetProperty(ref _installationValidationStatus, value);
    }

    public string WorkbookStatus
    {
        get => _workbookStatus;
        private set => SetProperty(ref _workbookStatus, value);
    }

    public string WorkbookImportStatus
    {
        get => _workbookImportStatus;
        private set => SetProperty(ref _workbookImportStatus, value);
    }

    public string WorkbookPath => _lastWorkbookExport?.WorkbookPath ?? string.Empty;

    public string WorkbookCategorySummary
        => "Mod type codes: " + string.Join("  •  ", WorkbookCategoryCatalog.Definitions.Select(category => $"{category.Code} = {category.Name}"));

    public string DiagnosticStatus
    {
        get => _diagnosticStatus;
        private set => SetProperty(ref _diagnosticStatus, value);
    }

    public bool ShowAdvancedTools
    {
        get => _showAdvancedTools;
        set => SetProperty(ref _showAdvancedTools, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(IsNotBusy));
                ScanCommand.RaiseCanExecuteChanged();
                ChoosePenumbraConfigCommand.RaiseCanExecuteChanged();
                CreateDryRunCommand.RaiseCanExecuteChanged();
                CreateBackupCommand.RaiseCanExecuteChanged();
                ApplyVirtualFolderChangesCommand.RaiseCanExecuteChanged();
                BackupAndApplyCommand.RaiseCanExecuteChanged();
                ConfigureControlledTestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public int ReviewTotal => _reviewValidation?.Summary.TotalMods ?? 0;
    public int ReviewChanged => _reviewValidation?.Summary.Changed ?? 0;
    public int ReviewUnchanged => _reviewValidation?.Summary.Unchanged ?? 0;
    public int ReviewProtected => _reviewValidation?.Summary.Protected ?? 0;
    public int ReviewNeedsReview => _reviewValidation?.Summary.NeedsReview ?? 0;
    public int ReviewInvalid => _reviewValidation?.Summary.Invalid ?? 0;
    public int ReviewWarnings => _reviewValidation?.Summary.Warnings ?? 0;

    public string UndoDescription => _undoStack.Count == 0 ? "Undo" : "Undo: " + _undoStack.Peek().Description;
    public string RedoDescription => _redoStack.Count == 0 ? "Redo" : "Redo: " + _redoStack.Peek().Description;

    public async Task InitializeAsync()
    {
        if (_installation is null)
            await DetectAsync();
    }

    private async Task InitializeSidebarStateAsync()
    {
        try
        {
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sidebar initialization failed");
            StartupBootstrapLogger.RecordException("Sidebar initialization failed.", ex);
        }
    }

    private void RaiseInstallationChanged()
    {
        RaisePropertyChanged(nameof(PenumbraStateDirectory));
        RaisePropertyChanged(nameof(ModLibraryRoot));
        RaisePropertyChanged(nameof(InstalledPenumbraVersion));
        RaisePropertyChanged(nameof(InstallationFound));
        RaisePropertyChanged(nameof(InstallationMissing));
        ScanCommand.RaiseCanExecuteChanged();
        ConfigureControlledTestCommand.RaiseCanExecuteChanged();
    }

    private async Task RunBusyAsync(string progressMessage, Func<Task> action)
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ProgressMessage = progressMessage;
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DetectAsync()
    {
        await RunBusyAsync("Finding Penumbra.", async () =>
        {
            AppendLog("Looking for Penumbra in known XIVLauncher locations.");
            var result = await _discoveryService.DiscoverAsync(CancellationToken.None);
            _installation = result.Installations.FirstOrDefault();
            RaiseInstallationChanged();

            if (_installation is null)
            {
                DetectionSummary = "Penumbra could not be found automatically." + Environment.NewLine +
                                   "Choose Penumbra.json manually if your setup is in a different location.";
                if (result.Errors.Count > 0)
                    AppendLog(string.Join(Environment.NewLine, result.Errors));
                ProgressMessage = "Penumbra was not found.";
                return;
            }

            DetectionSummary = BuildHomeSummary(_installation, _inventory);
            AppendLog($"Detected Penumbra at {_installation.ConfigurationPath}");
            foreach (var warning in _installation.Warnings)
                AppendLog("Warning: " + warning);
            ProgressMessage = "Penumbra detected.";
        }).ContinueWith(task =>
        {
            if (task.Exception is null)
                return;

            var ex = task.Exception.GetBaseException();
            _logger.LogError(ex, "Penumbra detection failed");
            StartupBootstrapLogger.RecordException("Automatic Penumbra detection failed.", ex);
            DetectionSummary = "Penumbra could not be found automatically." + Environment.NewLine +
                               "Choose Penumbra.json manually if your setup is in a different location.";
            AppendLog("Detection failed: " + ex.Message);
            ProgressMessage = "Penumbra detection failed.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task ScanAsync()
    {
        if (_installation is null)
            return;

        await RunBusyAsync("Scanning your installed mods.", async () =>
        {
            var progress = new Progress<string>(message => ProgressMessage = message);
            AppendLog("Starting read-only scan.");
            _inventory = await _scanService.ScanAsync(_installation, progress, CancellationToken.None);
            var compatibility = await _compatibilityService.EvaluateAsync(_installation, _inventory, CancellationToken.None);

            Mods.Clear();
            foreach (var mod in _inventory.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new ModRowViewModel(mod);
                row.PropertyChanged += ModRowPropertyChanged;
                row.MetadataEdited += OnRowMetadataEdited;
                Mods.Add(row);
            }

            Collections.Clear();
            foreach (var collection in _inventory.Collections)
                Collections.Add(collection);

            FolderTree.Clear();
            _organizerFolders.Clear();
            foreach (var node in _inventory.CurrentFolderTree)
            {
                FolderTree.Add(node);
                if (!string.IsNullOrWhiteSpace(node.Path) && !_organizerFolders.Any(folder => folder.Path.Equals(node.Path, StringComparison.OrdinalIgnoreCase)))
                    _organizerFolders.Add(new OrganizerFolder(node.Path, ManuallyCreated: false, node.Protected));
            }

            SeedExistingEmptyFolders();

            ScanWarnings.Clear();
            foreach (var warning in _inventory.Warnings.Concat(_inventory.Mods.SelectMany(m => m.Warnings)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(w => w, StringComparer.OrdinalIgnoreCase))
                ScanWarnings.Add(warning);

            InstalledModCount = _inventory.Mods.Count;
            ProtectedModCount = _inventory.Mods.Count(m => m.Protected);
            CollectionCount = _inventory.Collections.Count;
            WarningCount = ScanWarnings.Count;
            _controlledTestRequest = null;
            _lastWorkbookExport = null;
            _lastWorkbookImport = null;
            RaisePropertyChanged(nameof(WorkbookPath));
            WorkbookStatus = "Scan complete. Export one workbook, edit mod type / protected / destination, and import it back for review.";
            WorkbookImportStatus = WorkbookCategoryCatalog.BlankDestinationRule + " " + WorkbookCategoryCatalog.ReviewRule;
            ControlledTestStatus = "Controlled Test Apply is not configured yet.";
            ControlledTestSelectionSummary = "Choose up to 3 eligible mods before preparing a live test dry run.";
            ClearControlledTestCommand.RaiseCanExecuteChanged();
            InvalidateDryRunState("Scan refreshed the live Penumbra snapshot.");
            ResetOrganizerHistory();
            RebuildProposedFolders();
            RefreshOrganizerViews();
            CompatibilitySummary = BuildCompatibilitySummary(compatibility);
            DetectionSummary = BuildHomeSummary(_installation, _inventory);
            ProgressMessage = $"Scan complete. {_inventory.Mods.Count} mods loaded.";
            AppendLog($"Scan finished with {_inventory.Mods.Count} mods and {ScanWarnings.Count} warnings.");
            ExportWorkbookCommand.RaiseCanExecuteChanged();
            ImportWorkbookCommand.RaiseCanExecuteChanged();
            OpenWorkbookCommand.RaiseCanExecuteChanged();
            OpenWorkbookFolderCommand.RaiseCanExecuteChanged();
            ConfigureControlledTestCommand.RaiseCanExecuteChanged();
            BackupAndApplyCommand.RaiseCanExecuteChanged();
            RaiseInstallationChanged();
        }).ContinueWith(task =>
        {
            if (task.Exception is null)
                return;

            var ex = task.Exception.GetBaseException();
            _logger.LogError(ex, "Penumbra scan failed");
            StartupBootstrapLogger.RecordException("Scan failed.", ex);
            ProgressMessage = "Scan failed.";
            AppendLog("Scan failed: " + ex.Message);
            MessageBox.Show(ToUserMessage(ex), "Scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task ChoosePenumbraConfigAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Penumbra.json",
            Filter = "Penumbra configuration|Penumbra.json|JSON files|*.json",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var installation = await _discoveryService.ValidateManualSelectionAsync(dialog.FileName, null, null, CancellationToken.None);
            if (installation is null)
            {
                MessageBox.Show(
                    "That file does not look like a usable Penumbra configuration. Choose Penumbra.json from XIVLauncher's pluginConfigs folder.",
                    "Penumbra not found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _installation = installation;
            ManualConfigPath = dialog.FileName;
            DetectionSummary = BuildHomeSummary(_installation, _inventory);
            ProgressMessage = "Penumbra selected manually.";
            AppendLog($"Selected Penumbra configuration manually: {dialog.FileName}");
            RaiseInstallationChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Penumbra selection failed");
            MessageBox.Show(ToUserMessage(ex), "Penumbra not found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportWorkbookAsync()
    {
        if (_inventory is null)
            return;

        try
        {
            await RunBusyAsync("Exporting your workbook.", async () =>
            {
                var workbookPath = ResolveWorkbookExportPath();
                _lastWorkbookExport = await _workbookWorkflowService.ExportAsync(_inventory, BuildOrganizationPreferences(), workbookPath, CancellationToken.None);
                _lastWorkbookImport = null;
                RaisePropertyChanged(nameof(WorkbookPath));
                WorkbookStatus = $"{_lastWorkbookExport.Summary}{Environment.NewLine}Saved to: {_lastWorkbookExport.WorkbookPath}";
                WorkbookImportStatus = $"Edit the 'mod type', 'protected', and 'destination' columns, save the file, then click Import Workbook. {WorkbookCategoryCatalog.BlankDestinationRule}";
                OpenWorkbookCommand.RaiseCanExecuteChanged();
                OpenWorkbookFolderCommand.RaiseCanExecuteChanged();
                AppendLog($"Exported workbook to {_lastWorkbookExport.WorkbookPath}.");
            });
        }
        catch (OperationCanceledException)
        {
            ProgressMessage = "Workbook export cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workbook export failed");
            WorkbookStatus = "Workbook export failed.";
            ProgressMessage = "Workbook export failed.";
            AppendLog("Workbook export failed: " + ex.Message);
            MessageBox.Show(ToUserMessage(ex), "Workbook export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ImportWorkbookAsync()
    {
        if (_inventory is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import Edited Workbook",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        try
        {
            await RunBusyAsync("Importing workbook destinations.", async () =>
            {
                _lastWorkbookImport = await _workbookWorkflowService.ImportAsync(dialog.FileName, _inventory!, CancellationToken.None);
            });

            if (_lastWorkbookImport is null)
                return;

            if (_lastWorkbookImport.Errors.Count > 0)
            {
                WorkbookImportStatus = _lastWorkbookImport.Summary;
                ProgressMessage = "Workbook import blocked.";
                AppendLog("Workbook import blocked: " + string.Join(" | ", _lastWorkbookImport.Errors.Take(8)));
                MessageBox.Show(
                    _lastWorkbookImport.Summary + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, _lastWorkbookImport.Errors.Take(12)),
                    "Workbook import blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ApplyWorkbookImport(_lastWorkbookImport);
            WorkbookImportStatus = _lastWorkbookImport.Summary;
            WorkbookStatus = $"Imported workbook: {Path.GetFileName(_lastWorkbookImport.WorkbookPath)}";
            ProgressMessage = "Workbook imported.";
            AppendLog($"Imported workbook from {_lastWorkbookImport.WorkbookPath}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workbook import failed");
            WorkbookImportStatus = "Workbook import failed.";
            ProgressMessage = "Workbook import failed.";
            AppendLog("Workbook import failed: " + ex.Message);
            MessageBox.Show(ToUserMessage(ex), "Workbook import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task OpenWorkbookAsync()
    {
        if (_lastWorkbookExport is null || !File.Exists(_lastWorkbookExport.WorkbookPath))
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastWorkbookExport.WorkbookPath,
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }

    private Task OpenWorkbookFolderAsync()
    {
        if (_lastWorkbookExport is null || !File.Exists(_lastWorkbookExport.WorkbookPath))
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetDirectoryName(_lastWorkbookExport.WorkbookPath)!,
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }

    private void ApplyWorkbookImport(WorkbookImportResult import)
    {
        _suspendOrganizerRefresh = true;
        try
        {
            var importById = import.Rows.ToDictionary(row => row.StableScanId, StringComparer.Ordinal);
            foreach (var mod in Mods)
            {
                if (!importById.TryGetValue(mod.StableScanId, out var row))
                    continue;

                var originalType = mod.DetectedType;
                mod.Protected = row.Protected;
                mod.DetectedType = row.ResolvedModType;
                mod.EffectiveCreator = string.IsNullOrWhiteSpace(row.Author) ? mod.Author : row.Author;
                mod.ProposalSource =
                    string.Equals(row.ResolvedDestination, mod.CurrentVirtualFolder, StringComparison.Ordinal) &&
                    string.Equals(row.ResolvedModType, originalType, StringComparison.OrdinalIgnoreCase)
                        ? "Preserved current"
                        : "Manual";
                mod.ProposedVirtualFolder = row.ResolvedDestination ?? mod.CurrentVirtualFolder;
                mod.Proposal.NeedsReview =
                    string.Equals(row.ResolvedModType, "Review", StringComparison.OrdinalIgnoreCase)
                    || mod.ProposedVirtualFolder.Contains("Review", StringComparison.OrdinalIgnoreCase);
            }

            _organizerFolders.Clear();
            foreach (var folder in Mods
                         .Select(mod => mod.ProposedVirtualFolder)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                _organizerFolders.Add(new OrganizerFolder(folder));
            }

            SeedExistingEmptyFolders();
        }
        finally
        {
            _suspendOrganizerRefresh = false;
        }

        InvalidateDryRunState("Workbook import updated the proposed destinations.");
        RefreshOrganizerViews();
        RefreshReviewChanges();
        BackupAndApplyCommand.RaiseCanExecuteChanged();
    }

    private string ResolveWorkbookExportPath()
    {
        var fileName = $"PenumbraOrganizer-Workbook-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.xlsx";
        var dialog = new SaveFileDialog
        {
            Title = "Choose Workbook Export Location",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = fileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            throw new OperationCanceledException("Workbook export was cancelled.");

        return dialog.FileName;
    }

    private async Task ValidateInstallationAsync()
    {
        try
        {
            if (_installation is null)
                await DetectAsync();
            if (_installation is null)
                return;

            if (_inventory is null)
                await ScanAsync();
            if (_inventory is null)
                return;

            ProgressMessage = "Validating the installed Penumbra state.";
            var snapshot = BuildActiveProposalSnapshot();
            var result = await _realInstallationValidationService.ValidateAsync(
                _installation,
                snapshot,
                new RealInstallationValidationOptions(Authorized: true, CreateVerifiedBackup: false),
                CancellationToken.None);

            _lastRealInstallationValidation = result;
            _currentDryRunPlan = result.Plan;
            _preparedApplyOperation = null;
            _latestApplyResult = null;
            InstallationValidationStatus = result.Summary;
            DryRunStatus = BuildDryRunStatus(result.Plan);
            BackupStatus = result.Preflight.Succeeded
                ? "Validation completed. Create Backup remains optional and must still be triggered explicitly."
                : "Validation found blockers. Fix them before creating a backup or applying changes.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
            ProgressMessage = "Real-installation validation finished.";
            AppendLog("Validation summary: " + result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Real-installation validation failed");
            InstallationValidationStatus = "Real-installation validation failed.";
            ProgressMessage = "Validation failed.";
            AppendLog("Validation failed: " + ex.Message);
        }
    }

    private async Task ConfigureControlledTestAsync()
    {
        try
        {
            if (_installation is null)
                await DetectAsync();
            if (_installation is null)
                return;

            if (_inventory is null)
                await ScanAsync();
            if (_inventory is null)
                return;

            var baseSnapshot = BuildBaseProposalSnapshot();
            var defaultFolder = _controlledTestRequest?.TestFolderName ?? "PenumbraOrganizer Test";
            var setup = await _controlledLiveTestService.BuildSetupAsync(
                _installation,
                _inventory,
                baseSnapshot,
                new ControlledTestOptions(defaultFolder),
                CancellationToken.None);

            var dialog = new ControlledTestDialog(setup)
            {
                Owner = Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() != true || dialog.Request is null)
                return;

            _controlledTestRequest = dialog.Request;
            ControlledTestStatus = $"Controlled Test Apply is active for {_controlledTestRequest.StableScanIds.Count} selected mod(s).";
            ControlledTestSelectionSummary =
                $"Test folder: {_controlledTestRequest.TestFolderName}{Environment.NewLine}" +
                $"Selected mods: {string.Join(", ", Mods.Where(mod => _controlledTestRequest.StableScanIds.Contains(mod.StableScanId, StringComparer.Ordinal)).Select(mod => mod.Name))}";
            ClearControlledTestCommand.RaiseCanExecuteChanged();
            InvalidateDryRunState("Controlled Test Apply selection changed. Create a fresh dry run.");
            await CreateDryRunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Controlled test configuration failed");
            ControlledTestStatus = "Controlled Test Apply could not be configured.";
            ControlledTestSelectionSummary = ex.Message;
            AppendLog("Controlled test setup failed: " + ex.Message);
        }
    }

    private void ClearControlledTest()
    {
        _controlledTestRequest = null;
        ControlledTestStatus = "Controlled Test Apply is not configured yet.";
        ControlledTestSelectionSummary = "Choose up to 3 eligible mods before preparing a live test dry run.";
        ClearControlledTestCommand.RaiseCanExecuteChanged();
        InvalidateDryRunState("Controlled Test Apply was cleared.");
    }

    private async Task CreateDiagnosticPackageAsync()
    {
        var confirmation =
            "The diagnostic package will include:\n" +
            "- app version\n" +
            "- Windows version\n" +
            "- Penumbra version\n" +
            "- redacted state and mod-library paths\n" +
            "- validation summaries\n" +
            "- operation summaries\n" +
            "- sanitized logs\n\n" +
            "It will not include mod assets, live databases, backups, or credentials.\n\n" +
            "Create the diagnostic package now?";
        if (MessageBox.Show(confirmation, "Create Diagnostic Package", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        try
        {
            ProgressMessage = "Creating diagnostic package.";
            var operations = await _historyService.GetOperationsAsync(CancellationToken.None);
            var result = await _diagnosticExportService.CreateAsync(
                new DiagnosticExportRequest(
                    typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "dev",
                    _installation,
                    _inventory,
                    _reviewValidation,
                    _currentDryRunPlan,
                    _preparedApplyOperation,
                    _latestApplyResult,
                    _lastRealInstallationValidation,
                    operations,
                    ActivityLog),
                CancellationToken.None);

            DiagnosticStatus = $"Diagnostic package created at {result.ZipPath}";
            ProgressMessage = "Diagnostic package created.";
            AppendLog($"Created diagnostic package at {result.ZipPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostic export failed");
            DiagnosticStatus = "Diagnostic export failed.";
            ProgressMessage = "Diagnostic export failed.";
            AppendLog("Diagnostic export failed: " + ex.Message);
        }
    }

    private string BuildCompatibilitySummary(CompatibilityReport compatibility)
    {
        if (compatibility.Warnings.Count == 0)
            return $"Penumbra version {compatibility.InstalledVersion}. Ready to organize your mods.";

        return $"Penumbra version {compatibility.InstalledVersion}.{Environment.NewLine}" +
               string.Join(Environment.NewLine, compatibility.Warnings);
    }

    private bool FilterMod(object item)
    {
        if (item is not ModRowViewModel mod)
            return false;

        if (OrganizeFilter == "Changed" && !mod.IsChanged)
            return false;

        if (OrganizeFilter == "Needs Review" &&
            !(mod.Proposal.NeedsReview || mod.DetectedType == "Unknown type" || mod.EffectiveCreator == "Unknown creator"))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var text = SearchText.Trim();
        return mod.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.Author.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.CurrentVirtualFolder.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.ProposedVirtualFolder.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.PhysicalDirectory.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void SetOrganizeFilter(object? parameter)
    {
        if (parameter is not string filter || string.IsNullOrWhiteSpace(filter))
            return;

        OrganizeFilter = filter;
        RefreshCollectionViews();
    }

    private bool FilterSelectedFolderMod(object item)
    {
        if (item is not ModRowViewModel mod || SelectedProposedFolder is null)
            return false;

        return string.Equals(mod.ProposedVirtualFolder, SelectedProposedFolder.Path, StringComparison.Ordinal);
    }

    private void SelectStrategy(object? parameter)
    {
        if (parameter is not string strategy || string.IsNullOrWhiteSpace(strategy))
            return;

        SelectedStrategy = strategy;
        InvalidateDryRunState("Organization strategy changed.");
        AppendLog($"Selected organization strategy: {strategy}.");
    }

    private bool CanCreateProposedFolder()
        => !string.IsNullOrWhiteSpace(NewFolderName);

    private void CreateProposedFolder()
    {
        var result = _organizerMutationService.CreateFolder(CurrentProposalRows(), _organizerFolders, ComposeNewFolderPath());
        ApplyMutationResult(result, pushHistory: true);
        NewFolderName = string.Empty;
    }

    private string ComposeNewFolderPath()
    {
        var child = NormalizeVirtualFolder(NewFolderName);
        if (SelectedProposedFolder is null || string.IsNullOrWhiteSpace(SelectedProposedFolder.Path))
            return child;

        return $"{SelectedProposedFolder.Path}/{child}";
    }

    private void AssignSelectedToSelectedFolder()
    {
        if (SelectedProposedFolder is null)
            return;

        var ids = SelectedOrganizerMods.Select(mod => mod.StableScanId).ToArray();
        var result = _organizerMutationService.AssignToFolder(CurrentProposalRows(), _organizerFolders, ids, SelectedProposedFolder.Path);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void AssignAllVisibleToSelectedFolder()
    {
        if (SelectedProposedFolder is null)
            return;

        var rows = FilteredMods.Cast<ModRowViewModel>().ToArray();
        if (rows.Length == 0)
            return;
        if (!ConfirmAllVisible($"Assign all {rows.Length} visible mods to {SelectedProposedFolder.Path}?"))
            return;

        var result = _organizerMutationService.AssignToFolder(CurrentProposalRows(), _organizerFolders, rows.Select(row => row.StableScanId).ToArray(), SelectedProposedFolder.Path);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void ResetSelectedToCurrentFolder()
    {
        var ids = SelectedOrganizerMods.Select(mod => mod.StableScanId).ToArray();
        var result = _organizerMutationService.ReturnToCurrent(CurrentProposalRows(), ids);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void ResetAllVisibleToCurrentFolder()
    {
        var rows = FilteredMods.Cast<ModRowViewModel>().ToArray();
        if (rows.Length == 0)
            return;
        if (!ConfirmAllVisible($"Return all {rows.Length} visible mods to their current folders?"))
            return;

        var result = _organizerMutationService.ReturnToCurrent(CurrentProposalRows(), rows.Select(row => row.StableScanId).ToArray());
        ApplyMutationResult(result, pushHistory: true);
    }

    private void MarkSelectedProtected()
    {
        var ids = SelectedOrganizerMods.Select(mod => mod.StableScanId).ToArray();
        var result = _organizerMutationService.Protect(CurrentProposalRows(), ids);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void UnprotectSelected()
    {
        var ids = SelectedOrganizerMods.Select(mod => mod.StableScanId).ToArray();
        var result = _organizerMutationService.Unprotect(CurrentProposalRows(), ids);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void RenameSelectedFolder()
    {
        if (SelectedProposedFolder is null)
            return;
        var oldPath = SelectedProposedFolder.Path;
        var newPath = ComposeSiblingPath(oldPath, RenameFolderName);
        var affectedMods = Mods.Count(mod => mod.ProposedVirtualFolder.Equals(oldPath, StringComparison.OrdinalIgnoreCase) || mod.ProposedVirtualFolder.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase));
        var affectedFolders = ProposedFolders.Count(folder => folder.Path.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase));
        if (MessageBox.Show($"Rename {oldPath} to {newPath}?{Environment.NewLine}{Environment.NewLine}This updates {affectedMods} mods and {affectedFolders} subfolders.", "Rename proposed folder", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        var result = _organizerMutationService.RenameFolder(CurrentProposalRows(), _organizerFolders, oldPath, newPath);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void DeleteSelectedEmptyFolder()
    {
        if (SelectedProposedFolder is null)
            return;
        var result = _organizerMutationService.DeleteEmptyFolder(CurrentProposalRows(), _organizerFolders, SelectedProposedFolder.Path);
        ApplyMutationResult(result, pushHistory: true);
    }

    private void ApplyMutationResult(OrganizerMutationResult result, bool pushHistory)
    {
        ProgressMessage = result.Message;
        if (!result.Succeeded)
            return;

        InvalidateDryRunState("Organizer proposals changed.");

        if (pushHistory && result.HistoryEntry is not null)
        {
            _undoStack.Push(result.HistoryEntry);
            _redoStack.Clear();
        }

        RefreshRowsFromProposals();
        AppendLog(result.Message + ".");
        RefreshOrganizerViews();
        DebouncedSaveSession();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;
        var action = _undoStack.Pop();
        _organizerMutationService.ApplyUndo(CurrentProposalRows(), _organizerFolders, action);
        _redoStack.Push(action);
        InvalidateDryRunState("Organizer proposals changed after Undo.");
        AppendLog("Undo: " + action.Description + ".");
        RefreshRowsFromProposals();
        RefreshOrganizerViews();
        DebouncedSaveSession();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var action = _redoStack.Pop();
        _organizerMutationService.ApplyRedo(CurrentProposalRows(), _organizerFolders, action);
        _undoStack.Push(action);
        InvalidateDryRunState("Organizer proposals changed after Redo.");
        AppendLog("Redo: " + action.Description + ".");
        RefreshRowsFromProposals();
        RefreshOrganizerViews();
        DebouncedSaveSession();
    }

    private void RebuildProposedFolders()
    {
        var selectedPath = SelectedProposedFolder?.Path;
        // Setting SelectedProposedFolder below would otherwise re-enter SelectedFolderMods.Refresh()
        // while RefreshOrganizerViews is mid-refresh; suppress it and let the caller refresh once.
        _suspendSelectedFolderRefresh = true;
        try
        {
            ProposedFolders.Clear();
            foreach (var folder in _organizerFolders.Select(folder => folder.Path)
                         .Concat(Mods.Select(mod => mod.ProposedVirtualFolder))
                         .Where(folder => !string.IsNullOrWhiteSpace(folder))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
            {
                ProposedFolders.Add(new OrganizerFolderViewModel(folder));
            }

            RecountProposedFolders();
            SelectedProposedFolder = ProposedFolders.FirstOrDefault(folder => string.Equals(folder.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                                     ?? ProposedFolders.FirstOrDefault();
        }
        finally
        {
            _suspendSelectedFolderRefresh = false;
        }
    }

    private void RecountProposedFolders()
    {
        foreach (var folder in ProposedFolders)
        {
            folder.DirectModCount = Mods.Count(mod => string.Equals(mod.ProposedVirtualFolder, folder.Path, StringComparison.Ordinal));
            folder.DescendantModCount = Mods.Count(mod => mod.ProposedVirtualFolder.StartsWith(folder.Path + "/", StringComparison.OrdinalIgnoreCase));
            folder.Protected = Mods.Any(mod => mod.Protected &&
                                               (string.Equals(mod.ProposedVirtualFolder, folder.Path, StringComparison.Ordinal) ||
                                                mod.ProposedVirtualFolder.StartsWith(folder.Path + "/", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void RefreshOrganizerViews()
    {
        RebuildProposedFolders();
        RefreshCollectionViews();
        RecountProposedFolders();
        ChangedProposalCount = Mods.Count(mod => mod.IsChanged);
        NeedsReviewCount = Mods.Count(mod => mod.ProposedVirtualFolder.Contains("Review", StringComparison.OrdinalIgnoreCase) ||
                                             mod.DetectedType == "Unknown type" ||
                                             mod.EffectiveCreator == "Unknown creator");
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        RefreshSelectionCommandState();
        RaisePropertyChanged(nameof(UndoDescription));
        RaisePropertyChanged(nameof(RedoDescription));
        RefreshReviewChanges();
    }

    private void ModRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspendOrganizerRefresh)
            return;

        if (e.PropertyName is nameof(ModRowViewModel.ProposedVirtualFolder) or nameof(ModRowViewModel.Protected))
            RefreshOrganizerViews();
    }

    // WPF's ListCollectionView.Refresh() can throw a NullReferenceException in PrepareLocalArray()
    // when a refresh is requested re-entrantly (e.g. a row's PropertyChanged fires mid-refresh and
    // rebuilds the proposed folders, which re-refreshes a view filtered over the same source).
    // Recreating the view recovers cleanly, so guard every refresh and rebuild on NRE.
    private void RefreshCollectionViews()
    {
        if (_suspendCollectionViewRefresh)
            return;

        try
        {
            FilteredMods.Refresh();
            SelectedFolderMods.Refresh();
            ChangedMods.Refresh();
        }
        catch (NullReferenceException ex)
        {
            _logger.LogWarning(ex, "Collection view refresh failed; rebuilding organizer views");
            RebuildCollectionViews();
            FilteredMods.Refresh();
            SelectedFolderMods.Refresh();
            ChangedMods.Refresh();
        }
    }

    private void RefreshSelectedFolderView()
    {
        if (_suspendCollectionViewRefresh)
            return;

        try
        {
            SelectedFolderMods.Refresh();
        }
        catch (NullReferenceException ex)
        {
            _logger.LogWarning(ex, "Selected folder view refresh failed; rebuilding organizer views");
            RebuildCollectionViews();
            SelectedFolderMods.Refresh();
        }
    }

    private void RebuildCollectionViews()
    {
        FilteredMods = CreateCollectionView(FilterMod);
        SelectedFolderMods = CreateCollectionView(FilterSelectedFolderMod);
        ChangedMods = CreateCollectionView(item => item is ModRowViewModel mod && mod.IsChanged);
    }

    private ICollectionView CreateCollectionView(Predicate<object> filter)
    {
        var view = new CollectionViewSource { Source = Mods }.View;
        view.Filter = filter;
        return view;
    }

    private void OnRowMetadataEdited()
    {
        // A staged metadata change makes any existing dry run stale; the edit is written only
        // through the standard Backup and Apply path.
        InvalidateDryRunState("Mod metadata edits changed.");
        DebouncedSaveSession();
    }

    private void EditSelectedMetadata()
    {
        var row = SelectedOrganizerMods.FirstOrDefault();
        if (row is null)
            return;

        if (SelectedOrganizerMods.Count > 1)
            ProgressMessage = $"Editing metadata for {row.Name}. Select a single mod to edit a different one.";

        var dialog = new ModMetadataDialog(row)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
            return;

        // The dialog committed edits live; surface them and refresh review state.
        AppendLog(row.HasMetadataEdit
            ? $"Staged metadata edits for {row.Name}: {row.MetadataSummary}."
            : $"Cleared metadata edits for {row.Name}.");
        RefreshReviewChanges();
        RefreshOrganizerViews();
    }

    private void ResetOrganizerHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RaisePropertyChanged(nameof(UndoDescription));
        RaisePropertyChanged(nameof(RedoDescription));
    }

    private void RefreshSelectionCommandState()
    {
        AssignSelectedToSelectedFolderCommand.RaiseCanExecuteChanged();
        AssignAllVisibleToSelectedFolderCommand.RaiseCanExecuteChanged();
        ResetSelectedToCurrentFolderCommand.RaiseCanExecuteChanged();
        ResetAllVisibleToCurrentFolderCommand.RaiseCanExecuteChanged();
        MarkSelectedProtectedCommand.RaiseCanExecuteChanged();
        UnprotectSelectedCommand.RaiseCanExecuteChanged();
        EditMetadataCommand.RaiseCanExecuteChanged();
        RenameFolderCommand.RaiseCanExecuteChanged();
        DeleteEmptyFolderCommand.RaiseCanExecuteChanged();
    }

    private IList<OrganizerModProposal> CurrentProposalRows()
        => Mods.Select(mod => mod.Proposal).ToList();

    private void RefreshRowsFromProposals()
    {
        // Suspend per-row refresh callbacks so the bulk update doesn't re-enter
        // RefreshOrganizerViews once per row (which crashes a view mid-refresh). Callers
        // refresh the views once afterward.
        _suspendOrganizerRefresh = true;
        try
        {
            foreach (var row in Mods)
                row.RefreshFromProposal();
        }
        finally
        {
            _suspendOrganizerRefresh = false;
        }
    }

    private static string ComposeSiblingPath(string oldPath, string newLeaf)
    {
        var normalizedLeaf = NormalizeVirtualFolder(newLeaf);
        var index = oldPath.LastIndexOf('/');
        return index < 0 ? normalizedLeaf : oldPath[..(index + 1)] + normalizedLeaf;
    }

    private static bool ConfirmAllVisible(string message)
        => MessageBox.Show(message, "Confirm visible mods", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private async Task SaveSessionAsync()
    {
        if (_inventory is null)
            return;

        await _organizerSessionService.SaveLastSessionAsync(BuildSessionDocument(), CancellationToken.None);
        ProgressMessage = "Organizer session saved.";
        AppendLog($"Saved organizer session to {_organizerSessionService.LastSessionPath}");
    }

    private async Task ResumeLastSessionAsync()
    {
        if (_inventory is null)
            return;

        var result = await _organizerSessionService.TryLoadLastSessionAsync(_inventory, CancellationToken.None);
        if (!result.CanResume || result.Session is null)
        {
            ProgressMessage = result.Message;
            MessageBox.Show(result.Message, "Saved organizer session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RestoreSession(result.Session);
        ResetOrganizerHistory();
        ProgressMessage = "Organizer session resumed. Undo history starts fresh from the reopened session.";
        AppendLog("Resumed saved organizer session. Undo history starts fresh.");
    }

    private async Task DiscardSessionAsync()
    {
        await _organizerSessionService.DiscardLastSessionAsync(CancellationToken.None);
        ProgressMessage = "Saved organizer session discarded.";
        AppendLog("Discarded saved organizer session.");
    }

    private void SeedExistingEmptyFolders()
    {
        if (_inventory is null)
            return;

        foreach (var path in _inventory.EmptyFolders)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (_organizerFolders.Any(folder => folder.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            _organizerFolders.Add(new OrganizerFolder(path, ManuallyCreated: true, Protected: false));
        }
    }

    private OrganizerSessionDocument BuildSessionDocument()
    {
        if (_inventory is null)
            throw new InvalidOperationException("A scan is required before saving a session.");

        return new OrganizerSessionDocument
        {
            ScanIdentity = OrganizerSessionService.BuildScanIdentity(_inventory),
            ScanTimestampUtc = _inventory.ScannedAtUtc,
            InstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(_inventory.Installation),
            InstalledPenumbraVersion = _inventory.Installation.InstalledVersion,
            OrganizationPreferences = BuildOrganizationPreferences(),
            ProposedFolders = _organizerFolders
                .Select(folder => new OrganizerSessionFolder(folder.Path, folder.ManuallyCreated, folder.Protected))
                .ToArray(),
            Mods = Mods.Select(row => new OrganizerSessionMod(
                row.StableScanId,
                row.CurrentVirtualFolder,
                row.Proposal.ProposedVirtualFolder,
                row.Proposal.Protected,
                row.Proposal.OrganizerCreatorLabel,
                row.Proposal.OrganizerTypeLabel,
                row.Proposal.Source,
                row.Proposal.NeedsReview)).ToArray(),
            MetadataEdits = Mods
                .Select(row => (row.StableScanId, Edit: row.BuildMetadataEdit()))
                .Where(item => item.Edit is not null)
                .Select(item => new OrganizerSessionMetadataEdit(
                    item.StableScanId,
                    item.Edit!.Name,
                    item.Edit.Author,
                    item.Edit.Description,
                    item.Edit.Version,
                    item.Edit.Website,
                    item.Edit.ModTags,
                    item.Edit.Favorite,
                    item.Edit.LocalTags,
                    item.Edit.Note))
                .ToArray(),
        };
    }

    private void RestoreSession(OrganizerSessionDocument session)
    {
        var savedById = session.Mods.ToDictionary(row => row.StableScanId, StringComparer.Ordinal);
        var editsById = session.MetadataEdits.ToDictionary(edit => edit.StableScanId, StringComparer.Ordinal);
        foreach (var row in Mods)
        {
            if (savedById.TryGetValue(row.StableScanId, out var saved))
            {
                row.Proposal.ProposedVirtualFolder = saved.ProposedVirtualFolder;
                row.Proposal.Protected = saved.Protected;
                row.Proposal.OrganizerCreatorLabel = saved.OrganizerCreatorLabel;
                row.Proposal.OrganizerTypeLabel = saved.OrganizerTypeLabel;
                row.Proposal.Source = saved.ProposalSource;
                row.Proposal.NeedsReview = saved.NeedsReview;
            }

            if (editsById.TryGetValue(row.StableScanId, out var savedEdit))
                row.ApplyRestoredMetadata(savedEdit);
        }

        _organizerFolders.Clear();
        foreach (var folder in session.ProposedFolders)
            _organizerFolders.Add(new OrganizerFolder(folder.Path, folder.ManuallyCreated, folder.Protected));

        // Never silently drop empty folders that still exist on disk just because an older
        // saved session predates them; re-seed any that the restored session is missing.
        SeedExistingEmptyFolders();

        InvalidateDryRunState("A saved organizer session was restored.");
        RefreshRowsFromProposals();
        RefreshOrganizerViews();
    }

    private async Task CreateDryRunAsync()
    {
        if (_installation is null || _inventory is null)
            return;

        try
        {
            ProgressMessage = "Creating dry run.";
            var snapshot = BuildActiveProposalSnapshot();
            _currentDryRunPlan = await _dryRunPlanner.CreatePlanAsync(_installation, _inventory, snapshot, CancellationToken.None);
            _preparedApplyOperation = null;
            _latestApplyResult = null;
            DryRunStatus = BuildDryRunStatus(_currentDryRunPlan);
            BackupStatus = _currentDryRunPlan.ApplyPermitted
                ? "Dry run is current. Create Backup will build a verified backup package and rollback record before any live write."
                : "Dry run found blockers. Fix them before creating a backup package.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            ProgressMessage = "Dry run created.";
            AppendLog($"Created a fresh review plan with {_currentDryRunPlan.Summary.WriteOperationCount} writable target(s).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dry run creation failed");
            _currentDryRunPlan = null;
            _preparedApplyOperation = null;
            _latestApplyResult = null;
            DryRunStatus = "Review preparation failed.";
            BackupStatus = "Backup and Apply is unavailable until the review is refreshed.";
            ApplyChecklist = "Refresh the review and try again.";
            ApplyUnavailableReason = ToUserMessage(ex);
            RefreshDryRunCommandState();
            ProgressMessage = "Dry run failed.";
            AppendLog("Dry run failed: " + ex.Message);
        }
    }

    private async Task CreateBackupAsync()
    {
        if (_installation is null || _currentDryRunPlan is null)
            return;

        try
        {
            ProgressMessage = "Creating verified backup.";
            var snapshot = BuildActiveProposalSnapshot();
            _preparedApplyOperation = await _applyService.PrepareAsync(_currentDryRunPlan, _installation, snapshot, CancellationToken.None);
            BackupStatus = $"Verified backup ready. Operation: {_preparedApplyOperation.OperationId}";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();
            ProgressMessage = "Verified backup ready.";
            AppendLog($"Prepared apply operation {_preparedApplyOperation.OperationId} with verified backup.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup preparation failed");
            _preparedApplyOperation = null;
            BackupStatus = "Backup preparation failed.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = ex.Message;
            RefreshDryRunCommandState();
            ProgressMessage = "Backup preparation failed.";
            AppendLog("Backup preparation failed: " + ex.Message);
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();
        }
    }

    private async Task BackupAndApplyAsync()
    {
        if (_installation is null)
            await DetectAsync();
        if (_installation is null || _inventory is null)
            return;

        RefreshReviewChanges();

        try
        {
            await RunBusyAsync("Refreshing your review plan.", async () =>
            {
                var snapshot = BuildActiveProposalSnapshot();
                _currentDryRunPlan = await _dryRunPlanner.CreatePlanAsync(_installation, _inventory, snapshot, CancellationToken.None);
                _preparedApplyOperation = null;
                _latestApplyResult = null;
                DryRunStatus = BuildDryRunStatus(_currentDryRunPlan);
                BackupStatus = "Ready to create a verified backup and apply your reviewed changes.";
                ApplyChecklist = BuildApplyChecklist();
                ApplyUnavailableReason = BuildApplyUnavailableReason();
                RefreshDryRunCommandState();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup and Apply review preparation failed");
            MessageBox.Show(ToUserMessage(ex), "Backup and Apply blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_currentDryRunPlan is null)
            return;

        if (!_currentDryRunPlan.ApplyPermitted || _currentDryRunPlan.Validation.Status != DryRunPlanValidationStatus.Valid)
        {
            BackupStatus = "Backup and Apply is blocked until the review issues are fixed.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            MessageBox.Show(
                BuildPlanBlockedMessage(_currentDryRunPlan),
                "Backup and Apply blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await RunBusyAsync("Creating a verified backup.", async () =>
            {
                var snapshot = BuildActiveProposalSnapshot();
                _currentDryRunPlan = await _dryRunPlanner.CreatePlanAsync(_installation, _inventory, snapshot, CancellationToken.None);
                if (!_currentDryRunPlan.ApplyPermitted || _currentDryRunPlan.Validation.Status != DryRunPlanValidationStatus.Valid)
                    throw new InvalidOperationException("The Penumbra data changed after the review was generated.");

                _preparedApplyOperation = await _applyService.PrepareAsync(_currentDryRunPlan, _installation, snapshot, CancellationToken.None);
                BackupStatus = $"Verified backup ready in {_preparedApplyOperation.OperationId:N}.";
                ApplyChecklist = BuildApplyChecklist();
                ApplyUnavailableReason = BuildApplyUnavailableReason();
                RefreshDryRunCommandState();
            });

            var confirmation = new ApplyTestConfirmationDialog(
                title: "Apply Virtual-Folder Changes?",
                heading: "Apply Virtual-Folder Changes?",
                description: "A verified backup is ready. If you continue, the app will update Penumbra's virtual-folder database without moving any physical mod files.",
                confirmationText: BuildApplyConfirmationMessage(),
                confirmButtonText: "Backup and Apply")
            {
                Owner = Application.Current.MainWindow,
            };
            if (confirmation.ShowDialog() != true)
            {
                ProgressMessage = "Backup and Apply cancelled.";
                BackupStatus = "The verified backup was kept. No changes were written.";
                await _backups.RefreshAsync();
                await RefreshRecoveryStatusAsync();
                return;
            }

            await RunBusyAsync("Applying your changes and verifying the result.", async () =>
            {
                var snapshot = BuildActiveProposalSnapshot();
                _latestApplyResult = await _applyService.ApplyAsync(_currentDryRunPlan, _preparedApplyOperation!, _installation, snapshot, CancellationToken.None);
            });

            var details = _preparedApplyOperation is null
                ? null
                : await _historyService.TryLoadOperationAsync(_preparedApplyOperation.OperationId, CancellationToken.None);

            BackupStatus = BuildApplyResultSummary(_latestApplyResult!, details?.PostApplyVerification);
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();

            var title = details?.PostApplyVerification?.Succeeded == true ? "Organization completed" : "Organization finished with warnings";
            MessageBox.Show(
                BuildApplyCompletionMessage(_latestApplyResult!, details?.PostApplyVerification),
                title,
                MessageBoxButton.OK,
                details?.PostApplyVerification?.Succeeded == true ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup and Apply failed");
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();

            var details = _preparedApplyOperation is null
                ? null
                : await _historyService.TryLoadOperationAsync(_preparedApplyOperation.OperationId, CancellationToken.None);
            var message = BuildApplyFailureMessage(ex, details);
            BackupStatus = details?.Operation.RollbackAvailable == true
                ? "Apply stopped after a partial live change. Rollback is available from Backups."
                : "Apply stopped before any live change could be confirmed.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = ToUserMessage(ex);
            RefreshDryRunCommandState();

            MessageBox.Show(message, "Backup and Apply failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ApplyVirtualFolderChangesAsync()
    {
        if (_installation is null || _currentDryRunPlan is null || _preparedApplyOperation is null)
            return;

        var confirmation = new ApplyTestConfirmationDialog(
            title: "Apply Virtual-Folder Changes?",
            heading: _controlledTestRequest is null ? "Apply Virtual-Folder Changes?" : "Apply Controlled Test Changes?",
            description: _controlledTestRequest is null
                ? "This updates Penumbra's virtual-folder database only. Physical mod folders and files stay where they are."
                : "This updates only the selected Penumbra virtual-folder records for a controlled test. Physical mod folders and files stay where they are.",
            confirmationText: BuildApplyConfirmationMessage(),
            confirmButtonText: _controlledTestRequest is null ? "Backup and Apply" : "Apply Test Changes")
        {
            Owner = Application.Current.MainWindow,
        };
        if (confirmation.ShowDialog() != true)
        {
            BackupStatus = "Apply was cancelled before any live write.";
            return;
        }

        try
        {
            ProgressMessage = "Applying virtual-folder changes.";
            var snapshot = BuildActiveProposalSnapshot();
            _latestApplyResult = await _applyService.ApplyAsync(_currentDryRunPlan, _preparedApplyOperation, _installation, snapshot, CancellationToken.None);
            var details = await _historyService.TryLoadOperationAsync(_preparedApplyOperation.OperationId, CancellationToken.None);
            BackupStatus = BuildApplyResultSummary(_latestApplyResult, details?.PostApplyVerification);
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();
            ProgressMessage = $"Apply finished: {_latestApplyResult.Status}.";
            AppendLog($"Apply operation {_preparedApplyOperation.OperationId} finished with status {_latestApplyResult.Status}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply failed");
            BackupStatus = "Apply failed before any live Penumbra write could be confirmed.";
            ApplyUnavailableReason = ex.Message;
            ApplyChecklist = BuildApplyChecklist();
            RefreshDryRunCommandState();
            ProgressMessage = "Apply failed.";
            AppendLog("Apply failed: " + ex.Message);
            await _backups.RefreshAsync();
            await RefreshRecoveryStatusAsync();
        }
    }

    private ProposalSnapshot BuildBaseProposalSnapshot()
    {
        if (_inventory is null)
            throw new InvalidOperationException("A scan is required before creating a dry run.");

        var preferences = BuildOrganizationPreferences();
        var proposals = CurrentProposalRows()
            .Select(proposal => new OrganizerModProposal
            {
                StableScanId = proposal.StableScanId,
                Name = proposal.Name,
                CurrentVirtualFolder = proposal.CurrentVirtualFolder,
                ProposedVirtualFolder = proposal.ProposedVirtualFolder,
                OriginalCreator = proposal.OriginalCreator,
                OrganizerCreatorLabel = proposal.OrganizerCreatorLabel,
                OrganizerTypeLabel = proposal.OrganizerTypeLabel,
                Protected = proposal.Protected,
                OriginalProtected = proposal.OriginalProtected,
                Source = proposal.Source,
                NeedsReview = proposal.NeedsReview,
            })
            .ToArray();
        var folders = _organizerFolders.Select(folder => folder with { }).ToArray();
        var metadataEdits = Mods
            .Select(row => row.BuildMetadataEdit())
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();
        var validation = _organizerValidationService.Validate(_inventory, proposals, folders, preferences);
        var session = BuildSessionDocument();
        return new ProposalSnapshot(
            OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences, metadataEdits),
            OrganizerSessionService.BuildSessionIdentity(session),
            preferences,
            proposals,
            folders,
            validation,
            metadataEdits);
    }

    private ProposalSnapshot BuildActiveProposalSnapshot()
    {
        var baseSnapshot = BuildBaseProposalSnapshot();
        if (_controlledTestRequest is null || _installation is null || _inventory is null)
            return baseSnapshot;

        return _controlledLiveTestService.BuildControlledSnapshot(_installation, _inventory, baseSnapshot, _controlledTestRequest);
    }

    private void InvalidateDryRunState(string reason)
    {
        _currentDryRunPlan = null;
        _preparedApplyOperation = null;
        _latestApplyResult = null;
        DryRunStatus = "Your review needs to be refreshed before applying changes.";
        BackupStatus = reason;
        ApplyChecklist = "Review changes, then click Backup and Apply when you are ready.";
        ApplyUnavailableReason = BuildApplyUnavailableReason();
        RefreshDryRunCommandState();
    }

    private void RefreshDryRunCommandState()
    {
        CreateDryRunCommand.RaiseCanExecuteChanged();
        CreateBackupCommand.RaiseCanExecuteChanged();
        ApplyVirtualFolderChangesCommand.RaiseCanExecuteChanged();
        BackupAndApplyCommand.RaiseCanExecuteChanged();
    }

    private string BuildDryRunStatus(DryRunPlan plan)
    {
        if (plan.FileChanges.Count == 0)
            return "No supported Penumbra changes need to be written.";

        var controlledSummary = _controlledTestRequest is null
            ? "Workflow: standard organization review"
            : $"Workflow: Controlled Test Apply ({_controlledTestRequest.StableScanIds.Count} selected mod(s) -> {_controlledTestRequest.TestFolderName})";
        return
            $"{controlledSummary}{Environment.NewLine}" +
            $"Authoritative targets: {DescribeWriteTargets(plan.FileChanges)}{Environment.NewLine}" +
            $"Affected mods: {plan.Summary.AffectedModCount}{Environment.NewLine}" +
            $"Write operations: {plan.FileChanges.Count}{Environment.NewLine}" +
            $"Status: {plan.Validation.Status}";
    }

    private static string DescribeWriteTargets(IReadOnlyList<DryRunFileChange> fileChanges)
    {
        var parts = new List<string>();
        var sortCount = fileChanges.Count(change => change.WriteTargetKind == PenumbraWriteTargetKind.SortOrderJson);
        var metaCount = fileChanges.Count(change => change.WriteTargetKind == PenumbraWriteTargetKind.ModMetaJson);
        var localCount = fileChanges.Count(change => change.WriteTargetKind == PenumbraWriteTargetKind.LocalModDataJson);
        if (sortCount > 0) parts.Add("sort_order.json (organization)");
        if (metaCount > 0) parts.Add($"{metaCount} meta.json file(s)");
        if (localCount > 0) parts.Add($"{localCount} mod_data file(s)");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private string BuildApplyChecklist()
    {
        var preflight = _preparedApplyOperation?.Preflight ?? _lastRealInstallationValidation?.Preflight;
        var changedEntries = _currentDryRunPlan?.Entries.Where(entry => entry.ValidationStatus == OrganizerRowStatus.ValidChange).ToArray() ?? Array.Empty<DryRunPlanEntry>();
        var checklist = new List<string>
        {
            ChecklistLine("Proposals valid", _reviewValidation is not null && _reviewValidation.Errors.Count == 0),
            ChecklistLine("Protected mods unchanged", _reviewValidation is not null && _reviewValidation.Rows.All(row => row.Status != OrganizerRowStatus.BlockedProtected)),
            ChecklistLine("Penumbra version unchanged", _currentDryRunPlan?.Validation.InvalidationReasons.Contains(PlanInvalidationReason.PenumbraVersionChanged) != true),
            ChecklistLine("Source state unchanged", _currentDryRunPlan?.Validation.InvalidationReasons.Contains(PlanInvalidationReason.SourceFileHashChanged) != true),
            ChecklistLine("All target records mapped", changedEntries.All(entry => entry.RequiresWrite)),
            ChecklistLine("Permissions available", preflight?.Succeeded == true),
            ChecklistLine("Game closed", preflight?.BlockingProcesses.Count == 0),
            ChecklistLine("Backup verified", _preparedApplyOperation is not null),
            ChecklistLine("Rollback prepared", _preparedApplyOperation is not null),
            ChecklistLine("Review plan current", _currentDryRunPlan?.Validation.Status == DryRunPlanValidationStatus.Valid),
        };

        if (_latestApplyResult is not null)
            checklist.Add($"Latest apply result: {_latestApplyResult.Status}");

        return string.Join(Environment.NewLine, checklist);
    }

    private string BuildApplyUnavailableReason()
    {
        if (_reviewValidation is null)
            return "Scan your mods and review the proposed changes first.";

        if (_currentDryRunPlan is null)
            return "Review changes, then click Backup and Apply.";

        if (_currentDryRunPlan.Validation.Status != DryRunPlanValidationStatus.Valid)
            return "The review plan is out of date. Scan again before applying changes.";

        if (_preparedApplyOperation is null)
            return "A verified backup will be created automatically before Apply.";

        if (_preparedApplyOperation.Preflight.BlockingProcesses.Count > 0)
            return $"Apply is blocked while these processes are running: {string.Join(", ", _preparedApplyOperation.Preflight.BlockingProcesses)}";

        if (_latestApplyResult is not null)
            return _latestApplyResult.RollbackAvailable
                ? "Apply finished. Rollback is available from the Backups screen."
                : $"Apply finished with status {_latestApplyResult.Status}.";

        return _preparedApplyOperation.Preflight.Succeeded
            ? "Ready to write supported virtual-folder changes to sort_order.json."
            : string.Join(Environment.NewLine, _preparedApplyOperation.Preflight.Errors);
    }

    private static string ChecklistLine(string label, bool passed)
        => $"{(passed ? "[x]" : "[ ]")} {label}";

    private string BuildApplyConfirmationMessage()
    {
        var fileChanges = _currentDryRunPlan?.FileChanges ?? Array.Empty<DryRunFileChange>();
        var operationFolder = _preparedApplyOperation is null
            ? "Backup not prepared"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PenumbraOrganizer",
                "Backups",
                _preparedApplyOperation.OperationId.ToString("N"));
        var changedRows = _reviewValidation?.ValidChanges ?? Array.Empty<OrganizerValidationRow>();
        var protectedCount = _reviewValidation?.Summary.Protected ?? 0;
        var examples = changedRows
            .Take(8)
            .Select(row => $"{row.ModName}: {row.CurrentVirtualFolder} -> {row.ProposedVirtualFolder}")
            .ToArray();
        var exampleBlock = examples.Length == 0
            ? "No folder moves were found."
            : string.Join(Environment.NewLine, examples) +
              (changedRows.Count > examples.Length ? $"{Environment.NewLine}+ {changedRows.Count - examples.Length} more" : string.Empty);

        var metadataMods = MetadataEditedMods();
        var metadataBlock = metadataMods.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}Metadata edits ({metadataMods.Count} mod(s)):{Environment.NewLine}" +
              string.Join(Environment.NewLine, metadataMods.Take(8).Select(row => $"{row.Name}: {row.MetadataSummary}")) +
              (metadataMods.Count > 8 ? $"{Environment.NewLine}+ {metadataMods.Count - 8} more" : string.Empty);

        return
            $"{changedRows.Count} mod(s) will be reorganized.{Environment.NewLine}" +
            $"{metadataMods.Count} mod(s) will have metadata edits applied.{Environment.NewLine}" +
            $"{protectedCount} protected mod(s) will remain unchanged.{Environment.NewLine}{Environment.NewLine}" +
            $"Planned folder changes:{Environment.NewLine}{exampleBlock}{metadataBlock}{Environment.NewLine}{Environment.NewLine}" +
            $"Authoritative targets: {DescribeWriteTargets(fileChanges)}{Environment.NewLine}" +
            $"Write operations: {fileChanges.Count}{Environment.NewLine}" +
            $"Backup location: {operationFolder}{Environment.NewLine}" +
            $"Rollback readiness: {(_preparedApplyOperation is null ? "Will be prepared before writing" : "Prepared")}{Environment.NewLine}{Environment.NewLine}" +
            "Physical mod folders and mod files will not be moved." + Environment.NewLine +
            "FFXIV must be closed before Apply.";
    }

    private IReadOnlyList<ModRowViewModel> MetadataEditedMods()
        => Mods.Where(row => row.HasMetadataEdit).ToArray();

    private string BuildApplyResultSummary(ApplyResult applyResult, PostApplyVerificationResult? verification)
    {
        var anyWriteCompleted = applyResult.Files.Any(file => file.WriteCompleted);
        var category = verification switch
        {
            { Succeeded: true, Warnings.Count: > 0 } => "Applied with verification warnings",
            { Succeeded: true } => "Applied and verified",
            _ when applyResult.Status == ApplyStatus.PartiallyCompleted => "Partially applied",
            _ when applyResult.Status == ApplyStatus.Failed && anyWriteCompleted => "Failed after partial write",
            _ when applyResult.Status == ApplyStatus.Failed => "Failed before write",
            _ => applyResult.Status.ToString(),
        };

        var rollbackLine = applyResult.RollbackAvailable
            ? "Rollback available from the Backups screen."
            : "Rollback is not available because no live write was confirmed.";
        var verificationLine = verification is null
            ? "Post-Apply verification is still pending."
            : verification.Succeeded
                ? "The updated records were re-read and verified against the plan."
                : string.Join(" ", verification.Errors.Take(2));
        return $"{category}. {verificationLine} {rollbackLine}";
    }

    private string BuildApplyCompletionMessage(ApplyResult applyResult, PostApplyVerificationResult? verification)
    {
        var changedCount = _reviewValidation?.Summary.Changed ?? applyResult.Files.Count(file => file.WriteCompleted);
        var protectedCount = _reviewValidation?.Summary.Protected ?? 0;
        var verificationLine = verification?.Succeeded == true
            ? "Result verified"
            : "Verification finished with warnings";

        return
            $"Organization completed{Environment.NewLine}{Environment.NewLine}" +
            $"{changedCount} mod(s) updated{Environment.NewLine}" +
            $"{protectedCount} protected mod(s) unchanged{Environment.NewLine}" +
            $"Backup verified{Environment.NewLine}" +
            $"{verificationLine}{Environment.NewLine}{Environment.NewLine}" +
            "Your physical mod files were not moved." + Environment.NewLine +
            "If the new folders do not appear right away in Penumbra, reload the plugin or restart XIVLauncher.";
    }

    private string BuildApplyFailureMessage(Exception exception, OperationPackageDetails? details)
    {
        var summary = ToUserMessage(exception);
        if (details is null)
            return $"{summary}{Environment.NewLine}{Environment.NewLine}No files were changed.";

        if (details.Operation.RollbackAvailable)
            return $"{summary}{Environment.NewLine}{Environment.NewLine}Some changes may have been written. Use Backups to restore the verified backup.";

        if (details.Operation.VerificationStatus == OperationVerificationStatus.Verified)
            return $"{summary}{Environment.NewLine}{Environment.NewLine}A verified backup was created and kept in Backups.";

        return $"{summary}{Environment.NewLine}{Environment.NewLine}No live change was confirmed.";
    }

    private string BuildPlanBlockedMessage(DryRunPlan plan)
    {
        var blockers = plan.Validation.Errors
            .Concat(plan.Validation.Warnings)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (blockers.Length == 0)
            return "Backup and Apply is blocked until the current review issues are fixed.";

        return "Backup and Apply is blocked right now." +
               Environment.NewLine + Environment.NewLine +
               string.Join(Environment.NewLine, blockers);
    }

    private static string BuildHomeSummary(PenumbraInstallation? installation, ScanInventory? inventory)
    {
        if (installation is null)
        {
            return "Penumbra could not be found yet." + Environment.NewLine +
                   "Use Scan My Mods after Penumbra is detected.";
        }

        var lines = new List<string>
        {
            "Penumbra found",
            Directory.Exists(installation.ModRoot) ? "Mod library found" : "Mod library not found",
        };

        if (inventory is null)
            lines.Add("Scan My Mods to load your library.");
        else
            lines.Add($"{inventory.Mods.Count:N0} mods detected");

        return string.Join(Environment.NewLine, lines);
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "Windows blocked access to sort_order.json." + Environment.NewLine + "No files were changed.",
            IOException ioException when ioException.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
                => "Penumbra's data is locked right now." + Environment.NewLine + "Close FFXIV and any tool that may be holding sort_order.json open, then try again.",
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("blocking process", StringComparison.OrdinalIgnoreCase)
                => "FFXIV is currently running." + Environment.NewLine + "Close the game before applying changes.",
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("authoritative target", StringComparison.OrdinalIgnoreCase)
                => "The Penumbra data changed after the scan." + Environment.NewLine + "Scan again before applying changes.",
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("no supported writable changes", StringComparison.OrdinalIgnoreCase)
                => "There are no supported virtual-folder changes to apply.",
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("The Penumbra data changed after the review", StringComparison.OrdinalIgnoreCase)
                => "The Penumbra data changed after the scan." + Environment.NewLine + "Scan again before applying changes.",
            _ => "The operation could not be completed safely." + Environment.NewLine + exception.Message,
        };
    }

    private async Task PromptForPenumbraObservationAsync(OperationPackageDetails? details)
    {
        if (_preparedApplyOperation is null || details?.PostApplyVerification?.Succeeded != true)
            return;

        var dialog = new PenumbraObservationDialog
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true || dialog.Observation is null)
            return;

        await _operationObservationService.SaveObservationAsync(_preparedApplyOperation.OperationId, dialog.Observation.Value, CancellationToken.None);
        await _backups.RefreshAsync();
        await RefreshRecoveryStatusAsync();
        AppendLog($"Recorded Penumbra UI observation for {_preparedApplyOperation.OperationId}: {dialog.Observation.Value}.");
    }

    private async Task RefreshRecoveryStatusAsync()
    {
        try
        {
            _incompleteOperations = await _operationRecoveryService.GetIncompleteOperationsAsync(CancellationToken.None);
            RecoveryStatus = _incompleteOperations.Count == 0
                ? "No incomplete backup, Apply, verification, or rollback operations are currently visible."
                : $"{_incompleteOperations.Count} incomplete operation(s) detected. Latest: {_incompleteOperations[0].Summary}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh incomplete-operation status");
            StartupBootstrapLogger.RecordException("Incomplete-operation refresh failed.", ex);
            RecoveryStatus = "Incomplete-operation status could not be refreshed.";
        }

        ReverifyIncompleteOperationCommand.RaiseCanExecuteChanged();
        ContinueIncompleteVerificationCommand.RaiseCanExecuteChanged();
        RollbackIncompleteOperationCommand.RaiseCanExecuteChanged();
        ViewIncompleteOperationCommand.RaiseCanExecuteChanged();
    }

    private async Task ReverifyIncompleteOperationAsync()
    {
        var operation = _incompleteOperations.FirstOrDefault(candidate => candidate.RecommendedActions.Contains(RecoveryRecommendedAction.Reverify));
        if (operation is null)
            return;

        await _operationRecoveryService.ReverifyBackupAsync(operation.OperationId, CancellationToken.None);
        await _backups.RefreshAsync();
        await RefreshRecoveryStatusAsync();
        AppendLog($"Re-verified incomplete operation {operation.OperationId}.");
    }

    private async Task ContinueIncompleteVerificationAsync()
    {
        var operation = _incompleteOperations.FirstOrDefault(candidate => candidate.RecommendedActions.Contains(RecoveryRecommendedAction.ContinueVerification));
        if (operation is null)
            return;

        await _operationRecoveryService.ContinueVerificationAsync(operation.OperationId, CancellationToken.None);
        await _backups.RefreshAsync();
        await RefreshRecoveryStatusAsync();
        AppendLog($"Continued verification for operation {operation.OperationId}.");
    }

    private async Task RollbackIncompleteOperationAsync()
    {
        var operation = _incompleteOperations.FirstOrDefault(candidate => candidate.RecommendedActions.Contains(RecoveryRecommendedAction.RollBack));
        if (operation is null)
            return;

        await _backups.RollbackOperationAsync(operation.OperationId);
        await RefreshRecoveryStatusAsync();
    }

    private async Task ViewIncompleteOperationAsync()
    {
        if (_incompleteOperations.Count == 0)
            return;

        await _backups.FocusOperationAsync(_incompleteOperations[0].OperationId);
        AppendLog($"Focused incomplete operation {_incompleteOperations[0].OperationId} in Backups.");
    }

    private void DebouncedSaveSession()
    {
        if (_inventory is null)
            return;

        _autosaveCts?.Cancel();
        _autosaveCts = new CancellationTokenSource();
        _ = DebouncedSaveSessionAsync(_autosaveCts.Token);
    }

    private async Task DebouncedSaveSessionAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), token);
            await _organizerSessionService.SaveLastSessionAsync(BuildSessionDocument(), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Organizer session autosave failed");
        }
    }

    private void RefreshReviewChanges()
    {
        if (_inventory is null)
            return;

        _reviewValidation = _controlledTestRequest is null
            ? _organizerValidationService.Validate(_inventory, CurrentProposalRows().ToArray(), _organizerFolders.ToArray(), BuildOrganizationPreferences())
            : BuildActiveProposalSnapshot().ValidationResult;
        ReviewRows.Clear();
        foreach (var row in FilterReviewRows(_reviewValidation.Rows))
            ReviewRows.Add(row);

        if (_currentDryRunPlan is not null)
        {
            var blockers = _reviewValidation.Errors.Select(error => error.Message).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _currentDryRunPlan = _currentDryRunPlan with
            {
                Validation = _currentDryRunPlan.Validation with
                {
                    Status = blockers.Length == 0 ? _currentDryRunPlan.Validation.Status : DryRunPlanValidationStatus.Invalid,
                    Errors = _currentDryRunPlan.Validation.Errors.Concat(blockers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    ApplyPermitted = blockers.Length == 0 && _currentDryRunPlan.Validation.ApplyPermitted,
                },
                ApplyPermitted = blockers.Length == 0 && _currentDryRunPlan.ApplyPermitted,
            };
        }

        ApplyChecklist = BuildApplyChecklist();
        ApplyUnavailableReason = BuildApplyUnavailableReason();
        RefreshDryRunCommandState();
        RaisePropertyChanged(nameof(ReviewTotal));
        RaisePropertyChanged(nameof(ReviewChanged));
        RaisePropertyChanged(nameof(ReviewUnchanged));
        RaisePropertyChanged(nameof(ReviewProtected));
        RaisePropertyChanged(nameof(ReviewNeedsReview));
        RaisePropertyChanged(nameof(ReviewInvalid));
        RaisePropertyChanged(nameof(ReviewWarnings));
    }

    private IEnumerable<OrganizerValidationRow> FilterReviewRows(IReadOnlyList<OrganizerValidationRow> rows)
        => ReviewFilter switch
        {
            "Changes only" => rows.Where(row => row.Status == OrganizerRowStatus.ValidChange),
            "Invalid only" => rows.Where(row => row.Status is OrganizerRowStatus.InvalidPath or OrganizerRowStatus.BlockedProtected or OrganizerRowStatus.MissingMod or OrganizerRowStatus.StaleScan),
            "Needs Review" => rows.Where(row => row.Status == OrganizerRowStatus.NeedsReview),
            "Protected" => rows.Where(row => row.Status == OrganizerRowStatus.Protected),
            "Manual" => rows.Where(row => row.Source == OrganizerProposalSource.Manual),
            "Deterministic" => rows.Where(row => row.Source == OrganizerProposalSource.DeterministicRule),
            "Imported AI" => rows.Where(row => row.Source == OrganizerProposalSource.ImportedAi),
            "Unchanged" => rows.Where(row => row.Status == OrganizerRowStatus.Unchanged),
            _ => rows,
        };

    private OrganizationPreferences BuildOrganizationPreferences()
        => SelectedStrategy switch
        {
            "By creator" => new OrganizationPreferences(OrganizationStrategy.CreatorOnly, false, true, [OrganizationFolderComponent.Creator], null, true, true, true, UnknownCreatorBehavior.PreserveCurrent, UnknownTypeBehavior.NotApplicable, UncertainClassificationBehavior.Review, true, null),
            "By mod type" => new OrganizationPreferences(OrganizationStrategy.TypeOnly, true, false, [OrganizationFolderComponent.Type], null, true, true, true, UnknownCreatorBehavior.NotApplicable, UnknownTypeBehavior.PreserveCurrent, UncertainClassificationBehavior.Review, true, null),
            "By type and creator" => new OrganizationPreferences(OrganizationStrategy.TypeThenCreator, true, true, [OrganizationFolderComponent.Type, OrganizationFolderComponent.Creator], null, true, true, true, UnknownCreatorBehavior.Review, UnknownTypeBehavior.Review, UncertainClassificationBehavior.Review, true, null),
            "By creator and type" => new OrganizationPreferences(OrganizationStrategy.CreatorThenType, true, true, [OrganizationFolderComponent.Creator, OrganizationFolderComponent.Type], null, true, true, true, UnknownCreatorBehavior.Review, UnknownTypeBehavior.Review, UncertainClassificationBehavior.Review, true, null),
            "Custom" => new OrganizationPreferences(OrganizationStrategy.Custom, true, true, [OrganizationFolderComponent.Type, OrganizationFolderComponent.Creator], null, true, true, true, UnknownCreatorBehavior.Review, UnknownTypeBehavior.Review, UncertainClassificationBehavior.Review, true, "{Type}/{Creator}"),
            _ => OrganizationPreferences.DefaultManual,
        };

    private static string NormalizeVirtualFolder(string path)
        => path.Trim().Replace('\\', '/').Trim('/');

    private static bool IsValidVirtualFolderPath(string path, out string error)
    {
        var normalized = NormalizeVirtualFolder(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Folder name is required.";
            return false;
        }

        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal) || normalized.Any(char.IsControl))
        {
            error = "Use a relative Penumbra folder name.";
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.None);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            error = "Folder names cannot be empty or use . or ...";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void AppendLog(string message)
    {
        ActivityLog = $"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}{Environment.NewLine}{ActivityLog}";
    }

    private sealed record ProposalChange(ModRowViewModel Row, string OldFolder, string NewFolder, string OldSource, string NewSource);

    private sealed record OrganizerAction(string Description, IReadOnlyList<ProposalChange> Changes);
}
