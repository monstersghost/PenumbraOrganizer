namespace PenumbraOrganizer.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.App.Commands;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class BackupsViewModel : ObservableObject
{
    private readonly IOperationHistoryService _historyService;
    private readonly IBackupVerificationService _backupVerificationService;
    private readonly ILogger<BackupsViewModel> _logger;
    private BackupOperationRowViewModel? _selectedOperation;
    private string _statusMessage = "Backup history will appear here.";
    private string _selectionSummary = "Select an operation to view its summary and affected files.";
    private string _selectedOperationFolder = string.Empty;
    private string _selectedBackupAvailability = "Rollback remains hidden in the public alpha UI.";

    public BackupsViewModel(
        IOperationHistoryService historyService,
        IBackupVerificationService backupVerificationService,
        ILogger<BackupsViewModel> logger)
    {
        _historyService = historyService;
        _backupVerificationService = backupVerificationService;
        _logger = logger;

        Operations = new ObservableCollection<BackupOperationRowViewModel>();
        AffectedFiles = new ObservableCollection<BackupAffectedFileViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        VerifyBackupCommand = new AsyncRelayCommand(VerifySelectedBackupAsync, () => SelectedOperation is not null);
        OpenBackupFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, () => SelectedOperation is not null);
    }

    public ObservableCollection<BackupOperationRowViewModel> Operations { get; }
    public ObservableCollection<BackupAffectedFileViewModel> AffectedFiles { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand VerifyBackupCommand { get; }
    public AsyncRelayCommand OpenBackupFolderCommand { get; }

    public BackupOperationRowViewModel? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (!SetProperty(ref _selectedOperation, value))
                return;

            VerifyBackupCommand.RaiseCanExecuteChanged();
            OpenBackupFolderCommand.RaiseCanExecuteChanged();
            _ = LoadSelectedOperationAsync(value);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public string SelectedOperationFolder
    {
        get => _selectedOperationFolder;
        private set => SetProperty(ref _selectedOperationFolder, value);
    }

    public string SelectedBackupAvailability
    {
        get => _selectedBackupAvailability;
        private set => SetProperty(ref _selectedBackupAvailability, value);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var selectedId = SelectedOperation?.OperationId;
            var operations = await _historyService.GetOperationsAsync(CancellationToken.None);
            Operations.Clear();
            foreach (var operation in operations)
                Operations.Add(new BackupOperationRowViewModel(operation));

            SelectedOperation = selectedId is null
                ? Operations.FirstOrDefault()
                : Operations.FirstOrDefault(operation => operation.OperationId == selectedId) ?? Operations.FirstOrDefault();

            StatusMessage = operations.Count == 0
                ? "No backup operations have been created yet."
                : $"Loaded {operations.Count} backup operation{(operations.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh backup history");
            StatusMessage = "Backup history could not be loaded.";
        }
    }

    private async Task VerifySelectedBackupAsync()
    {
        if (SelectedOperation is null)
            return;

        try
        {
            StatusMessage = "Verifying backup package.";
            await _backupVerificationService.VerifyAsync(SelectedOperation.OperationId, CancellationToken.None);
            await RefreshAsync();
            StatusMessage = "Backup verification completed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup {OperationId}", SelectedOperation.OperationId);
            StatusMessage = "Backup verification failed.";
        }
    }

    private Task OpenSelectedFolderAsync()
    {
        if (SelectedOperation is null)
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedOperation.OperationFolder,
            UseShellExecute = true,
        });
        StatusMessage = "Opened the backup folder.";
        return Task.CompletedTask;
    }

    private async Task LoadSelectedOperationAsync(BackupOperationRowViewModel? selectedOperation)
    {
        AffectedFiles.Clear();

        if (selectedOperation is null)
        {
            SelectionSummary = "Select an operation to view its summary and affected files.";
            SelectedOperationFolder = string.Empty;
            SelectedBackupAvailability = "Rollback remains hidden in the public alpha UI.";
            return;
        }

        try
        {
            var details = await _historyService.TryLoadOperationAsync(selectedOperation.OperationId, CancellationToken.None);
            if (details is null)
            {
                SelectionSummary = "The selected backup package could not be loaded.";
                SelectedOperationFolder = string.Empty;
                SelectedBackupAvailability = "Rollback remains hidden in the public alpha UI.";
                return;
            }

            SelectedOperationFolder = details.Operation.OperationFolder;
            SelectedBackupAvailability = details.RollbackTransaction is null
                ? "Rollback is not yet available for this operation."
                : "A rollback transaction exists, but this public alpha screen remains read-only.";
            SelectionSummary =
                $"Operation ID: {details.Operation.OperationId}\n" +
                $"Created: {details.Operation.CreatedAtUtc:u}\n" +
                $"Backup status: {details.Operation.BackupStatus}\n" +
                $"Backup verified: {(details.Operation.VerificationStatus == OperationVerificationStatus.Verified ? "Yes" : "No")}\n" +
                $"Rollback status: {(details.Operation.HasRollbackTransaction ? details.Operation.RollbackStatus : "Not available")}\n" +
                $"Affected files: {details.Operation.AffectedFileCount}\n" +
                $"Conflicts: {details.Operation.ConflictCount}\n" +
                $"Failures: {details.Operation.FailureCount}\n" +
                $"Penumbra version: {details.Operation.PenumbraVersion ?? "Unknown"}";

            if (details.Manifest is not null)
            {
                foreach (var file in details.Manifest.Files.OrderBy(file => file.SourceTargetPath, StringComparer.OrdinalIgnoreCase))
                    AffectedFiles.Add(new BackupAffectedFileViewModel(file));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load backup operation {OperationId}", selectedOperation.OperationId);
            SelectionSummary = "The selected backup package could not be read.";
            SelectedOperationFolder = string.Empty;
            SelectedBackupAvailability = "Rollback remains hidden in the public alpha UI.";
        }
    }
}

public sealed class BackupOperationRowViewModel
{
    private readonly OperationHistoryEntry _entry;

    public BackupOperationRowViewModel(OperationHistoryEntry entry)
    {
        _entry = entry;
    }

    public Guid OperationId => _entry.OperationId;
    public DateTimeOffset CreatedAtUtc => _entry.CreatedAtUtc;
    public string BackupStatus => _entry.BackupStatus.ToString();
    public int AffectedItems => _entry.AffectedFileCount;
    public string BackupVerified => _entry.VerificationStatus == OperationVerificationStatus.Verified ? "Yes" : "No";
    public string RollbackStatus => _entry.HasRollbackTransaction ? _entry.RollbackStatus.ToString() : "Not available";
    public int Conflicts => _entry.ConflictCount;
    public string OperationFolder => _entry.OperationFolder;
}

public sealed class BackupAffectedFileViewModel
{
    public BackupAffectedFileViewModel(BackupFileEntry file)
    {
        TargetPath = file.SourceTargetPath;
        RelativeBackupPath = file.RelativeBackupPath;
        Classification = file.Classification.ToString();
        JsonStatus = file.JsonValidationStatus.ToString();
        OriginalHash = file.OriginalSha256;
    }

    public string TargetPath { get; }
    public string RelativeBackupPath { get; }
    public string Classification { get; }
    public string JsonStatus { get; }
    public string OriginalHash { get; }
}
