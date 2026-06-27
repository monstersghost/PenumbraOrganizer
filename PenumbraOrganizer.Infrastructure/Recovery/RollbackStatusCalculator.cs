using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

internal static class RollbackStatusCalculator
{
    public static RollbackTransactionStatus Calculate(IReadOnlyList<RollbackFileEntry> files, RollbackTransactionStatus? preferredStatus = null)
    {
        if (preferredStatus == RollbackTransactionStatus.Cancelled)
            return RollbackTransactionStatus.Cancelled;

        var failureCount = files.Count(file => file.Status is RollbackFileStatus.Failed or RollbackFileStatus.MissingBackup or RollbackFileStatus.CorruptBackup);
        var conflictCount = files.Count(file => file.Status == RollbackFileStatus.Conflict);
        var progressCount = files.Count(file => file.Status is RollbackFileStatus.Restored or RollbackFileStatus.AlreadyRestored or RollbackFileStatus.Skipped);

        if (failureCount > 0)
            return progressCount > 0 || conflictCount > 0 ? RollbackTransactionStatus.PartiallyCompleted : RollbackTransactionStatus.Failed;
        if (conflictCount > 0)
            return RollbackTransactionStatus.CompletedWithConflicts;

        return RollbackTransactionStatus.Completed;
    }
}
