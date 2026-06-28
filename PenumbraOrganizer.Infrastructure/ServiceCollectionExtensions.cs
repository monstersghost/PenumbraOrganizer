namespace PenumbraOrganizer.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Compatibility;
using PenumbraOrganizer.Infrastructure.Diagnostics;
using PenumbraOrganizer.Infrastructure.Discovery;
using PenumbraOrganizer.Infrastructure.Exports;
using PenumbraOrganizer.Infrastructure.Recovery;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Infrastructure.Sessions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPenumbraOrganizerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICreatorCanonicalizer, CreatorCanonicalizer>();
        services.AddSingleton<IProtectionService, ProtectionService>();
        services.AddSingleton<IPenumbraDiscoveryService, PenumbraDiscoveryService>();
        services.AddSingleton<IPenumbraScanService, PenumbraScanService>();
        services.AddSingleton<IPenumbraCompatibilityService, PenumbraCompatibilityService>();
        services.AddSingleton<IPenumbraVirtualFolderWriter, PenumbraVirtualFolderWriter>();
        services.AddSingleton<IPlanInvalidationService, PlanInvalidationService>();
        services.AddSingleton<IDryRunValidationService, DryRunValidationService>();
        services.AddSingleton<IDryRunPlanner, DryRunPlanner>();
        services.AddSingleton<IControlledLiveTestService, ControlledLiveTestService>();
        services.AddSingleton<IWritePermissionPreflightService, WritePermissionPreflightService>();
        services.AddSingleton<IRealInstallationValidationService, RealInstallationValidationService>();
        services.AddSingleton<IPostApplyVerificationService, PostApplyVerificationService>();
        services.AddSingleton<IInventoryExportService, InventoryExportService>();
        services.AddSingleton<IWorkbookWorkflowService, WorkbookWorkflowService>();
        services.AddSingleton<IAiProposalValidationService, AiProposalValidationService>();
        services.AddSingleton<IAiProposalImportService, AiProposalImportService>();
        services.AddSingleton<IOrganizerMutationService, OrganizerMutationService>();
        services.AddSingleton<IOrganizerProposalValidationService, OrganizerProposalValidationService>();
        services.AddSingleton<IOrganizerSessionService, OrganizerSessionService>();
        services.AddSingleton<IOperationHistoryService, OperationHistoryService>();
        services.AddSingleton<IBackupVerificationService, BackupVerificationService>();
        services.AddSingleton<IRollbackVerificationService, RollbackVerificationService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRollbackService, RollbackService>();
        services.AddSingleton<IOperationRecoveryService, OperationRecoveryService>();
        services.AddSingleton<IOperationObservationService, OperationObservationService>();
        services.AddSingleton<IApplyService, ApplyService>();
        services.AddSingleton<IDiagnosticExportService, DiagnosticExportService>();
        return services;
    }
}
