using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Apply;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class OperationHistoryService : IOperationHistoryService
{
    private readonly RecoveryStorageLayout _layout;

    public OperationHistoryService()
        : this(new RecoveryStorageLayout())
    {
    }

    public OperationHistoryService(string backupsRoot)
        : this(new RecoveryStorageLayout(backupsRoot))
    {
    }

    internal OperationHistoryService(RecoveryStorageLayout layout)
    {
        _layout = layout;
    }

    public async Task<IReadOnlyList<OperationHistoryEntry>> GetOperationsAsync(CancellationToken cancellationToken)
        => await RebuildIndexAsync(cancellationToken);

    public async Task<OperationPackageDetails?> TryLoadOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operationPath = _layout.GetOperationPath(operationId);
        if (!File.Exists(operationPath))
            return null;

        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(operationPath, cancellationToken);
        var manifest = await AtomicJsonFileStore.ReadAsync<BackupManifest>(_layout.GetManifestPath(operationId), cancellationToken);
        var plan = await AtomicJsonFileStore.ReadAsync<DryRunPlan>(_layout.GetPlanPath(operationId), cancellationToken);
        var apply = await AtomicJsonFileStore.ReadAsync<ApplyPackageDocument>(_layout.GetApplyPath(operationId), cancellationToken);
        var rollback = await AtomicJsonFileStore.ReadAsync<RollbackTransaction>(_layout.GetRollbackPath(operationId), cancellationToken);
        var verification = await AtomicJsonFileStore.ReadAsync<OperationVerificationDocument>(_layout.GetVerificationPath(operationId), cancellationToken);

        return new OperationPackageDetails(
            operation,
            manifest,
            plan,
            apply?.Operation,
            apply?.Result,
            rollback,
            verification?.BackupVerification,
            verification?.RollbackVerification,
            verification?.PostApplyVerification);
    }

    public async Task<OperationHistoryEntry> RefreshOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var details = await TryLoadOperationAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Operation {operationId} was not found.");
        var entry = ToHistoryEntry(details.Operation);

        var current = await AtomicJsonFileStore.ReadAsync<OperationHistoryIndexDocument>(_layout.HistoryIndexPath, cancellationToken)
            ?? new OperationHistoryIndexDocument { Operations = Array.Empty<OperationHistoryEntry>() };
        var merged = current.Operations
            .Where(existing => existing.OperationId != operationId)
            .Append(entry)
            .OrderByDescending(existing => existing.CreatedAtUtc)
            .ToArray();

        await PersistIndexAsync(merged, cancellationToken);
        return entry;
    }

    public async Task<IReadOnlyList<OperationHistoryEntry>> RebuildIndexAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_layout.BackupsRoot))
        {
            Directory.CreateDirectory(_layout.BackupsRoot);
            await PersistIndexAsync(Array.Empty<OperationHistoryEntry>(), cancellationToken);
            return Array.Empty<OperationHistoryEntry>();
        }

        var entries = new List<OperationHistoryEntry>();
        foreach (var directory in Directory.EnumerateDirectories(_layout.BackupsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationPath = Path.Combine(directory, "operation.json");
            if (!File.Exists(operationPath))
                continue;

            try
            {
                var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(operationPath, cancellationToken);
                entries.Add(ToHistoryEntry(operation));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
            }
        }

        var ordered = entries
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.OperationId)
            .ToArray();

        await PersistIndexAsync(ordered, cancellationToken);
        return ordered;
    }

    private async Task PersistIndexAsync(IReadOnlyList<OperationHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var document = new OperationHistoryIndexDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Operations = entries,
        };

        await AtomicJsonFileStore.WriteAsync(
            _layout.HistoryIndexPath,
            document,
            value => value.Operations.All(entry => entry.OperationId != Guid.Empty),
            cancellationToken);
    }

    private static OperationHistoryEntry ToHistoryEntry(BackupOperation operation)
        => new(
            operation.OperationId,
            operation.CreatedAtUtc,
            operation.OperationKind,
            operation.BackupStatus,
            operation.ApplyStatus,
            operation.RollbackStatus,
            operation.AffectedFileCount,
            operation.AffectedModCount,
            operation.PenumbraVersion,
            operation.VerificationStatus,
            operation.ConflictCount,
            operation.FailureCount,
            operation.OperationFolder,
            operation.HasRollbackTransaction,
            operation.RollbackAvailable,
            operation.ObservationStatus,
            operation.ObservationRecordedAtUtc);
}
