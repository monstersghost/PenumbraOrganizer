using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Core.Interfaces;

public interface IPenumbraDiscoveryService
{
    Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
    Task<PenumbraInstallation?> ValidateManualSelectionAsync(string configPath, string? modRoot, string? pluginAssemblyPath, CancellationToken cancellationToken);
}

public interface IPenumbraScanService
{
    Task<ScanInventory> ScanAsync(PenumbraInstallation installation, IProgress<string>? progress, CancellationToken cancellationToken);
}

public interface IPenumbraCompatibilityService
{
    Task<CompatibilityReport> EvaluateAsync(PenumbraInstallation installation, ScanInventory inventory, CancellationToken cancellationToken);
}

public interface IInventoryExportService
{
    Task<InventoryExportResult> CreateAiReviewPackageAsync(
        ScanInventory inventory,
        CancellationToken cancellationToken,
        OrganizationPreferences? organizationPreferences = null);

    Task ValidateExportPackageAsync(string exportFolder, CancellationToken cancellationToken);
}

public interface IAiProposalValidationService
{
    AiProposalValidationResult Validate(AiInventoryExport inventory, AiProposalDocument proposal);
}

public interface IAiProposalImportService
{
    Task<AiProposalImportResult> ImportAsync(
        string proposalPath,
        string inventoryPath,
        IReadOnlyList<OrganizerModProposal> currentProposals,
        CancellationToken cancellationToken);
}

public interface IOrganizerMutationService
{
    OrganizerMutationResult AssignToFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, IReadOnlyList<string> stableScanIds, string proposedFolder);
    OrganizerMutationResult ReturnToCurrent(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds);
    OrganizerMutationResult Protect(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds);
    OrganizerMutationResult Unprotect(IList<OrganizerModProposal> mods, IReadOnlyList<string> stableScanIds);
    OrganizerMutationResult CreateFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string proposedFolder);
    OrganizerMutationResult RenameFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string oldPath, string newPath);
    OrganizerMutationResult DeleteEmptyFolder(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, string path);
    void ApplyUndo(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, OrganizerHistoryEntry entry);
    void ApplyRedo(IList<OrganizerModProposal> mods, IList<OrganizerFolder> folders, OrganizerHistoryEntry entry);
}

public interface IOrganizerProposalValidationService
{
    OrganizerValidationResult Validate(
        ScanInventory inventory,
        IReadOnlyList<OrganizerModProposal> proposals,
        IReadOnlyList<OrganizerFolder> folders,
        OrganizationPreferences organizationPreferences);
}

public interface IOrganizerSessionService
{
    string SessionsDirectory { get; }
    string LastSessionPath { get; }
    Task SaveLastSessionAsync(OrganizerSessionDocument session, CancellationToken cancellationToken);
    Task<OrganizerSessionRestoreResult> TryLoadLastSessionAsync(ScanInventory inventory, CancellationToken cancellationToken);
    Task DiscardLastSessionAsync(CancellationToken cancellationToken);
}

public interface ICreatorCanonicalizer
{
    string Canonicalize(string creatorName);
}

public interface IProtectionService
{
    bool IsProtectedPath(string currentVirtualFolder);
}

public interface IBackupService
{
    Task<OperationPackageDetails> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken);
}

public interface IBackupVerificationService
{
    Task<BackupVerificationResult> VerifyAsync(Guid operationId, CancellationToken cancellationToken);
}

public interface IRollbackService
{
    Task<RollbackTransaction> SaveTransactionAsync(RollbackTransaction transaction, CancellationToken cancellationToken);
    Task<RollbackResult> ExecuteAsync(Guid operationId, RollbackExecutionOptions options, CancellationToken cancellationToken);
}

public interface IRollbackVerificationService
{
    Task<RollbackVerificationResult> VerifyAsync(Guid operationId, CancellationToken cancellationToken);
}

public interface IOperationHistoryService
{
    Task<IReadOnlyList<OperationHistoryEntry>> GetOperationsAsync(CancellationToken cancellationToken);
    Task<OperationPackageDetails?> TryLoadOperationAsync(Guid operationId, CancellationToken cancellationToken);
    Task<OperationHistoryEntry> RefreshOperationAsync(Guid operationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperationHistoryEntry>> RebuildIndexAsync(CancellationToken cancellationToken);
}

public interface IDryRunPlanner
{
    Task<DryRunPlan> CreatePlanAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
}

public interface IDryRunValidationService
{
    Task<DryRunValidationResult> ValidateAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
}

public interface IPenumbraVirtualFolderWriter
{
    Task<IReadOnlyList<DryRunSourceFileSnapshot>> CaptureSourceFilesAsync(PenumbraInstallation installation, CancellationToken cancellationToken);
    Task<IReadOnlyList<SchemaFingerprint>> CaptureSchemaFingerprintsAsync(PenumbraInstallation installation, CancellationToken cancellationToken);
    Task<IReadOnlyList<DryRunPlanEntry>> MapPlanEntriesAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<DryRunFileChange>> BuildExpectedFileChangesAsync(
        PenumbraInstallation installation,
        IReadOnlyList<DryRunPlanEntry> planEntries,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
}

public interface IApplyService
{
    Task<ApplyOperation> PrepareAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
    Task<ApplyResult> ApplyAsync(
        DryRunPlan plan,
        ApplyOperation operation,
        PenumbraInstallation installation,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
}

public interface IRealInstallationValidationService
{
    Task<RealInstallationValidationResult> ValidateAsync(
        PenumbraInstallation installation,
        ProposalSnapshot? proposalSnapshot,
        RealInstallationValidationOptions options,
        CancellationToken cancellationToken);
}

public interface IControlledLiveTestService
{
    Task<ControlledTestSetup> BuildSetupAsync(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        ControlledTestOptions options,
        CancellationToken cancellationToken);

    ProposalSnapshot BuildControlledSnapshot(
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        ControlledTestRequest request);
}

public interface IPostApplyVerificationService
{
    Task<PostApplyVerificationResult> VerifyAsync(
        DryRunPlan plan,
        ApplyResult applyResult,
        CancellationToken cancellationToken);
}

public interface IWritePermissionPreflightService
{
    Task<WritePermissionPreflightResult> CheckAsync(
        DryRunPlan plan,
        CancellationToken cancellationToken);
}

public interface IPlanInvalidationService
{
    Task<IReadOnlyList<PlanInvalidationReason>> GetInvalidationReasonsAsync(
        DryRunPlan plan,
        PenumbraInstallation installation,
        ScanInventory inventory,
        ProposalSnapshot proposalSnapshot,
        CancellationToken cancellationToken);
}

public interface IDiagnosticExportService
{
    Task<DiagnosticExportResult> CreateAsync(DiagnosticExportRequest request, CancellationToken cancellationToken);
}

public interface IOperationRecoveryService
{
    Task<IReadOnlyList<IncompleteOperationRecord>> GetIncompleteOperationsAsync(CancellationToken cancellationToken);

    Task<BackupVerificationResult> ReverifyBackupAsync(Guid operationId, CancellationToken cancellationToken);

    Task<PostApplyVerificationResult> ContinueVerificationAsync(Guid operationId, CancellationToken cancellationToken);
}

public interface IOperationObservationService
{
    Task<BackupOperation> SaveObservationAsync(
        Guid operationId,
        PenumbraUiObservationStatus status,
        CancellationToken cancellationToken);
}
