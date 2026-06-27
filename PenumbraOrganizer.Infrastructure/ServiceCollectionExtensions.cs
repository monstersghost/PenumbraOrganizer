namespace PenumbraOrganizer.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Compatibility;
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
        services.AddSingleton<IInventoryExportService, InventoryExportService>();
        services.AddSingleton<IAiProposalValidationService, AiProposalValidationService>();
        services.AddSingleton<IOrganizerMutationService, OrganizerMutationService>();
        services.AddSingleton<IOrganizerProposalValidationService, OrganizerProposalValidationService>();
        services.AddSingleton<IOrganizerSessionService, OrganizerSessionService>();
        services.AddSingleton<IOperationHistoryService, OperationHistoryService>();
        services.AddSingleton<IBackupVerificationService, BackupVerificationService>();
        services.AddSingleton<IRollbackVerificationService, RollbackVerificationService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRollbackService, RollbackService>();
        return services;
    }
}
