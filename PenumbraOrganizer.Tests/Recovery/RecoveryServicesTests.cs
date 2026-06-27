namespace PenumbraOrganizer.Tests.Recovery;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Recovery;

public sealed class RecoveryServicesTests
{
    [Fact]
    public async Task ValidTextBackup_Succeeds()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Verified);
        details.Operation.VerificationStatus.Should().Be(OperationVerificationStatus.Verified);
        details.Manifest!.Files.Should().ContainSingle();
        details.Manifest.Files[0].Classification.Should().Be(BackupFileClassification.Text);
    }

    [Fact]
    public async Task ValidJsonBackup_Succeeds()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\organization.json", """{"folder":"Clothing/Bizu Mods"}""");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Verified);
        details.Manifest!.Files[0].Classification.Should().Be(BackupFileClassification.Json);
        details.Manifest.Files[0].JsonValidationStatus.Should().Be(JsonValidationStatus.Valid);
    }

    [Fact]
    public async Task MultipleFilesBackup_Succeeds()
    {
        using var context = new RecoveryTestContext();
        var first = context.WriteTextFile(@"state\organization.json", """{"folder":"One"}""");
        var second = context.WriteTextFile(@"state\settings.txt", "two");

        var details = await context.CreateBackupAsync(new SourceFile(first), new SourceFile(second));

        details.Manifest!.Files.Should().HaveCount(2);
        details.Operation.AffectedFileCount.Should().Be(2);
    }

    [Fact]
    public async Task MissingSource_FailsBackup()
    {
        using var context = new RecoveryTestContext();
        var missing = Path.Combine(context.LiveRoot, "missing.json");

        var details = await context.CreateBackupAsync(new SourceFile(missing));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("does not exist");
        details.Manifest.Should().BeNull();
    }

    [Fact]
    public async Task ProtectedSource_IsRejected()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\protected.json", """{"locked":true}""");

        var details = await context.CreateBackupAsync(new SourceFile(file, Protected: true));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("Protected files cannot be included");
    }

    [Fact]
    public async Task CopiedLengthMismatch_FailsBackup()
    {
        var hooks = new RecoveryServiceHooks
        {
            AfterBackupFileCopiedAsync = async (_, tempPath, _) => await File.WriteAllTextAsync(tempPath, "x"),
        };
        using var context = new RecoveryTestContext(hooks);
        var file = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("Copied length mismatch");
    }

    [Fact]
    public async Task BackupHashMismatch_FailsBackup()
    {
        var hooks = new RecoveryServiceHooks
        {
            AfterBackupFileCopiedAsync = async (_, tempPath, _) =>
            {
                var bytes = await File.ReadAllBytesAsync(tempPath);
                bytes[0] = (byte)(bytes[0] + 1);
                await File.WriteAllBytesAsync(tempPath, bytes);
            },
        };
        using var context = new RecoveryTestContext(hooks);
        var file = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("Backup hash mismatch");
    }

    [Fact]
    public async Task MalformedExpectedJson_FailsVerification()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\organization.json", "{ invalid json");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("Expected JSON backup is invalid");
    }

    [Fact]
    public async Task AtomicManifestCreation_Succeeds()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(file));
        var manifestPath = context.GetManifestPath(details.Operation.OperationId);

        File.Exists(manifestPath).Should().BeTrue();
        File.Exists(manifestPath + ".tmp").Should().BeFalse();
        JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!.OperationId.Should().Be(details.Operation.OperationId);
    }

    [Fact]
    public async Task InterruptedTemporaryManifest_IsNotTreatedAsFinalized()
    {
        RecoveryTestContext? capturedContext = null;
        var hooks = new RecoveryServiceHooks
        {
            BeforePersistManifestAsync = async (operationId, _) =>
            {
                var manifestTempPath = Path.Combine(capturedContext!.GetOperationDirectory(operationId), "manifest.json.tmp");
                await File.WriteAllTextAsync(manifestTempPath, "{not-json");
                throw new InvalidOperationException("Synthetic interruption before manifest finalization.");
            },
        };

        using var context = new RecoveryTestContext(hooks);
        capturedContext = context;
        var file = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(file));

        File.Exists(context.GetManifestPath(details.Operation.OperationId)).Should().BeFalse();
        File.Exists(context.GetManifestPath(details.Operation.OperationId) + ".tmp").Should().BeTrue();
        details.Manifest.Should().BeNull();
        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
    }

    [Fact]
    public async Task OperationHistory_CanBeReconstructed()
    {
        using var context = new RecoveryTestContext();
        var file = context.WriteTextFile(@"state\organization.txt", "before");
        var details = await context.CreateBackupAsync(new SourceFile(file));

        File.Delete(context.HistoryIndexPath);
        var rebuilt = await context.HistoryService.RebuildIndexAsync(CancellationToken.None);

        rebuilt.Should().ContainSingle(entry => entry.OperationId == details.Operation.OperationId);
    }

    [Fact]
    public async Task ExactRollback_Succeeds()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        var appliedBytes = Encoding.UTF8.GetBytes("applied");
        await File.WriteAllBytesAsync(path, appliedBytes);
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash(appliedBytes), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Status.Should().Be(RollbackTransactionStatus.Completed);
        (await File.ReadAllTextAsync(path)).Should().Be("original");
    }

    [Fact]
    public async Task RestoredBytes_ExactlyEqualOriginal()
    {
        using var context = new RecoveryTestContext();
        var originalBytes = new byte[] { 0x00, 0x01, 0xFF, 0x41, 0x42 };
        var path = context.WriteBinaryFile(@"state\organization.bin", originalBytes);
        var details = await context.CreateBackupAsync(new SourceFile(path));
        var appliedBytes = new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14 };
        await File.WriteAllBytesAsync(path, appliedBytes);
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash(appliedBytes), true, ApplyResultStatus.Applied));

        await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        (await File.ReadAllBytesAsync(path)).Should().Equal(originalBytes);
    }

    [Fact]
    public async Task AlreadyRestoredFile_IsDetected()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.AlreadyRestored);
    }

    [Fact]
    public async Task ModifiedLiveFile_CreatesConflict()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, "unexpected");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Status.Should().Be(RollbackTransactionStatus.CompletedWithConflicts);
        result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.Conflict);
    }

    [Fact]
    public async Task Conflict_DoesNotOverwrite()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, "unexpected");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("unexpected");
    }

    [Fact]
    public async Task MissingLiveFile_IsRestored()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        File.Delete(path);
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.Restored);
        (await File.ReadAllTextAsync(path)).Should().Be("original");
    }

    [Fact]
    public async Task MissingBackup_BlocksRollback()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        File.Delete(context.ResolveBackupFilePath(details.Operation.OperationId, details.Manifest!.Files[0]));
        await File.WriteAllTextAsync(path, "applied");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.MissingBackup);
        (await File.ReadAllTextAsync(path)).Should().Be("applied");
    }

    [Fact]
    public async Task CorruptBackup_BlocksRollback()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(context.ResolveBackupFilePath(details.Operation.OperationId, details.Manifest!.Files[0]), "corrupt");
        await File.WriteAllTextAsync(path, "applied");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.CorruptBackup);
    }

    [Fact]
    public async Task PartialApplyTransaction_RestoresOnlySuccessfulEntries()
    {
        using var context = new RecoveryTestContext();
        var first = context.WriteTextFile(@"state\one.txt", "one");
        var second = context.WriteTextFile(@"state\two.txt", "two");
        var details = await context.CreateBackupAsync(new SourceFile(first), new SourceFile(second));
        await File.WriteAllTextAsync(first, "applied-one");
        await File.WriteAllTextAsync(second, "leave-two");
        await context.SaveTransactionAsync(
            details,
            new TransactionEntry(first, Hash("applied-one"), true, ApplyResultStatus.Applied),
            new TransactionEntry(second, Hash("leave-two"), true, ApplyResultStatus.Skipped));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Files.Single(file => file.TargetPath == first).Status.Should().Be(RollbackFileStatus.Restored);
        result.Files.Single(file => file.TargetPath == second).Status.Should().Be(RollbackFileStatus.Skipped);
        (await File.ReadAllTextAsync(second)).Should().Be("leave-two");
    }

    [Fact]
    public async Task PartialRollbackResult_IsReportedCorrectly()
    {
        using var context = new RecoveryTestContext();
        var first = context.WriteTextFile(@"state\one.txt", "one");
        var second = context.WriteTextFile(@"state\two.txt", "two");
        var details = await context.CreateBackupAsync(new SourceFile(first), new SourceFile(second));
        await File.WriteAllTextAsync(first, "applied-one");
        await File.WriteAllTextAsync(second, "applied-two");
        File.Delete(context.ResolveBackupFilePath(details.Operation.OperationId, details.Manifest!.Files.Single(file => file.SourceTargetPath == second)));
        await context.SaveTransactionAsync(
            details,
            new TransactionEntry(first, Hash("applied-one"), true, ApplyResultStatus.Applied),
            new TransactionEntry(second, Hash("applied-two"), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Status.Should().Be(RollbackTransactionStatus.PartiallyCompleted);
        result.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task SecondRollbackRun_IsSafe()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, "applied");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        var second = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        second.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.AlreadyRestored);
        (await File.ReadAllTextAsync(path)).Should().Be("original");
    }

    [Fact]
    public async Task InterruptedRollback_CanBeResumedSafely()
    {
        RecoveryTestContext? capturedContext = null;
        var hooks = new RecoveryServiceHooks
        {
            AfterRollbackFileProcessedAsync = (_, index, _) =>
            {
                if (index == 0)
                    capturedContext!.CancelCurrentExecution();
                return Task.CompletedTask;
            },
        };

        using var context = new RecoveryTestContext(hooks);
        capturedContext = context;
        var first = context.WriteTextFile(@"state\one.txt", "one");
        var second = context.WriteTextFile(@"state\two.txt", "two");
        var details = await context.CreateBackupAsync(new SourceFile(first), new SourceFile(second));
        await File.WriteAllTextAsync(first, "applied-one");
        await File.WriteAllTextAsync(second, "applied-two");
        await context.SaveTransactionAsync(
            details,
            new TransactionEntry(first, Hash("applied-one"), true, ApplyResultStatus.Applied),
            new TransactionEntry(second, Hash("applied-two"), true, ApplyResultStatus.Applied));

        var firstRun = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, context.ExecutionCancellationToken);
        var secondRun = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        firstRun.Status.Should().Be(RollbackTransactionStatus.Cancelled);
        secondRun.Status.Should().Be(RollbackTransactionStatus.Completed);
        (await File.ReadAllTextAsync(first)).Should().Be("one");
        (await File.ReadAllTextAsync(second)).Should().Be("two");
    }

    [Fact]
    public async Task JsonRestoration_IsValidated()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.json", """{"folder":"Original"}""");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, """{"folder":"Applied"}""");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("""{"folder":"Applied"}"""), true, ApplyResultStatus.Applied));

        await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        var verification = await context.RollbackVerificationService.VerifyAsync(details.Operation.OperationId, CancellationToken.None);

        verification.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ProtectedFile_CannotEnterRollbackTransaction()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        var manifestFile = details.Manifest!.Files[0];
        var transaction = new RollbackTransaction(
            details.Operation.OperationId,
            DateTimeOffset.UtcNow,
            details.Operation.ApplicationVersion,
            details.Operation.PenumbraVersion,
            details.Operation.ScanIdentity,
            RollbackTransactionStatus.Available,
            [
                new RollbackFileEntry(
                    path,
                    manifestFile.RelativeBackupPath,
                    manifestFile.OriginalSha256,
                    manifestFile.OriginalLength,
                    Hash("applied"),
                    true,
                    manifestFile.Classification,
                    true,
                    ApplyResultStatus.Applied,
                    RollbackFileStatus.Pending,
                    manifestFile.AssociatedStableScanIds,
                    manifestFile.WritablePlanOperationId,
                    null,
                    null,
                    null)
            ],
            null,
            null,
            null);

        Func<Task> act = async () => await context.RollbackService.SaveTransactionAsync(transaction, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Protected files cannot enter a rollback transaction*");
    }

    [Fact]
    public async Task BackupRelativePaths_CannotEscapeOperationDirectory()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        var manifest = details.Manifest! with
        {
            Files =
            [
                details.Manifest.Files[0] with
                {
                    RelativeBackupPath = "../escape.txt",
                }
            ],
        };
        await File.WriteAllTextAsync(
            context.GetManifestPath(details.Operation.OperationId),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var verification = await context.BackupVerificationService.VerifyAsync(details.Operation.OperationId, CancellationToken.None);

        verification.Succeeded.Should().BeFalse();
        verification.Issues.Should().Contain(issue => issue.Contains("cannot escape", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TargetPathTraversal_IsRejected()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        var unsafeTarget = Path.Combine(context.LiveRoot, "state", "..", "escape.txt");
        var manifest = details.Manifest!.Files[0];
        var transaction = new RollbackTransaction(
            details.Operation.OperationId,
            DateTimeOffset.UtcNow,
            details.Operation.ApplicationVersion,
            details.Operation.PenumbraVersion,
            details.Operation.ScanIdentity,
            RollbackTransactionStatus.Available,
            [
                new RollbackFileEntry(
                    unsafeTarget,
                    manifest.RelativeBackupPath,
                    manifest.OriginalSha256,
                    manifest.OriginalLength,
                    Hash("applied"),
                    true,
                    manifest.Classification,
                    false,
                    ApplyResultStatus.Applied,
                    RollbackFileStatus.Pending,
                    manifest.AssociatedStableScanIds,
                    manifest.WritablePlanOperationId,
                    null,
                    null,
                    null)
            ],
            null,
            null,
            null);

        Func<Task> act = async () => await context.RollbackService.SaveTransactionAsync(transaction, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Target path traversal*");
    }

    [Fact]
    public async Task DuplicateBackupDestinationCollision_IsRejected()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "before");

        var details = await context.CreateBackupAsync(new SourceFile(path), new SourceFile(path));

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("Duplicate source target path");
    }

    [Fact]
    public async Task UnicodePaths_Work()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(
            "state\\\u0627\u0644\u0639\u0631\u0628\u064a\u0629\\\u0645\u0638\u0647\u0631.json",
            """{"folder":"\u0645\u0631\u062d\u0628\u0627"}""");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, """{"folder":"\u0645\u0637\u0628\u0642"}""");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("""{"folder":"\u0645\u0637\u0628\u0642"}"""), true, ApplyResultStatus.Applied));

        var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        result.Status.Should().Be(RollbackTransactionStatus.Completed);
        (await File.ReadAllTextAsync(path)).Should().Be("""{"folder":"\u0645\u0631\u062d\u0628\u0627"}""");
    }

    [Fact]
    public async Task ReadOnlyDestinationFailure_IsReported()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, "applied");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

            var result = await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

            result.Files.Should().ContainSingle(file => file.Status == RollbackFileStatus.Failed);
        }
        finally
        {
            if (File.Exists(path))
                File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Cancellation_IsHandledSafely()
    {
        RecoveryTestContext? capturedContext = null;
        var hooks = new RecoveryServiceHooks
        {
            AfterBackupFileCopiedAsync = (_, _, _) =>
            {
                capturedContext!.CancelCurrentExecution();
                return Task.CompletedTask;
            },
        };

        using var context = new RecoveryTestContext(hooks);
        capturedContext = context;
        var first = context.WriteTextFile(@"state\one.txt", "one");
        var second = context.WriteTextFile(@"state\two.txt", "two");

        var details = await context.CreateBackupAsync([new SourceFile(first), new SourceFile(second)], context.ExecutionCancellationToken);

        details.Operation.BackupStatus.Should().Be(BackupStatus.Failed);
        details.Operation.LastError.Should().Contain("cancelled");
    }

    [Fact]
    public async Task OperationHistory_UpdatesCorrectly()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));
        await File.WriteAllTextAsync(path, "unexpected");
        await context.SaveTransactionAsync(details, new TransactionEntry(path, Hash("applied"), true, ApplyResultStatus.Applied));

        await context.RollbackService.ExecuteAsync(details.Operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        var history = await context.HistoryService.GetOperationsAsync(CancellationToken.None);

        history.Should().ContainSingle(entry =>
            entry.OperationId == details.Operation.OperationId &&
            entry.BackupStatus == BackupStatus.Verified &&
            entry.RollbackStatus == RollbackTransactionStatus.CompletedWithConflicts &&
            entry.ConflictCount == 1);
    }

    [Fact]
    public async Task RecoveryServices_StayInsideTemporaryDirectories()
    {
        using var context = new RecoveryTestContext();
        var path = context.WriteTextFile(@"state\organization.txt", "original");
        var details = await context.CreateBackupAsync(new SourceFile(path));

        details.Operation.OperationFolder.Should().StartWith(context.BackupsRoot);
        details.Manifest!.Files.Should().OnlyContain(file => file.SourceTargetPath.StartsWith(context.LiveRoot, StringComparison.OrdinalIgnoreCase));
        details.Operation.OperationFolder.Should().NotStartWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }

    private sealed class RecoveryTestContext : IDisposable
    {
        private readonly CancellationTokenSource _executionCts = new();

        public RecoveryTestContext(RecoveryServiceHooks? hooks = null)
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerRecoveryTests", Guid.NewGuid().ToString("N"));
            LiveRoot = Path.Combine(RootPath, "Live");
            BackupsRoot = Path.Combine(RootPath, "LocalAppData", "PenumbraOrganizer", "Backups");
            Directory.CreateDirectory(LiveRoot);

            HistoryService = new OperationHistoryService(BackupsRoot);
            BackupVerificationService = new BackupVerificationService(BackupsRoot, HistoryService);
            RollbackVerificationService = new RollbackVerificationService(BackupsRoot, HistoryService);
            BackupService = new BackupService(BackupsRoot, BackupVerificationService, HistoryService, hooks);
            RollbackService = new RollbackService(BackupsRoot, RollbackVerificationService, HistoryService, hooks);
        }

        public string RootPath { get; }
        public string LiveRoot { get; }
        public string BackupsRoot { get; }
        public string HistoryIndexPath => Path.Combine(BackupsRoot, "history-index.json");
        public OperationHistoryService HistoryService { get; }
        public BackupVerificationService BackupVerificationService { get; }
        public RollbackVerificationService RollbackVerificationService { get; }
        public BackupService BackupService { get; }
        public RollbackService RollbackService { get; }
        public CancellationToken ExecutionCancellationToken => _executionCts.Token;

        public string WriteTextFile(string relativePath, string contents)
        {
            var path = Path.Combine(LiveRoot, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public string WriteBinaryFile(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(LiveRoot, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public async Task<OperationPackageDetails> CreateBackupAsync(params SourceFile[] files)
            => await CreateBackupAsync(files, CancellationToken.None);

        public async Task<OperationPackageDetails> CreateBackupAsync(SourceFile[] files, CancellationToken cancellationToken)
        {
            var request = new BackupRequest(
                Guid.NewGuid(),
                "scan-identity",
                files.Select(file => new BackupFileRequest(file.Path, file.Protected, ["scan-id"], "plan-id")).ToArray(),
                ApplicationVersion: "tests",
                PenumbraVersion: "1.6.1.10",
                AffectedModCount: files.Length);

            return await BackupService.CreateBackupAsync(request, cancellationToken);
        }

        public async Task SaveTransactionAsync(OperationPackageDetails details, params TransactionEntry[] entries)
        {
            var manifestBySource = details.Manifest!.Files.ToDictionary(file => file.SourceTargetPath, StringComparer.OrdinalIgnoreCase);
            var transaction = new RollbackTransaction(
                details.Operation.OperationId,
                DateTimeOffset.UtcNow,
                details.Operation.ApplicationVersion,
                details.Operation.PenumbraVersion,
                details.Operation.ScanIdentity,
                RollbackTransactionStatus.Available,
                entries.Select(entry =>
                {
                    var manifest = manifestBySource[entry.TargetPath];
                    return new RollbackFileEntry(
                        entry.TargetPath,
                        manifest.RelativeBackupPath,
                        manifest.OriginalSha256,
                        manifest.OriginalLength,
                        entry.ExpectedAppliedSha256,
                        entry.ExistedBeforeApply,
                        manifest.Classification,
                        false,
                        entry.ApplyResultStatus,
                        RollbackFileStatus.Pending,
                        manifest.AssociatedStableScanIds,
                        manifest.WritablePlanOperationId,
                        null,
                        null,
                        null);
                }).ToArray(),
                null,
                null,
                null);

            await RollbackService.SaveTransactionAsync(transaction, CancellationToken.None);
        }

        public string GetOperationDirectory(Guid operationId)
            => Path.Combine(BackupsRoot, operationId.ToString("N"));

        public string GetManifestPath(Guid operationId)
            => Path.Combine(GetOperationDirectory(operationId), "manifest.json");

        public string ResolveBackupFilePath(Guid operationId, BackupFileEntry entry)
            => Path.Combine(GetOperationDirectory(operationId), entry.RelativeBackupPath.Replace('/', Path.DirectorySeparatorChar));

        public void CancelCurrentExecution() => _executionCts.Cancel();

        public void Dispose()
        {
            _executionCts.Dispose();
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
        }
    }

    private sealed record SourceFile(string Path, bool Protected = false);
    private sealed record TransactionEntry(string TargetPath, string ExpectedAppliedSha256, bool ExistedBeforeApply, ApplyResultStatus ApplyResultStatus);

    private static string Hash(string value)
        => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value));
}
