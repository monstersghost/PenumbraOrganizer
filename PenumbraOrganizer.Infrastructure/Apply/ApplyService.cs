namespace PenumbraOrganizer.Infrastructure.Apply;

using System.Security.Cryptography;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Infrastructure.Recovery;

public sealed class ApplyService : IApplyService
{

    private readonly IDryRunValidationService _validationService;
    private readonly IWritePermissionPreflightService _preflightService;
    private readonly IBackupService _backupService;
    private readonly IRollbackService _rollbackService;
    private readonly IPostApplyVerificationService _postApplyVerificationService;
    private readonly IOperationHistoryService _historyService;
    private readonly RecoveryStorageLayout _layout;

    public ApplyService(
        IDryRunValidationService validationService,
        IWritePermissionPreflightService preflightService,
        IBackupService backupService,
        IRollbackService rollbackService,
        IPostApplyVerificationService postApplyVerificationService,
        IOperationHistoryService historyService)
        : this(
            validationService,
            preflightService,
            backupService,
            rollbackService,
            postApplyVerificationService,
            historyService,
            new RecoveryStorageLayout())
    {
    }

    public ApplyService(
        IDryRunValidationService validationService,
        IWritePermissionPreflightService preflightService,
        IBackupService backupService,
        IRollbackService rollbackService,
        IPostApplyVerificationService postApplyVerificationService,
        IOperationHistoryService historyService,
        string backupsRoot)
        : this(
            validationService,
            preflightService,
            backupService,
            rollbackService,
            postApplyVerificationService,
            historyService,
            new RecoveryStorageLayout(backupsRoot))
    {
    }

    internal ApplyService(
        IDryRunValidationService validationService,
        IWritePermissionPreflightService preflightService,
        IBackupService backupService,
        IRollbackService rollbackService,
        IPostApplyVerificationService postApplyVerificationService,
        IOperationHistoryService historyService,
        RecoveryStorageLayout layout)
    {
        _validationService = validationService;
        _preflightService = preflightService;
        _backupService = backupService;
        _rollbackService = rollbackService;
        _postApplyVerificationService = postApplyVerificationService;
        _historyService = historyService;
        _layout = layout;
    }

    public async Task<ApplyOperation> PrepareAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        if (plan.FileChanges.Count == 0)
            throw new InvalidOperationException("This dry run does not contain any supported writable changes.");

        EnsureWriteTargetsExist(plan);

        var inventory = new ScanInventory
        {
            Installation = installation,
            ScannedAtUtc = plan.CreatedAtUtc,
            Mods = proposalSnapshot.Proposals.Select(proposal => new ModScanResult
            {
                StableScanId = proposal.StableScanId,
                PhysicalDirectory = string.Empty,
                PhysicalDirectoryName = proposal.StableScanId,
                CurrentVirtualFolder = proposal.CurrentVirtualFolder,
                Name = proposal.Name,
                Author = proposal.OriginalCreator,
                Protected = proposal.Protected,
            }).ToArray(),
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };

