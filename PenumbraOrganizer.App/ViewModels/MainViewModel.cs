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
using PenumbraOrganizer.App.Commands;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class MainViewModel : ObservableObject
{
    private readonly IPenumbraDiscoveryService _discoveryService;
    private readonly IPenumbraScanService _scanService;
    private readonly IPenumbraCompatibilityService _compatibilityService;
    private readonly IInventoryExportService _inventoryExportService;
    private readonly IOrganizerMutationService _organizerMutationService;
    private readonly IOrganizerProposalValidationService _organizerValidationService;
    private readonly IOrganizerSessionService _organizerSessionService;
    private readonly IDryRunPlanner _dryRunPlanner;
    private readonly IApplyService _applyService;
    private readonly IRealInstallationValidationService _realInstallationValidationService;
    private readonly IAiProposalImportService _aiProposalImportService;
    private readonly IDiagnosticExportService _diagnosticExportService;
    private readonly IOperationHistoryService _historyService;
    private readonly BackupsViewModel _backups;
    private readonly ILogger<MainViewModel> _logger;
    private PenumbraInstallation? _installation;
    private ScanInventory? _inventory;
    private InventoryExportResult? _lastExport;
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
    private string _newFolderName = string.Empty;
    private string _renameFolderName = string.Empty;
    private string _reviewFilter = "All";
    private string _applyUnavailableReason = "Create a dry run before applying changes.";
    private string _dryRunStatus = "Create a dry run to preview the exact Penumbra write target and expected result.";
    private string _backupStatus = "Create a verified backup after the dry run is current.";
    private string _applyChecklist = "Readiness checks will appear after a dry run is created.";
    private OrganizerFolderViewModel? _selectedProposedFolder;
    private OrganizerValidationResult? _reviewValidation;
    private DryRunPlan? _currentDryRunPlan;
    private ApplyOperation? _preparedApplyOperation;
    private ApplyResult? _latestApplyResult;
    private RealInstallationValidationResult? _lastRealInstallationValidation;
    private int _changedProposalCount;
    private int _needsReviewCount;
    private int _installedModCount;
    private int _protectedModCount;
    private int _collectionCount;
    private int _warningCount;
    private string _installationValidationStatus = "Real-installation validation has not been run yet.";
    private string _aiImportStatus = "AI proposal import is available after creating an AI review package.";
    private string _diagnosticStatus = "Diagnostic export is available without touching your mod assets or live databases.";

    public MainViewModel(
        IPenumbraDiscoveryService discoveryService,
        IPenumbraScanService scanService,
        IPenumbraCompatibilityService compatibilityService,
        IInventoryExportService inventoryExportService,
        IAiProposalImportService aiProposalImportService,
        IOrganizerMutationService organizerMutationService,
        IOrganizerProposalValidationService organizerValidationService,
        IOrganizerSessionService organizerSessionService,
        IDryRunPlanner dryRunPlanner,
        IApplyService applyService,
        IRealInstallationValidationService realInstallationValidationService,
        IDiagnosticExportService diagnosticExportService,
        IOperationHistoryService historyService,
        BackupsViewModel backups,
        ILogger<MainViewModel> logger)
    {
        _discoveryService = discoveryService;
        _scanService = scanService;
        _compatibilityService = compatibilityService;
        _inventoryExportService = inventoryExportService;
        _aiProposalImportService = aiProposalImportService;
        _organizerMutationService = organizerMutationService;
        _organizerValidationService = organizerValidationService;
        _organizerSessionService = organizerSessionService;
        _dryRunPlanner = dryRunPlanner;
        _applyService = applyService;
        _realInstallationValidationService = realInstallationValidationService;
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
        FilteredMods = new CollectionViewSource { Source = Mods }.View;
        FilteredMods.Filter = FilterMod;
        SelectedFolderMods = new CollectionViewSource { Source = Mods }.View;
        SelectedFolderMods.Filter = FilterSelectedFolderMod;
        ChangedMods = new CollectionViewSource { Source = Mods }.View;
        ChangedMods.Filter = item => item is ModRowViewModel mod && mod.IsChanged;

        DetectCommand = new AsyncRelayCommand(DetectAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => _installation is not null);
        CreateAiReviewPackageCommand = new AsyncRelayCommand(CreateAiReviewPackageAsync, () => _inventory is not null);
        ImportAiProposalCommand = new AsyncRelayCommand(ImportAiProposalAsync, () => _inventory is not null && _lastExport is not null);
        OpenExportFolderCommand = new AsyncRelayCommand(OpenExportFolderAsync, () => _lastExport is not null);
        CopyMasterPromptCommand = new AsyncRelayCommand(CopyMasterPromptAsync, () => _lastExport is not null);
        CopyInventoryFilePathCommand = new AsyncRelayCommand(CopyInventoryFilePathAsync, () => _lastExport is not null);
        CopyCompleteAiRequestCommand = new AsyncRelayCommand(CopyCompleteAiRequestAsync, () => _lastExport is not null);
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
        RenameFolderCommand = new RelayCommand(_ => RenameSelectedFolder(), _ => SelectedProposedFolder is not null && !string.IsNullOrWhiteSpace(RenameFolderName));
        DeleteEmptyFolderCommand = new RelayCommand(_ => DeleteSelectedEmptyFolder(), _ => SelectedProposedFolder is not null);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => _inventory is not null);
        ResumeLastSessionCommand = new AsyncRelayCommand(ResumeLastSessionAsync, () => _inventory is not null);
        DiscardSessionCommand = new AsyncRelayCommand(DiscardSessionAsync);
        RefreshReviewCommand = new RelayCommand(_ => RefreshReviewChanges());
        CreateDryRunCommand = new AsyncRelayCommand(CreateDryRunAsync, () => _inventory is not null);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => _currentDryRunPlan?.ApplyPermitted == true && _preparedApplyOperation is null);
        ApplyVirtualFolderChangesCommand = new AsyncRelayCommand(ApplyVirtualFolderChangesAsync, () => _currentDryRunPlan?.ApplyPermitted == true && _preparedApplyOperation is not null);
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
        SelectedOrganizerMods.CollectionChanged += (_, _) => RefreshSelectionCommandState();
        _ = _backups.RefreshAsync();
    }

    public ICommand DetectCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CreateAiReviewPackageCommand { get; }
    public AsyncRelayCommand ImportAiProposalCommand { get; }
    public AsyncRelayCommand OpenExportFolderCommand { get; }
    public AsyncRelayCommand CopyMasterPromptCommand { get; }
    public AsyncRelayCommand CopyInventoryFilePathCommand { get; }
    public AsyncRelayCommand CopyCompleteAiRequestCommand { get; }
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
    public RelayCommand RenameFolderCommand { get; }
    public RelayCommand DeleteEmptyFolderCommand { get; }
    public AsyncRelayCommand SaveSessionCommand { get; }
    public AsyncRelayCommand ResumeLastSessionCommand { get; }
    public AsyncRelayCommand DiscardSessionCommand { get; }
    public RelayCommand RefreshReviewCommand { get; }
    public AsyncRelayCommand CreateDryRunCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand ApplyVirtualFolderChangesCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public ObservableCollection<ModRowViewModel> Mods { get; }
    public ObservableCollection<CollectionInventory> Collections { get; }
    public ObservableCollection<VirtualFolderNode> FolderTree { get; }
    public ObservableCollection<OrganizerFolderViewModel> ProposedFolders { get; }
    public ObservableCollection<ModRowViewModel> SelectedOrganizerMods { get; }
    public ObservableCollection<OrganizerValidationRow> ReviewRows { get; }
    public ObservableCollection<string> ScanWarnings { get; }
    public ICollectionView FilteredMods { get; }
    public ICollectionView SelectedFolderMods { get; }
    public ICollectionView ChangedMods { get; }
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
                FilteredMods.Refresh();
                SelectedFolderMods.Refresh();
            }
        }
    }

    public string ActivityLog
    {
        get => _activityLog;
        private set => SetProperty(ref _activityLog, value);
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
                SelectedFolderMods.Refresh();
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

    public string InstallationValidationStatus
    {
        get => _installationValidationStatus;
        private set => SetProperty(ref _installationValidationStatus, value);
    }

    public string AiImportStatus
    {
        get => _aiImportStatus;
        private set => SetProperty(ref _aiImportStatus, value);
    }

    public string DiagnosticStatus
    {
        get => _diagnosticStatus;
        private set => SetProperty(ref _diagnosticStatus, value);
    }

    public int ReviewTotal => _reviewValidation?.Summary.TotalMods ?? 0;
    public int ReviewChanged => _reviewValidation?.Summary.Changed ?? 0;
    public int ReviewUnchanged => _reviewValidation?.Summary.Unchanged ?? 0;
    public int ReviewProtected => _reviewValidation?.Summary.Protected ?? 0;
    public int ReviewNeedsReview => _reviewValidation?.Summary.NeedsReview ?? 0;
    public int ReviewInvalid => _reviewValidation?.Summary.Invalid ?? 0;
    public int ReviewWarnings => _reviewValidation?.Summary.Warnings ?? 0;

    public string UndoDescription => _undoStack.Count == 0 ? "Undo" : "Undo: " + _undoStack.Peek().Description;
    public string RedoDescription => _redoStack.Count == 0 ? "Redo" : "Redo: " + _redoStack.Peek().Description;

    private async Task DetectAsync()
    {
        try
        {
            ProgressMessage = "Finding Penumbra";
            AppendLog("Looking for Penumbra in known XIVLauncher locations.");
            var result = await _discoveryService.DiscoverAsync(CancellationToken.None);
            _installation = result.Installations.FirstOrDefault();
            ScanCommand.RaiseCanExecuteChanged();

            if (_installation is null)
            {
                DetectionSummary = "Penumbra could not be detected automatically. The folder-picker wizard is still pending implementation.";
                if (result.Errors.Count > 0)
                    AppendLog(string.Join(Environment.NewLine, result.Errors));
                return;
            }

            DetectionSummary = $"Penumbra settings found at:{Environment.NewLine}{_installation.ConfigDirectory}{Environment.NewLine}{Environment.NewLine}Your mod library is located at:{Environment.NewLine}{_installation.ModRoot}{Environment.NewLine}{Environment.NewLine}Installed Penumbra version:{Environment.NewLine}{_installation.InstalledVersion ?? "Unknown"}";
            AppendLog($"Detected Penumbra at {_installation.ConfigurationPath}");
            foreach (var warning in _installation.Warnings)
                AppendLog("Warning: " + warning);
            ProgressMessage = "Penumbra detected.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Penumbra detection failed");
            DetectionSummary = "Penumbra detection failed. Try again or choose the installation manually once the wizard is added.";
            AppendLog("Detection failed: " + ex.Message);
            ProgressMessage = "Detection failed.";
        }
    }

    private async Task ScanAsync()
    {
        if (_installation is null)
            return;

        try
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

            ScanWarnings.Clear();
            foreach (var warning in _inventory.Warnings.Concat(_inventory.Mods.SelectMany(m => m.Warnings)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(w => w, StringComparer.OrdinalIgnoreCase))
                ScanWarnings.Add(warning);

            InstalledModCount = _inventory.Mods.Count;
            ProtectedModCount = _inventory.Mods.Count(m => m.Protected);
            CollectionCount = _inventory.Collections.Count;
            WarningCount = ScanWarnings.Count;
            InvalidateDryRunState("Scan refreshed the live Penumbra snapshot.");
            ResetOrganizerHistory();
            RebuildProposedFolders();
            RefreshOrganizerViews();
            CompatibilitySummary = BuildCompatibilitySummary(compatibility);
            ProgressMessage = $"Scan complete. {_inventory.Mods.Count} mods loaded.";
            AppendLog($"Scan finished with {_inventory.Mods.Count} mods and {ScanWarnings.Count} warnings.");
            CreateAiReviewPackageCommand.RaiseCanExecuteChanged();
            ImportAiProposalCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Penumbra scan failed");
            ProgressMessage = "Scan failed.";
            AppendLog("Scan failed: " + ex.Message);
        }
    }

    private async Task CreateAiReviewPackageAsync()
    {
        if (_inventory is null)
            return;

        try
        {
            ProgressMessage = "Creating AI review package";
            _lastExport = await _inventoryExportService.CreateAiReviewPackageAsync(_inventory, CancellationToken.None, BuildOrganizationPreferences());
            ImportAiProposalCommand.RaiseCanExecuteChanged();
            OpenExportFolderCommand.RaiseCanExecuteChanged();
            CopyMasterPromptCommand.RaiseCanExecuteChanged();
            CopyInventoryFilePathCommand.RaiseCanExecuteChanged();
            CopyCompleteAiRequestCommand.RaiseCanExecuteChanged();
            AppendLog($"Created AI review package at {_lastExport.ExportFolder}");
            AiImportStatus = "AI review package ready. Import a Penumbra_AI_Proposal.json file to merge validated suggestions into this session.";
            ProgressMessage = "AI review package created.";

            var dialog = new ExportPackageDialog(_lastExport, this)
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI review package creation failed");
            ProgressMessage = "AI review package creation failed.";
            AppendLog("AI review package creation failed: " + ex.Message);
            MessageBox.Show(
                "The AI review package could not be created. Please try again after a successful scan.",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ImportAiProposalAsync()
    {
        if (_inventory is null || _lastExport is null)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Penumbra_AI_Proposal.json",
            FileName = "Penumbra_AI_Proposal.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        try
        {
            ProgressMessage = "Importing AI proposal.";
            var import = await _aiProposalImportService.ImportAsync(
                dialog.FileName,
                _lastExport.InventoryPath,
                CurrentProposalRows().ToArray(),
                CancellationToken.None);

            if (import.Errors.Count > 0 && import.ImportedRows.Count == 0)
            {
                AiImportStatus = import.Summary;
                ProgressMessage = "AI proposal import blocked.";
                AppendLog("AI proposal import blocked: " + string.Join(" | ", import.Errors.Select(error => error.Message).Distinct(StringComparer.OrdinalIgnoreCase)));
                MessageBox.Show(import.Summary, "AI proposal blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var imported in import.ImportedRows)
            {
                var row = Mods.FirstOrDefault(candidate => candidate.StableScanId == imported.StableScanId);
                if (row is null)
                    continue;

                row.Proposal.ProposedVirtualFolder = imported.ProposedVirtualFolder;
                row.Proposal.OrganizerCreatorLabel = imported.OrganizerCreatorLabel;
                row.Proposal.OrganizerTypeLabel = imported.OrganizerTypeLabel;
                row.Proposal.Protected = imported.Protected;
                row.Proposal.Source = imported.Source;
                row.Proposal.NeedsReview = imported.NeedsReview;

                if (!_organizerFolders.Any(folder => folder.Path.Equals(imported.ProposedVirtualFolder, StringComparison.OrdinalIgnoreCase)))
                    _organizerFolders.Add(new OrganizerFolder(imported.ProposedVirtualFolder, true, false));
            }

            InvalidateDryRunState("Imported AI suggestions changed the proposal snapshot.");
            RefreshRowsFromProposals();
            RefreshOrganizerViews();
            RefreshReviewChanges();
            AiImportStatus = import.Summary;
            ProgressMessage = "AI proposal imported.";
            AppendLog(import.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI proposal import failed");
            AiImportStatus = "AI proposal import failed.";
            ProgressMessage = "AI proposal import failed.";
            AppendLog("AI proposal import failed: " + ex.Message);
            MessageBox.Show(ex.Message, "AI proposal import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            var snapshot = BuildProposalSnapshot();
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

    private async Task OpenExportFolderAsync()
    {
        if (_lastExport is null)
            return;

        await _inventoryExportService.ValidateExportPackageAsync(_lastExport.ExportFolder, CancellationToken.None);
        Process.Start(new ProcessStartInfo
        {
            FileName = _lastExport.ExportFolder,
            UseShellExecute = true,
        });
    }

    private async Task CopyMasterPromptAsync()
    {
        if (_lastExport is null)
            return;

        await _inventoryExportService.ValidateExportPackageAsync(_lastExport.ExportFolder, CancellationToken.None);
        Clipboard.SetText(await File.ReadAllTextAsync(_lastExport.InstructionsPath));
    }

    private async Task CopyInventoryFilePathAsync()
    {
        if (_lastExport is null)
            return;

        await _inventoryExportService.ValidateExportPackageAsync(_lastExport.ExportFolder, CancellationToken.None);
        Clipboard.SetText(_lastExport.InventoryPath);
    }

    private async Task CopyCompleteAiRequestAsync()
    {
        if (_lastExport is null)
            return;

        await _inventoryExportService.ValidateExportPackageAsync(_lastExport.ExportFolder, CancellationToken.None);
        var prompt = await File.ReadAllTextAsync(_lastExport.InstructionsPath);
        Clipboard.SetText($"Upload Penumbra_AI_Review_Package.zip to your AI assistant first, then paste the prompt below.{Environment.NewLine}{Environment.NewLine}{prompt}");
    }

    private string BuildCompatibilitySummary(CompatibilityReport compatibility)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Compatibility: {compatibility.Status}");
        builder.AppendLine($"Installed version: {compatibility.InstalledVersion}");
        builder.AppendLine($"Scanned version: {compatibility.ScannedVersion}");
        if (compatibility.Warnings.Count > 0)
            builder.AppendLine(string.Join(Environment.NewLine, compatibility.Warnings));
        return builder.ToString().TrimEnd();
    }

    private bool FilterMod(object item)
    {
        if (item is not ModRowViewModel mod)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var text = SearchText.Trim();
        return mod.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.Author.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.CurrentVirtualFolder.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.ProposedVirtualFolder.Contains(text, StringComparison.OrdinalIgnoreCase)
               || mod.PhysicalDirectory.Contains(text, StringComparison.OrdinalIgnoreCase);
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
        FilteredMods.Refresh();
        SelectedFolderMods.Refresh();
        ChangedMods.Refresh();
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
        if (e.PropertyName is nameof(ModRowViewModel.ProposedVirtualFolder) or nameof(ModRowViewModel.Protected))
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
        RenameFolderCommand.RaiseCanExecuteChanged();
        DeleteEmptyFolderCommand.RaiseCanExecuteChanged();
    }

    private IList<OrganizerModProposal> CurrentProposalRows()
        => Mods.Select(mod => mod.Proposal).ToList();

    private void RefreshRowsFromProposals()
    {
        foreach (var row in Mods)
            row.RefreshFromProposal();
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
        };
    }

    private void RestoreSession(OrganizerSessionDocument session)
    {
        var savedById = session.Mods.ToDictionary(row => row.StableScanId, StringComparer.Ordinal);
        foreach (var row in Mods)
        {
            if (!savedById.TryGetValue(row.StableScanId, out var saved))
                continue;
            row.Proposal.ProposedVirtualFolder = saved.ProposedVirtualFolder;
            row.Proposal.Protected = saved.Protected;
            row.Proposal.OrganizerCreatorLabel = saved.OrganizerCreatorLabel;
            row.Proposal.OrganizerTypeLabel = saved.OrganizerTypeLabel;
            row.Proposal.Source = saved.ProposalSource;
            row.Proposal.NeedsReview = saved.NeedsReview;
        }

        _organizerFolders.Clear();
        foreach (var folder in session.ProposedFolders)
            _organizerFolders.Add(new OrganizerFolder(folder.Path, folder.ManuallyCreated, folder.Protected));

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
            var snapshot = BuildProposalSnapshot();
            _currentDryRunPlan = await _dryRunPlanner.CreatePlanAsync(_installation, _inventory, snapshot, CancellationToken.None);
            _preparedApplyOperation = null;
            _latestApplyResult = null;
            DryRunStatus = BuildDryRunStatus(_currentDryRunPlan);
            BackupStatus = _currentDryRunPlan.ApplyPermitted
                ? "Dry run is current. Create Backup will build a verified backup package and rollback record."
                : "Dry run found blockers. Fix them before creating a backup package.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            ProgressMessage = "Dry run created.";
            AppendLog($"Created dry run {_currentDryRunPlan.PlanId} with {_currentDryRunPlan.Summary.WriteOperationCount} writable target(s).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dry run creation failed");
            _currentDryRunPlan = null;
            _preparedApplyOperation = null;
            _latestApplyResult = null;
            DryRunStatus = "Dry run creation failed.";
            BackupStatus = "Create Backup is unavailable until a valid dry run exists.";
            ApplyChecklist = "Dry run failed. Review the error and rescan if needed.";
            ApplyUnavailableReason = ex.Message;
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
            var snapshot = BuildProposalSnapshot();
            _preparedApplyOperation = await _applyService.PrepareAsync(_currentDryRunPlan, _installation, snapshot, CancellationToken.None);
            BackupStatus = $"Verified backup ready. Operation: {_preparedApplyOperation.OperationId}";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
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
        }
    }

    private async Task ApplyVirtualFolderChangesAsync()
    {
        if (_installation is null || _currentDryRunPlan is null || _preparedApplyOperation is null)
            return;

        if (MessageBox.Show(
                BuildApplyConfirmationMessage(),
                "Apply virtual-folder changes",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            ProgressMessage = "Applying virtual-folder changes.";
            var snapshot = BuildProposalSnapshot();
            _latestApplyResult = await _applyService.ApplyAsync(_currentDryRunPlan, _preparedApplyOperation, _installation, snapshot, CancellationToken.None);
            BackupStatus = _latestApplyResult.RollbackAvailable
                ? $"Apply finished with status {_latestApplyResult.Status}. Rollback is now available from Backups."
                : $"Apply finished with status {_latestApplyResult.Status}.";
            ApplyChecklist = BuildApplyChecklist();
            ApplyUnavailableReason = BuildApplyUnavailableReason();
            RefreshDryRunCommandState();
            await _backups.RefreshAsync();
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
        }
    }

    private ProposalSnapshot BuildProposalSnapshot()
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
        var validation = _organizerValidationService.Validate(_inventory, proposals, folders, preferences);
        var session = BuildSessionDocument();
        return new ProposalSnapshot(
            OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences),
            OrganizerSessionService.BuildSessionIdentity(session),
            preferences,
            proposals,
            folders,
            validation);
    }

    private void InvalidateDryRunState(string reason)
    {
        _currentDryRunPlan = null;
        _preparedApplyOperation = null;
        _latestApplyResult = null;
        DryRunStatus = "Your plan is out of date. Create a new dry run before applying changes.";
        BackupStatus = reason;
        ApplyChecklist = "Create a new dry run before creating a backup or applying changes.";
        ApplyUnavailableReason = BuildApplyUnavailableReason();
        RefreshDryRunCommandState();
    }

    private void RefreshDryRunCommandState()
    {
        CreateDryRunCommand.RaiseCanExecuteChanged();
        CreateBackupCommand.RaiseCanExecuteChanged();
        ApplyVirtualFolderChangesCommand.RaiseCanExecuteChanged();
    }

    private string BuildDryRunStatus(DryRunPlan plan)
    {
        var fileChange = plan.FileChanges.SingleOrDefault();
        if (fileChange is null)
            return "No supported Penumbra virtual-folder writes are needed.";

        return
            $"Authoritative target: {Path.GetFileName(fileChange.TargetPath)}{Environment.NewLine}" +
            $"Record scope: {fileChange.ExactRecordKey}{Environment.NewLine}" +
            $"Affected mods: {plan.Summary.AffectedModCount}{Environment.NewLine}" +
            $"Plan status: {plan.Validation.Status}";
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
            ChecklistLine("Dry run current", _currentDryRunPlan?.Validation.Status == DryRunPlanValidationStatus.Valid),
        };

        if (_latestApplyResult is not null)
            checklist.Add($"Latest apply result: {_latestApplyResult.Status}");

        return string.Join(Environment.NewLine, checklist);
    }

    private string BuildApplyUnavailableReason()
    {
        if (_reviewValidation is null)
            return "Scan your mods and review proposals before creating a dry run.";

        if (_currentDryRunPlan is null)
            return "Your plan is out of date. Create a new dry run before applying changes.";

        if (_currentDryRunPlan.Validation.Status != DryRunPlanValidationStatus.Valid)
            return "Your plan is out of date. Create a new dry run before applying changes.";

        if (_preparedApplyOperation is null)
            return "Create a verified backup package before applying changes.";

        if (_preparedApplyOperation.Preflight.BlockingProcesses.Count > 0)
            return $"Apply is blocked while these processes are running: {string.Join(", ", _preparedApplyOperation.Preflight.BlockingProcesses)}";

        if (_latestApplyResult is not null)
            return _latestApplyResult.RollbackAvailable
                ? "Apply finished. Rollback is available from the Backups screen."
                : $"Apply finished with status {_latestApplyResult.Status}.";

        return _preparedApplyOperation.Preflight.Succeeded
            ? "Apply is enabled only for supported virtual-folder changes in mod_data.db."
            : string.Join(Environment.NewLine, _preparedApplyOperation.Preflight.Errors);
    }

    private static string ChecklistLine(string label, bool passed)
        => $"{(passed ? "[x]" : "[ ]")} {label}";

    private string BuildApplyConfirmationMessage()
    {
        var target = _currentDryRunPlan?.FileChanges.SingleOrDefault();
        var operationFolder = _preparedApplyOperation is null
            ? "Backup not prepared"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PenumbraOrganizer",
                "Backups",
                _preparedApplyOperation.OperationId.ToString("N"));
        return
            $"This will apply {_currentDryRunPlan?.Summary.AffectedModCount ?? 0} planned mod change(s).\n\n" +
            $"Authoritative target: {target?.TargetPath ?? "Unknown"}\n" +
            $"Backup location: {operationFolder}\n" +
            "Rollback will be available after a confirmed write.\n\n" +
            "Physical mod files, assets, collections, priorities, and FFXIV game files are not changed.\n\n" +
            "Close FFXIV and XIVLauncher before continuing.\n\n" +
            "Apply the planned virtual-folder changes now?";
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

        _reviewValidation = _organizerValidationService.Validate(_inventory, CurrentProposalRows().ToArray(), _organizerFolders.ToArray(), BuildOrganizationPreferences());
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
