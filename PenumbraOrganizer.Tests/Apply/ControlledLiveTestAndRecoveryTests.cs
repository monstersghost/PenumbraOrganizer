namespace PenumbraOrganizer.Tests.Apply;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Diagnostics;
using PenumbraOrganizer.Infrastructure.Recovery;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Infrastructure.Sessions;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class ControlledLiveTestAndRecoveryTests
{
    [Fact]
    public async Task ControlledSetup_EnforcesDefaultLimit_AndBlocksProtectedAndAmbiguousMods()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Protected Mod", """{"FileVersion":3,"Name":"Protected Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Ambiguous One", """{"FileVersion":3,"Name":"Shared Name","Author":"Author A"}""");
        context.Fixture.CreateMod("Ambiguous Two", """{"FileVersion":3,"Name":"Shared Name","Author":"Author B"}""");
        context.Fixture.CreateMod("Rootless Mod", """{"FileVersion":3,"Name":"Rootless Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Eligible Mod", """{"FileVersion":3,"Name":"Eligible Mod","Author":"Author"}""");
        // "Rootless Mod" has no sort_order entry; under the real format it is organizable too.
        context.Fixture.WriteModData(
            ("Protected Mod", ".Character specific mods/Akako Main Files"),
            ("Ambiguous One", "Current/A"),
            ("Ambiguous Two", "Current/B"),
            ("Eligible Mod", "Current/Eligible"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(["Protected Mod"], ("Eligible Mod", "Manual/Eligible"));

        var setup = await context.ControlledService.BuildSetupAsync(
            context.Installation,
            context.Inventory!,
            snapshot,
            new ControlledTestOptions("PenumbraOrganizer Test"),
            CancellationToken.None);

        setup.Options.MaximumSelectedModCount.Should().Be(3);
        setup.Candidates.Single(candidate => candidate.StableScanId == "Protected Mod").Status.Should().Be(ControlledTestCandidateStatus.Protected);
        setup.Candidates.Single(candidate => candidate.StableScanId == "Ambiguous One").Status.Should().Be(ControlledTestCandidateStatus.Ambiguous);
        setup.Candidates.Single(candidate => candidate.StableScanId == "Rootless Mod").CanSelect.Should().BeTrue();
        setup.Candidates.Single(candidate => candidate.StableScanId == "Eligible Mod").CanSelect.Should().BeTrue();
    }

    [Fact]
    public async Task ControlledSnapshot_SelectsOnlyChosenWrites_AndExcludesUnrelatedProposals()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.CreateMod("Beta", """{"FileVersion":3,"Name":"Beta","Author":"Author"}""");
        context.Fixture.CreateMod("Gamma", """{"FileVersion":3,"Name":"Gamma","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"), ("Beta", "Current/Beta"), ("Gamma", "Current/Gamma"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Alpha", "Manual/Alpha"), ("Beta", "Manual/Beta"), ("Gamma", "Manual/Gamma"));
        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            snapshot,
            new ControlledTestRequest("PenumbraOrganizer Test", ["Beta"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);

        controlled.Proposals.Single(proposal => proposal.StableScanId == "Beta").ProposedVirtualFolder.Should().Be("PenumbraOrganizer Test");
        controlled.Proposals.Single(proposal => proposal.StableScanId == "Alpha").ProposedVirtualFolder.Should().Be("Current/Alpha");
        controlled.Proposals.Single(proposal => proposal.StableScanId == "Gamma").ProposedVirtualFolder.Should().Be("Current/Gamma");
        plan.Summary.AffectedModCount.Should().Be(1);
        plan.Entries.Count(entry => entry.RequiresWrite).Should().Be(1);
        plan.Entries.Single(entry => entry.RequiresWrite).StableScanId.Should().Be("Beta");
    }

    [Fact]
    public async Task ControlledSnapshot_PreservesEmptyFolders()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteSortOrder([("Alpha", "Current/Alpha")], ["KeepMeEmpty"]);
        await context.ScanAsync();

        var baseSnapshot = context.BuildSnapshot(("Alpha", "Manual/Alpha"));
        baseSnapshot = baseSnapshot with
        {
            Folders = baseSnapshot.Folders.Append(new OrganizerFolder("KeepMeEmpty", ManuallyCreated: true, Protected: false)).ToArray(),
        };

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            baseSnapshot,
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));

        controlled.Folders.Should().Contain(folder => folder.Path == "KeepMeEmpty");
    }

    [Fact]
    public async Task ControlledSnapshot_PreservesOrganizationCleanupSelections()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var baseSnapshot = context.BuildSnapshot(("Alpha", "Manual/Alpha"));
        baseSnapshot = baseSnapshot with { OrganizationCleanupSelections = ["Some/Orphaned/Folder"] };

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            baseSnapshot,
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));

        controlled.OrganizationCleanupSelections.Should().BeEquivalentTo(["Some/Orphaned/Folder"]);
    }

    [Fact]
    public async Task ControlledSnapshot_RejectsInvalidTestFolder()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var snapshot = context.BuildSnapshot(("Alpha", "Manual/Alpha"));
        var act = () => Task.FromResult(context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            snapshot,
            new ControlledTestRequest(@"C:\Absolute\Path", ["Alpha"])));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*relative Penumbra folder*");
    }

    [Fact]
    public async Task ControlledPrepare_PersistsRollbackBeforeApply()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, controlled, CancellationToken.None);
        var details = await context.HistoryService.TryLoadOperationAsync(operation.OperationId, CancellationToken.None);

        details.Should().NotBeNull();
        details!.RollbackTransaction.Should().NotBeNull();
        details.Operation.ApplyStatus.Should().Be(ApplyStatus.Ready);
        details.Operation.HasRollbackTransaction.Should().BeTrue();
    }

    [Fact]
    public async Task RealValidation_ReportIncludesStructuredSafetyReadiness()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var validation = await context.RealValidationService.ValidateAsync(
            context.Installation,
            controlled,
            new RealInstallationValidationOptions(Authorized: true, CreateVerifiedBackup: false),
            CancellationToken.None);

        validation.Report.PenumbraStateDirectory.Should().Be(context.Installation.ConfigDirectory);
        validation.Report.ModLibraryRoot.Should().Be(context.Installation.ModRoot);
        validation.Report.MappedRecords.Should().Be(1);
        validation.Report.BackupReadiness.Should().Contain("Ready to prepare");
        validation.AppearsSafeForApply.Should().BeFalse();
    }

    [Fact]
    public async Task Observation_Persists_WithoutChangingWriteTarget()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, controlled, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, controlled, CancellationToken.None);

        await context.ObservationService.SaveObservationAsync(operation.OperationId, PenumbraUiObservationStatus.AppearedAfterReloadOrRestart, CancellationToken.None);
        var history = await context.HistoryService.GetOperationsAsync(CancellationToken.None);

        history.Single(entry => entry.OperationId == operation.OperationId).ObservationStatus.Should().Be(PenumbraUiObservationStatus.AppearedAfterReloadOrRestart);
        plan.FileChanges.Should().ContainSingle(change => change.TargetPath == context.Fixture.SortOrderPath);
        plan.FileChanges.Should().NotContain(change => change.TargetPath.Equals(context.Fixture.OrganizationJsonPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IncompleteBackup_IsDetected()
    {
        var hooks = new RecoveryServiceHooks
        {
            BeforePersistManifestAsync = (_, _) => throw new InvalidOperationException("Synthetic interruption before manifest finalization."),
        };
        using var context = await LiveWorkflowContext.CreateAsync(hooks);

        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);
        Func<Task> act = async () => await context.ApplyService.PrepareAsync(plan, context.Installation, controlled, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var incomplete = await context.RecoveryService.GetIncompleteOperationsAsync(CancellationToken.None);

        incomplete.Should().Contain(record => record.Stage == IncompleteOperationStage.BackupPreparation);
    }

    [Fact]
    public async Task IncompletePostApplyVerification_IsDetected_AndCanContinueSafely()
    {
        using var context = await LiveWorkflowContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, controlled, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, controlled, CancellationToken.None);
        File.Delete(context.GetVerificationPath(operation.OperationId));

        var incomplete = await context.RecoveryService.GetIncompleteOperationsAsync(CancellationToken.None);
        incomplete.Should().Contain(record => record.OperationId == operation.OperationId && record.Stage == IncompleteOperationStage.PostApplyVerification);

        var verification = await context.RecoveryService.ContinueVerificationAsync(operation.OperationId, CancellationToken.None);
        var remaining = await context.RecoveryService.GetIncompleteOperationsAsync(CancellationToken.None);

        verification.Succeeded.Should().BeTrue();
        remaining.Should().NotContain(record => record.OperationId == operation.OperationId && record.Stage == IncompleteOperationStage.PostApplyVerification);
    }

    [Fact]
    public async Task IncompleteRollback_IsDetected()
    {
        using var context = await LiveWorkflowContext.CreateAsync();

        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Author"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();

        var controlled = context.ControlledService.BuildControlledSnapshot(
            context.Installation,
            context.Inventory!,
            context.BuildSnapshot(("Alpha", "Manual/Alpha")),
            new ControlledTestRequest("PenumbraOrganizer Test", ["Alpha"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, controlled, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, controlled, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, controlled, CancellationToken.None);
        var details = await context.HistoryService.TryLoadOperationAsync(operation.OperationId, CancellationToken.None);
        var cancelledRollback = details!.RollbackTransaction! with { Status = RollbackTransactionStatus.Cancelled };
        var cancelledOperation = details.Operation with { RollbackStatus = RollbackTransactionStatus.Cancelled };
        await File.WriteAllTextAsync(context.GetRollbackPath(operation.OperationId), System.Text.Json.JsonSerializer.Serialize(cancelledRollback, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(context.GetOperationPath(operation.OperationId), System.Text.Json.JsonSerializer.Serialize(cancelledOperation, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        await context.HistoryService.RefreshOperationAsync(operation.OperationId, CancellationToken.None);

        var incomplete = await context.RecoveryService.GetIncompleteOperationsAsync(CancellationToken.None);
        incomplete.Should().Contain(record => record.OperationId == operation.OperationId && record.Stage == IncompleteOperationStage.Rollback);
    }

    private sealed class LiveWorkflowContext : IDisposable
    {
        private LiveWorkflowContext(TemporaryPenumbraFixture fixture, RecoveryServiceHooks? hooks)
        {
            Fixture = fixture;
            RootPath = fixture.RootPath;
            BackupsRoot = Path.Combine(RootPath, "LocalAppData", "PenumbraOrganizer", "Backups");

            Installation = new PenumbraInstallation(
                fixture.PenumbraJsonPath,
                fixture.PenumbraConfigPath,
                fixture.ModRoot,
                fixture.PluginAssemblyPath,
                fixture.PluginManifestPath,
                "1.6.1.10",
                DiscoveryConfidence.High,
                Array.Empty<DiscoveryEvidence>(),
                Array.Empty<string>());

            var protectionService = new ProtectionService();
            ScanService = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, protectionService);
            ProposalValidationService = new OrganizerProposalValidationService();
            ControlledService = new ControlledLiveTestService(ProposalValidationService);
            Writer = new PenumbraOrganizationWriter();
            ValidationService = new DryRunValidationService(new PlanInvalidationService(Writer));
            Planner = new DryRunPlanner(Writer, ValidationService);
            PreflightService = new WritePermissionPreflightService(BackupsRoot);
            HistoryService = new OperationHistoryService(BackupsRoot);
            var backupVerification = new BackupVerificationService(BackupsRoot, HistoryService);
            var rollbackVerification = new RollbackVerificationService(BackupsRoot, HistoryService);
            var backupService = new BackupService(BackupsRoot, backupVerification, HistoryService, hooks);
            RollbackService = new RollbackService(BackupsRoot, rollbackVerification, HistoryService, hooks);
            ApplyService = new ApplyService(ValidationService, PreflightService, backupService, RollbackService, new PostApplyVerificationService(), HistoryService, BackupsRoot);
            RealValidationService = new RealInstallationValidationService(ScanService, Planner, PreflightService, ApplyService);
            RecoveryService = new OperationRecoveryService(BackupsRoot, HistoryService, backupVerification, new PostApplyVerificationService());
            ObservationService = new OperationObservationService(BackupsRoot, HistoryService);
            Diagnostics = new DiagnosticExportService();
        }

        public TemporaryPenumbraFixture Fixture { get; }
        public string RootPath { get; }
        public string BackupsRoot { get; }
        public PenumbraInstallation Installation { get; }
        public IPenumbraScanService ScanService { get; }
        public IOrganizerProposalValidationService ProposalValidationService { get; }
        public ControlledLiveTestService ControlledService { get; }
        public IPenumbraVirtualFolderWriter Writer { get; }
        public IDryRunValidationService ValidationService { get; }
        public IDryRunPlanner Planner { get; }
        public IWritePermissionPreflightService PreflightService { get; }
        public OperationHistoryService HistoryService { get; }
        public RollbackService RollbackService { get; }
        public ApplyService ApplyService { get; }
        public IRealInstallationValidationService RealValidationService { get; }
        public IOperationRecoveryService RecoveryService { get; }
        public IOperationObservationService ObservationService { get; }
        public IDiagnosticExportService Diagnostics { get; }
        public ScanInventory? Inventory { get; private set; }

        public static Task<LiveWorkflowContext> CreateAsync(RecoveryServiceHooks? hooks = null)
        {
            var fixture = new TemporaryPenumbraFixture();
            fixture.WriteMainConfig();
            fixture.WritePluginManifest();
            return Task.FromResult(new LiveWorkflowContext(fixture, hooks));
        }

        public async Task ScanAsync()
        {
            Inventory = await ScanService.ScanAsync(Installation, null, CancellationToken.None);
        }

        public ProposalSnapshot BuildSnapshot(params (string StableScanId, string ProposedFolder)[] changes)
            => BuildSnapshot(Array.Empty<string>(), changes);

        public ProposalSnapshot BuildSnapshot(IReadOnlyList<string> protectIds, params (string StableScanId, string ProposedFolder)[] changes)
        {
            var proposals = Inventory!.Mods
                .OrderBy(mod => mod.StableScanId, StringComparer.Ordinal)
                .Select(mod =>
                {
                    var changed = changes.FirstOrDefault(change => change.StableScanId == mod.StableScanId);
                    var protectedNow = mod.Protected || protectIds.Contains(mod.StableScanId, StringComparer.Ordinal);
                    return new OrganizerModProposal
                    {
                        StableScanId = mod.StableScanId,
                        Name = mod.Name,
                        CurrentVirtualFolder = mod.CurrentVirtualFolder,
                        ProposedVirtualFolder = string.IsNullOrWhiteSpace(changed.ProposedFolder) ? mod.CurrentVirtualFolder : changed.ProposedFolder,
                        OriginalCreator = mod.Author,
                        OrganizerCreatorLabel = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author,
                        OrganizerTypeLabel = "Unknown type",
                        Protected = protectedNow,
                        OriginalProtected = mod.Protected,
                        Source = OrganizerProposalSource.Manual,
                    };
                })
                .ToArray();

            var folders = proposals
                .Select(proposal => proposal.ProposedVirtualFolder)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(folder => new OrganizerFolder(folder, true, false))
                .ToArray();
            var preferences = OrganizationPreferences.DefaultManual;
            var validation = ProposalValidationService.Validate(Inventory!, proposals, folders, preferences);
            var session = new OrganizerSessionDocument
            {
                ScanIdentity = OrganizerSessionService.BuildScanIdentity(Inventory!),
                ScanTimestampUtc = Inventory!.ScannedAtUtc,
                InstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(Installation),
                InstalledPenumbraVersion = Installation.InstalledVersion,
                OrganizationPreferences = preferences,
                ProposedFolders = folders.Select(folder => new OrganizerSessionFolder(folder.Path, folder.ManuallyCreated, folder.Protected)).ToArray(),
                Mods = proposals.Select(proposal => new OrganizerSessionMod(
                    proposal.StableScanId,
                    proposal.CurrentVirtualFolder,
                    proposal.ProposedVirtualFolder,
                    proposal.Protected,
                    proposal.OrganizerCreatorLabel,
                    proposal.OrganizerTypeLabel,
                    proposal.Source,
                    proposal.NeedsReview)).ToArray(),
            };

            return new ProposalSnapshot(
                OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences),
                OrganizerSessionService.BuildSessionIdentity(session),
                preferences,
                proposals,
                folders,
                validation);
        }

        public string GetVerificationPath(Guid operationId)
            => Path.Combine(BackupsRoot, operationId.ToString("N"), "verification.json");

        public string GetOperationPath(Guid operationId)
            => Path.Combine(BackupsRoot, operationId.ToString("N"), "operation.json");

        public string GetRollbackPath(Guid operationId)
            => Path.Combine(BackupsRoot, operationId.ToString("N"), "rollback.json");

        public void Dispose()
        {
            Fixture.Dispose();
        }
    }
}
