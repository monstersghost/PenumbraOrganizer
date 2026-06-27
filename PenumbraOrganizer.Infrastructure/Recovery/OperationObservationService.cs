using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

public sealed class OperationObservationService : IOperationObservationService
{
    private readonly RecoveryStorageLayout _layout;
    private readonly IOperationHistoryService _historyService;

    public OperationObservationService(IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(), historyService)
    {
    }

    public OperationObservationService(string backupsRoot, IOperationHistoryService historyService)
        : this(new RecoveryStorageLayout(backupsRoot), historyService)
    {
    }

    internal OperationObservationService(RecoveryStorageLayout layout, IOperationHistoryService historyService)
    {
        _layout = layout;
        _historyService = historyService;
    }

    public async Task<BackupOperation> SaveObservationAsync(
        Guid operationId,
        PenumbraUiObservationStatus status,
        CancellationToken cancellationToken)
    {
        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(_layout.GetOperationPath(operationId), cancellationToken);
        var updated = operation with
        {
            ObservationStatus = status,
            ObservationRecordedAtUtc = DateTimeOffset.UtcNow,
        };

        await AtomicJsonFileStore.WriteAsync(_layout.GetOperationPath(operationId), updated, value => value.OperationId == operationId, cancellationToken);
        await _historyService.RefreshOperationAsync(operationId, cancellationToken);
        return updated;
    }
}
