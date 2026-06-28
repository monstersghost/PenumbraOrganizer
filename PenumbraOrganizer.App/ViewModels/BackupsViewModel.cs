namespace PenumbraOrganizer.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.App;
using PenumbraOrganizer.App.Dialogs;
using PenumbraOrganizer.App.Commands;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class BackupsViewModel : ObservableObject
{
    private readonly IOperationHistoryService _historyService;
    private readonly IBackupVerificationService _backupVerificationService;
    private readonly IRollbackService _rollbackService;
    private readonly ILogger<BackupsViewModel> _logger;
    private BackupOperationRowViewModel? _selectedOperation;
    private string _statusMessage = "Backup history will appear here.";
    private string _selectionSummary = "Select a backup to review its result and affected files.";
    private string _selectedOperationFolder = string.Empty;
    private string _selectedBackupAvailability = "Restore details will appear after you select a backup.";

    public BackupsViewModel(
        IOperationHistoryService historyService,
        IBackupVerificationService backupVerificationService,
        IRollbackService rollbackService,
        ILogger<BackupsViewModel> logger)
    {
        _historyService = historyService;
        _backupVerificationService = backupVerificationService;
        _rollbackService = rollbackService;
        _logger = logger;

        Operations = new ObservableCollection<BackupOperationRowViewModel>();
        AffectedFiles = new ObservableCollection<BackupAffectedFileViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        VerifyBackupCommand = new AsyncRelayCommand(VerifySelectedBackupAsync, () => SelectedOperation is not null);
        OpenBackupFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, () => SelectedOperation is not null);
        RollbackCommand = new AsyncRelayCommand(RollbackSelectedAsync, CanRollbackSelected);
    }

    public ObservableCollection<BackupOperationRowViewModel> Operations { get; }
    public ObservableCollection<BackupAffectedFileViewModel> AffectedFiles { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand VerifyBackupCommand { get; }
    public AsyncRelayCommand OpenBackupFolderCommand { get; }
    public AsyncRelayCommand RollbackCommand { get; }

    public BackupOperationRowViewModel? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (!SetProperty(ref _selectedOperation, value))
                return;

            VerifyBackupCommand.RaiseCanExecuteChanged();
            OpenBackupFolderCommand.RaiseCanExecuteChanged();
            RollbackCommand.RaiseCanExecuteChanged();
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
                : $"{operations.Count} backup operation{(operations.Count == 1 ? string.Empty : "s")} available.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh backup history");
            StartupBootstrapLogger.RecordException("Backup history refresh failed.", ex);
            StatusMessage = "Backup history could not be loaded.";
        }
    }

    public async Task FocusOperationAsync(Guid operationId)
    {
        await RefreshAsync();
        SelectedOperation = Operations.FirstOrDefault(operation => operation.OperationId == operationId) ?? Operations.FirstOrDefault();
    }

    public async Task RollbackOperationAsync(Guid operationId)
    {
        await FocusOperationAsync(operationId);
        await RollbackSelectedAsync();
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
            StartupBootstrapLogger.RecordException("Backup verification failed.", ex);
            StatusMessage = "Backup verification failed.";
        }
    }

    private bool CanRollbackSelected()
        => SelectedOperation is not null &&
           SelectedOperation.RollbackAvailable &&
           string.Equals(SelectedOperation.BackupVerified, "Yes", StringComparison.Ordinal);

    private async Task RollbackSelectedAsync()
    {
        if (SelectedOperation is null)
            return;

        var details = await _historyService.TryLoadOperationAsync(SelectedOperation.OperationId, CancellationToken.None);
        if (details is null || !details.Operation.RollbackAvailable || details.RollbackTransaction is null)
        {
            StatusMessage = "Restore is not available for the selected backup.";
            return;
        }

        var summary =
            $"Restore {details.Operation.AffectedModCount ?? details.Operation.AffectedFileCount} affected mod change(s)?\n\n" +
            "This restores Penumbra virtual-folder state only. Physical mod files are not moved.\n\n" +
            $"Backup verified: {(details.Operation.VerificationStatus == OperationVerificationStatus.Verified ? "Yes" : "No")}\n" +
            $"Current rollback status: {details.Operation.RollbackStatus}";
        var confirm = new WorkflowIssueDialogModel
        {
            Title = "Restore Backup",
            Heading = "Restore this backup?",
            Summary = summary,
            Severity = WorkflowIssueSeverity.Warning,
            AnyWriteOccurred = false,
            AffectedRowsOrMods = [SelectedOperation.OperationKind, $"{details.Operation.AffectedModCount ?? details.Operation.AffectedFileCount} affected mod change(s)"],
            TechnicalDetails = $"Operation: {details.Operation.OperationId}{Environment.NewLine}Backup folder: {details.Operation.OperationFolder}",
            ContinueLabel = "Restore Backup",
            AllowContinue = true,
        };
        var confirmResult = new WorkflowIssueDialog(confirm)
        {
            Owner = Application.Current.MainWindow,
        };
        confirmResult.ShowDialog();
        if (confirm.Result != WorkflowIssueDialogResult.Continue)
        {
            return;
        }

        try
        {
            StatusMessage = "Checking current data and restoring eligible records.";
            var result = await _rollbackService.ExecuteAsync(SelectedOperation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
            await RefreshAsync();
            StatusMessage =
                $"Restore finished with status {result.Status}. " +
                $"Restored: {result.Files.Count(file => file.Status == RollbackFileStatus.Restored)}, " +
                $"Already restored: {result.Files.Count(file => file.Status == RollbackFileStatus.AlreadyRestored)}, " +
                $"Skipped: {result.Files.Count(file => file.Status == RollbackFileStatus.Skipped)}, " +
                $"Conflicts: {result.Files.Count(file => file.Status == RollbackFileStatus.Conflict)}, " +
                $"Failures: {result.FailureCount}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back operation {OperationId}", SelectedOperation.OperationId);
            StartupBootstrapLogger.RecordException("Backup restore failed.", ex);
            StatusMessage = "Restore failed before completion.";
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
            SelectionSummary = "Select a backup to review its result and affected files.";
            SelectedOperationFolder = string.Empty;
            SelectedBackupAvailability = "Restore details will appear after you select a backup.";
            return;
        }

        try
        {
            var details = await _historyService.TryLoadOperationAsync(selectedOperation.OperationId, CancellationToken.None);
            if (details is null)
            {
                SelectionSummary = "The selected backup package could not be loaded.";
                SelectedOperationFolder = string.Empty;
                SelectedBackupAvailability = "Restore details are unavailable for the selected backup.";
                return;
            }

            SelectedOperationFolder = details.Operation.OperationFolder;
            SelectedBackupAvailability = details.RollbackTransaction is null
                ? "Restore is not yet available for this backup."
                : details.Operation.RollbackAvailable
                    ? "Restore is available. The app will verify current data and skip conflicts instead of overwriting them."
                    : "A restore record exists, but restore stays disabled until Apply finishes successfully.";
            SelectionSummary =
                $"Operation kind: {selectedOperation.OperationKind}\n" +
                $"Created: {details.Operation.CreatedAtUtc:u}\n" +
                $"Backup status: {details.Operation.BackupStatus}\n" +
                $"Apply status: {details.Operation.ApplyStatus}\n" +
                $"Backup verified: {(details.Operation.VerificationStatus == OperationVerificationStatus.Verified ? "Yes" : "No")}\n" +
                $"Rollback status: {(details.Operation.HasRollbackTransaction ? details.Operation.RollbackStatus : "Not available")}\n" +
                $"Restore available: {(details.Operation.RollbackAvailable ? "Yes" : "No")}\n" +
                $"Changed mods: {details.Operation.AffectedModCount ?? details.Operation.AffectedFileCount}\n" +
                $"Conflicts: {details.Operation.ConflictCount}\n" +
                $"Failures: {details.Operation.FailureCount}\n" +
                $"Penumbra version: {details.Operation.PenumbraVersion ?? "Unknown"}\n" +
                $"Penumbra observation: {details.Operation.ObservationStatus?.ToString() ?? "Not recorded"}";

            if (details.Manifest is not null)
            {
                foreach (var file in details.Manifest.Files.OrderBy(file => file.SourceTargetPath, StringComparer.OrdinalIgnoreCase))
                    AffectedFiles.Add(new BackupAffectedFileViewModel(file));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load backup operation {OperationId}", selectedOperation.OperationId);
            StartupBootstrapLogger.RecordException("Backup details load failed.", ex);
            SelectionSummary = "The selected backup package could not be read.";
            SelectedOperationFolder = string.Empty;
            SelectedBackupAvailability = "Restore details are unavailable for the selected backup.";
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
    public string OperationKind => _entry.OperationKind switch
    {
        BackupOperationKind.ManualBackup => "Manual backup",
        BackupOperationKind.PreApplyBackup => "Pre-apply backup",
        _ => "Applied changes",
    };
    public string BackupStatus => _entry.BackupStatus.ToString();
    public string ApplyStatus => _entry.ApplyStatus.ToString();
    public int AffectedItems => _entry.AffectedModCount ?? _entry.AffectedFileCount;
    public string BackupVerified => _entry.VerificationStatus == OperationVerificationStatus.Verified ? "Yes" : "No";
    public string RollbackStatus => _entry.HasRollbackTransaction ? _entry.RollbackStatus.ToString() : "Not available";
    public int Conflicts => _entry.ConflictCount;
    public string OperationFolder => _entry.OperationFolder;
    public bool RollbackAvailable => _entry.RollbackAvailable;
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