        var validation = await _validationService.ValidateAsync(plan, installation, inventory, proposalSnapshot, cancellationToken);
        if (!validation.ApplyPermitted)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors.Concat(validation.Warnings).DefaultIfEmpty("The dry run is not apply-ready.")));

        var preflight = await _preflightService.CheckAsync(plan, cancellationToken);
        if (!preflight.Succeeded)
            throw new InvalidOperationException(string.Join(Environment.NewLine, preflight.Errors));

        var operationId = Guid.NewGuid();
        var writeTargetsByPath = new HashSet<string>(plan.FileChanges.Select(change => change.TargetPath), StringComparer.OrdinalIgnoreCase);
        var scanIdsByPath = plan.FileChanges.ToDictionary(
            change => change.TargetPath,
            change => plan.Entries.Where(entry => entry.TargetPath.Equals(change.TargetPath, StringComparison.OrdinalIgnoreCase) && entry.RequiresWrite)
                .Select(entry => entry.StableScanId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        // Back up the whole Penumbra config directory, not just the files Apply is about to
        // write, so rollback can restore everything to its pre-Apply state even if something
        // outside our own writes (a crash, Penumbra itself) touches other files in the meantime.
        var backupRequest = new BackupRequest(
            operationId,
            plan.ScanIdentity,
            EnumerateConfigDirectoryFiles(installation.ConfigDirectory)
                .Select(path => new BackupFileRequest(
                    path,
                    Protected: false,
                    scanIdsByPath.TryGetValue(path, out var scanIds) ? scanIds : Array.Empty<string>(),
                    plan.PlanId.ToString("N"),
                    IsWriteTarget: writeTargetsByPath.Contains(path)))
                .ToArray(),
            plan.ApplicationVersion,
            plan.InstalledPenumbraVersion,
            plan.Summary.AffectedModCount);

        var backupDetails = await _backupService.CreateBackupAsync(backupRequest, cancellationToken);
        if (backupDetails.Operation.VerificationStatus != OperationVerificationStatus.Verified)
            throw new InvalidOperationException(backupDetails.Operation.LastError ?? "Backup verification failed.");

        var manifest = backupDetails.Manifest ?? throw new InvalidOperationException("The verified backup manifest is missing.");
        var expectedShaByPath = plan.FileChanges.ToDictionary(change => change.TargetPath, change => change.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
        var rollbackTransaction = new RollbackTransaction(
            operationId,
            DateTimeOffset.UtcNow,
            plan.ApplicationVersion,
            plan.InstalledPenumbraVersion,
            plan.ScanIdentity,
            RollbackTransactionStatus.Available,
            manifest.Files.Select(manifestEntry =>
            {
                var isWriteTarget = writeTargetsByPath.Contains(manifestEntry.SourceTargetPath);
                return new RollbackFileEntry(
                    manifestEntry.SourceTargetPath,
                    manifestEntry.RelativeBackupPath,
                    manifestEntry.OriginalSha256,
                    manifestEntry.OriginalLength,
                    isWriteTarget ? expectedShaByPath[manifestEntry.SourceTargetPath] : manifestEntry.OriginalSha256,
                    ExistedBeforeApply: true,
                    manifestEntry.Classification,
                    Protected: false,
                    // Files Apply intends to write start Pending and get their real status once
                    // ApplyAsync runs. Everything else in the full-directory backup is expected to
                    // stay byte-identical, so mark it Applied now -- that keeps it in scope for
                    // ExecuteAsync's normal restore/conflict-detection instead of being skipped as
                    // "not part of this operation".
                    isWriteTarget ? ApplyResultStatus.Pending : ApplyResultStatus.Applied,
                    RollbackFileStatus.Pending,
                    manifestEntry.AssociatedStableScanIds,
                    plan.PlanId.ToString("N"),
                    null,
                    null,
                    null);
            }).ToArray(),
            null,
            null,
            null);
        await _rollbackService.SaveTransactionAsync(rollbackTransaction, cancellationToken);

        var applyOperation = new ApplyOperation(
            operationId,
            plan.PlanId,
            DateTimeOffset.UtcNow,
            plan.ApplicationVersion,
            plan.InstalledPenumbraVersion,
            plan.ScanIdentity,
            plan.InstallationIdentity,
            plan.OrganizationSessionIdentity,
            plan.ProposalSnapshotIdentity,
            operationId,
            preflight,
            ApplyStatus.Ready,
            RollbackAvailable: false,
            LastError: null);

        await AtomicJsonFileStore.WriteAsync(
            _layout.GetPlanPath(operationId),
            plan,
            value => value.PlanId == plan.PlanId,
            cancellationToken);
        await WriteApplyDocumentAsync(operationId, new ApplyPackageDocument(applyOperation, null), cancellationToken);
        await UpdateOperationStateAsync(operationId, ApplyStatus.Ready, rollbackAvailable: false, null, cancellationToken);
        await _historyService.RefreshOperationAsync(operationId, cancellationToken);

        return applyOperation;
    }

    public async Task<ApplyResult> ApplyAsync(
        DryRunPlan plan,
        ApplyOperation operation,
        PenumbraInstallation installation,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken)
    {
        var currentPackage = await ReadApplyDocumentAsync(operation.OperationId, cancellationToken);
        if (currentPackage.Operation is null)
            throw new InvalidOperationException("The apply preparation record is missing.");

        var details = await _historyService.TryLoadOperationAsync(operation.OperationId, cancellationToken)
                      ?? throw new InvalidOperationException("The prepared backup operation could not be reloaded.");
        if (details.Manifest is null || details.RollbackTransaction is null)
            throw new InvalidOperationException("The apply foundation is incomplete because backup or rollback metadata is missing.");
        if (details.Operation.VerificationStatus != OperationVerificationStatus.Verified)
            throw new InvalidOperationException("Apply is blocked because the backup is not verified.");

        var validation = await _validationService.ValidateAsync(
            plan,
            installation,
            BuildInventory(plan, proposalSnapshot, installation),
            proposalSnapshot,
            cancellationToken);
        if (validation.Errors.Count > 0 || validation.Status == DryRunPlanValidationStatus.Invalid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors.Concat(validation.Warnings).DefaultIfEmpty("The dry run is no longer valid.")));

        var preflight = await _preflightService.CheckAsync(plan, cancellationToken);
        if (!preflight.Succeeded)
            throw new InvalidOperationException(string.Join(Environment.NewLine, preflight.Errors));

        var operationRecord = details.Operation with { ApplyStatus = ApplyStatus.InProgress };
        var transaction = details.RollbackTransaction with { Status = RollbackTransactionStatus.Available };
        await AtomicJsonFileStore.WriteAsync(_layout.GetOperationPath(operation.OperationId), operationRecord, value => value.OperationId == operation.OperationId, cancellationToken);
        await WriteApplyDocumentAsync(operation.OperationId, currentPackage with
        {
            Operation = currentPackage.Operation with
            {
                Status = ApplyStatus.InProgress,
                Preflight = preflight,
            }
        }, cancellationToken);

        var results = new List<ApplyFileResult>();
        var transactionFiles = transaction.Files.ToDictionary(file => file.TargetPath, StringComparer.OrdinalIgnoreCase);
        var anyWriteCompleted = false;

        try
        {
            foreach (var fileChange in plan.FileChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ApplyFileAsync(plan, fileChange, installation, cancellationToken);
                results.Add(result);
                anyWriteCompleted |= result.WriteCompleted;

                var transactionFile = transactionFiles[fileChange.TargetPath];
                transactionFiles[fileChange.TargetPath] = transactionFile with
                {
                    ApplyResultStatus = result.Status,
                    ObservedLiveSha256 = result.FinalSha256,
                    Message = result.Message,
                };
                transaction = transaction with { Files = transactionFiles.Values.OrderBy(file => file.TargetPath, StringComparer.OrdinalIgnoreCase).ToArray() };
                await PersistApplyProgressAsync(operation.OperationId, currentPackage.Operation!, results, transaction, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            var cancelled = BuildApplyResult(operation, results, ApplyStatus.Cancelled, rollbackAvailable: anyWriteCompleted, "Apply was cancelled.");
            await PersistApplyCompletionAsync(operation.OperationId, currentPackage.Operation!, cancelled, transaction, cancellationToken);
            throw;
        }

        var interim = BuildApplyResult(operation, results, ComputeStatus(results), rollbackAvailable: anyWriteCompleted, null);
        var verification = await _postApplyVerificationService.VerifyAsync(plan, interim, installation, cancellationToken);
        var finalStatus = verification.Succeeded
            ? interim.Status
            : anyWriteCompleted
                ? ApplyStatus.PartiallyCompleted
                : ApplyStatus.Failed;
        var lastError = verification.Succeeded ? interim.LastError : string.Join(Environment.NewLine, verification.Errors);
        var final = interim with
        {
            Status = finalStatus,
            RollbackAvailable = anyWriteCompleted,
            LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError,
        };

        var verificationDocument = await AtomicJsonFileStore.ReadAsync<OperationVerificationDocument>(_layout.GetVerificationPath(operation.OperationId), cancellationToken)
            ?? new OperationVerificationDocument();
        verificationDocument = verificationDocument with { PostApplyVerification = verification };
        await AtomicJsonFileStore.WriteAsync(_layout.GetVerificationPath(operation.OperationId), verificationDocument, _ => true, cancellationToken);

        await PersistApplyCompletionAsync(operation.OperationId, currentPackage.Operation!, final, transaction, cancellationToken);
        return final;
    }

    // A fresh install may have no sort_order.json yet, and a mod may have no per-user
    // mod_data/<id>.json. Materialize the baseline the planner actually hashed as the source
    // (Penumbra's own sort_order.json.bak when present, otherwise the canonical empty document —
    // see PenumbraSortOrder.LoadBaselineText) before backup, so the proven backup/apply/rollback
    // machinery operates on a real, captured file and never mistakes a recovered organization for
    // an empty one. Rollback restores exactly this baseline.
    private static void EnsureWriteTargetsExist(DryRunPlan plan)
    {
        foreach (var change in plan.FileChanges)
        {
            if (change.WriteTargetKind != PenumbraWriteTargetKind.SortOrderJson)
                continue;

            var target = RecoveryStorageLayout.ValidateAbsoluteTargetPath(change.TargetPath);
            if (File.Exists(target))
                continue;

            var baseline = PenumbraSortOrder.LoadBaselineText(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, System.Text.Encoding.UTF8.GetBytes(baseline));
        }
    }

    private static IReadOnlyList<string> EnumerateConfigDirectoryFiles(string configDirectory)
        => Directory.Exists(configDirectory)
            ? Directory.EnumerateFiles(configDirectory, "*", SearchOption.AllDirectories).ToArray()
            : Array.Empty<string>();

    private static ScanInventory BuildInventory(
        DryRunPlan plan,
        ProposalSnapshot proposalSnapshot,
        PenumbraInstallation installation)
        => new()
        {
            Installation = installation,
            ScannedAtUtc = plan.CreatedAtUtc,
            Mods = proposalSnapshot.Proposals.Select(proposal => new ModScanResult
            {
                StableScanId = proposal.StableScanId,
                PhysicalDirectory = string.Empty,
                PhysicalDirectoryName = proposal.StableScanId,
                CurrentVirtualFolder = proposal.CurrentVirtualFolder,
                Name = proposal.Name,
                Author = proposal.OriginalCreator,
                Protected = proposal.Protected,
            }).ToArray(),
            CurrentFolderTree = Array.Empty<VirtualFolderNode>(),
            Collections = Array.Empty<CollectionInventory>(),
            Warnings = Array.Empty<string>(),
        };

    private async Task<ApplyFileResult> ApplyFileAsync(
        DryRunPlan plan,
        DryRunFileChange fileChange,
        PenumbraInstallation installation,
        CancellationToken cancellationToken)
    {
        var targetPath = RecoveryStorageLayout.ValidateAbsoluteTargetPath(fileChange.TargetPath);
        var currentInstalledVersion = PenumbraInstalledVersionReader.Read(installation) ?? installation.InstalledVersion ?? "Unknown";
        if (!string.Equals(currentInstalledVersion, plan.InstalledPenumbraVersion, StringComparison.Ordinal))
        {
            return new ApplyFileResult(
                targetPath,
                fileChange.ExactRecordKey,
                ApplyResultStatus.Failed,
                fileChange.SourceSha256,
                fileChange.ExpectedSha256,
                null,
                "The installed Penumbra version changed before Apply.",
                WriteCompleted: false);
        }

        var liveBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
        var liveHash = Convert.ToHexString(SHA256.HashData(liveBytes));
        if (!string.Equals(liveHash, fileChange.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplyFileResult(
                targetPath,
                fileChange.ExactRecordKey,
                ApplyResultStatus.Failed,
                fileChange.SourceSha256,
                fileChange.ExpectedSha256,
                liveHash,
                "The authoritative source hash changed after the dry run was created.",
                WriteCompleted: false);
        }

        var expectedBytes = Convert.FromBase64String(fileChange.ExpectedBytesBase64);
        var expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes));
        if (!string.Equals(expectedHash, fileChange.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplyFileResult(
                targetPath,
                fileChange.ExactRecordKey,
                ApplyResultStatus.Failed,
                fileChange.SourceSha256,
                fileChange.ExpectedSha256,
                null,
                "The dry run expected-result bytes no longer match their recorded hash.",
                WriteCompleted: false);
        }

        var tempPath = targetPath + ".apply.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, expectedBytes, cancellationToken);
            await ValidateTemporaryOutputAsync(tempPath, fileChange, plan, cancellationToken);
            await FlushFileAsync(tempPath, cancellationToken);

            File.Move(tempPath, targetPath, overwrite: true);

            var finalBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
            var finalHash = Convert.ToHexString(SHA256.HashData(finalBytes));
            if (!string.Equals(finalHash, fileChange.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ApplyFileResult(
                    targetPath,
                    fileChange.ExactRecordKey,
                    ApplyResultStatus.Failed,
                    fileChange.SourceSha256,
                    fileChange.ExpectedSha256,
                    finalHash,
                    "The applied file hash did not match the planned hash after atomic replacement.",
                    WriteCompleted: true);
            }

            return new ApplyFileResult(
                targetPath,
                fileChange.ExactRecordKey,
                ApplyResultStatus.Applied,
                fileChange.SourceSha256,
                fileChange.ExpectedSha256,
                finalHash,
                "The virtual-folder mapping was applied atomically.",
                WriteCompleted: true);
        }
        catch (Exception ex)
        {
            return new ApplyFileResult(
                targetPath,
                fileChange.ExactRecordKey,
                ApplyResultStatus.Failed,
                fileChange.SourceSha256,
                fileChange.ExpectedSha256,
                null,
                ex.Message,
                WriteCompleted: false);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static async Task ValidateTemporaryOutputAsync(string tempPath, DryRunFileChange fileChange, DryRunPlan plan, CancellationToken cancellationToken)
    {
        if (fileChange.WriteTargetKind == PenumbraWriteTargetKind.ModDataDb)
        {
            // mod_data.db is binary, not text/JSON. Correctness was already verified once at
            // plan-build time, against byte-identical content: ModDataDbVirtualFolderWriter
            // self-verifies each folder update via the real LiteDB engine before those bytes ever
            // become the plan's expected bytes, and the hash chain around this call guarantees
            // what's on disk now is exactly that. Re-deriving the same check here would need
            // PenumbraInstallation threaded into this private static method for no new information.
            return;
        }

        var text = await File.ReadAllTextAsync(tempPath, cancellationToken);

        if (fileChange.WriteTargetKind == PenumbraWriteTargetKind.SortOrderJson)
        {
            var sortOrder = PenumbraSortOrder.Parse(text);
            foreach (var entry in plan.Entries.Where(entry => entry.RequiresWrite))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(sortOrder.GetFolderFor(entry.RecordKey), entry.ProposedVirtualFolder, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The expected-result sort_order.json does not contain the planned folder for {entry.RecordKey}.");
            }

            return;
        }

        // Metadata files (meta.json, mod_data/<id>.json) must remain well-formed JSON objects.
        using var document = System.Text.Json.JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException($"The expected-result metadata file is not a JSON object: {fileChange.TargetPath}");
    }

    private async Task PersistApplyProgressAsync(
        Guid operationId,
        ApplyOperation preparedOperation,
        IReadOnlyList<ApplyFileResult> results,
        RollbackTransaction transaction,
        CancellationToken cancellationToken)
    {
        var status = ComputeStatus(results);
        var applyResult = BuildApplyResult(preparedOperation, results, status, rollbackAvailable: results.Any(result => result.WriteCompleted), null);
        await PersistApplyCompletionAsync(operationId, preparedOperation, applyResult, transaction, cancellationToken);
    }

    private async Task PersistApplyCompletionAsync(
        Guid operationId,
        ApplyOperation preparedOperation,
        ApplyResult applyResult,
        RollbackTransaction transaction,
        CancellationToken cancellationToken)
    {
        await WriteApplyDocumentAsync(
            operationId,
            new ApplyPackageDocument(
                preparedOperation with
                {
                    Status = applyResult.Status,
                    RollbackAvailable = applyResult.RollbackAvailable,
                    LastError = applyResult.LastError,
                },
                applyResult),
            cancellationToken);

        await AtomicJsonFileStore.WriteAsync(
            _layout.GetRollbackPath(operationId),
            transaction,
            value => value.OperationId == operationId,
            cancellationToken);

        await UpdateOperationStateAsync(operationId, applyResult.Status, applyResult.RollbackAvailable, applyResult.LastError, cancellationToken);
        await _historyService.RefreshOperationAsync(operationId, cancellationToken);
    }

    private async Task UpdateOperationStateAsync(
        Guid operationId,
        ApplyStatus applyStatus,
        bool rollbackAvailable,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var operation = await AtomicJsonFileStore.ReadRequiredAsync<BackupOperation>(_layout.GetOperationPath(operationId), cancellationToken);
        var updated = operation with
        {
            ApplyStatus = applyStatus,
            RollbackAvailable = rollbackAvailable,
            FailureCount = applyStatus is ApplyStatus.Failed or ApplyStatus.PartiallyCompleted
                ? Math.Max(operation.FailureCount, 1)
                : operation.FailureCount,
            LastError = lastError,
        };

        await AtomicJsonFileStore.WriteAsync(
            _layout.GetOperationPath(operationId),
            updated,
            value => value.OperationId == operationId,
            cancellationToken);
    }

    private async Task<ApplyPackageDocument> ReadApplyDocumentAsync(Guid operationId, CancellationToken cancellationToken)
        => await AtomicJsonFileStore.ReadRequiredAsync<ApplyPackageDocument>(_layout.GetApplyPath(operationId), cancellationToken);

    private Task WriteApplyDocumentAsync(Guid operationId, ApplyPackageDocument document, CancellationToken cancellationToken)
        => AtomicJsonFileStore.WriteAsync(
            _layout.GetApplyPath(operationId),
            document,
            value => value.Operation?.OperationId == operationId || value.Result?.OperationId == operationId,
            cancellationToken);

    private static ApplyResult BuildApplyResult(
        ApplyOperation operation,
        IReadOnlyList<ApplyFileResult> results,
        ApplyStatus status,
        bool rollbackAvailable,
        string? lastError)
        => new(
            operation.OperationId,
            operation.PlanId,
            status,
            DateTimeOffset.UtcNow,
            results.ToArray(),
            rollbackAvailable,
            lastError ?? string.Join(Environment.NewLine, results.Where(result => result.Status == ApplyResultStatus.Failed).Select(result => result.Message).Distinct(StringComparer.OrdinalIgnoreCase)));

    private static ApplyStatus ComputeStatus(IReadOnlyList<ApplyFileResult> results)
    {
        if (results.Count == 0)
            return ApplyStatus.Pending;

        var applied = results.Count(result => result.Status == ApplyResultStatus.Applied);
        var failed = results.Count(result => result.Status == ApplyResultStatus.Failed);

        if (failed == 0 && applied == results.Count)
            return ApplyStatus.Completed;
        if (applied > 0 && failed > 0)
            return ApplyStatus.PartiallyCompleted;
        if (failed > 0)
            return ApplyStatus.Failed;
        return ApplyStatus.Pending;
    }

    private static async Task FlushFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }
}
