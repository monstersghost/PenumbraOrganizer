namespace PenumbraOrganizer.Tests.Apply;

using LiteDB;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Recovery;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Infrastructure.Sessions;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class DryRunAndApplyTests
{
    [Fact]
    public async Task Mapping_UsesModDataAsAuthoritativeTarget_AndDisambiguatesDuplicateNames()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Alpha One", """{"FileVersion":3,"Name":"Duplicate Name","Author":"Author A"}""");
        context.Fixture.CreateMod("Alpha Two", """{"FileVersion":3,"Name":"Duplicate Name","Author":"Author B"}""");
        context.Fixture.WriteModData(("Alpha One", "Original/One"), ("Alpha Two", "Original/Two"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Alpha One", "Moves/One"), ("Alpha Two", "Moves/Two"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.Entries.Should().HaveCount(2);
        plan.Entries.Should().Contain(entry =>
            entry.StableScanId == "Alpha One" &&
            entry.AuthoritativeStateEntryIdentity == "LocalModData:Alpha One:Folder" &&
            entry.TargetPath == context.Fixture.ModDataDbPath &&
            entry.RecordKey == "Alpha One");
        plan.Entries.Should().Contain(entry =>
            entry.StableScanId == "Alpha Two" &&
            entry.AuthoritativeStateEntryIdentity == "LocalModData:Alpha Two:Folder" &&
            entry.RecordKey == "Alpha Two");
        plan.FileChanges.Should().ContainSingle(change => change.TargetPath == context.Fixture.ModDataDbPath);
    }

    [Fact]
    public async Task MissingStateEntry_BlocksPlanning()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Missing Mod", """{"FileVersion":3,"Name":"Missing Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/Mapped"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Mapped Mod", "Target/Mapped"), ("Missing Mod", "Target/Missing"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.ApplyPermitted.Should().BeFalse();
        plan.Entries.Single(entry => entry.StableScanId == "Missing Mod").ValidationStatus.Should().Be(OrganizerRowStatus.MissingMod);
        plan.Entries.Single(entry => entry.StableScanId == "Missing Mod").RequiresWrite.Should().BeFalse();
    }

    [Fact]
    public async Task ProtectedRow_DoesNotCreateWritableOperation()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Protected Mod", """{"FileVersion":3,"Name":"Protected Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Protected Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(["Protected Mod"], ("Protected Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.ApplyPermitted.Should().BeFalse();
        plan.Entries.Single().Protected.Should().BeTrue();
        plan.Entries.Single().RequiresWrite.Should().BeFalse();
    }

    [Fact]
    public async Task UnsupportedSchema_BlocksPlanning()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Broken Schema", """{"FileVersion":3,"Name":"Broken Schema","Author":"Author"}""");
        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Broken Schema",
            ["Folder"] = 42,
        });

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Broken Schema", "Target/Folder"));

        var act = () => context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing a string Folder field*");
    }

    [Fact]
    public async Task DryRun_IsDeterministic_AndPreservesUnknownFields()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Preserve Mod", """{"FileVersion":3,"Name":"Preserve Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Other Mod", """{"FileVersion":3,"Name":"Other Mod","Author":"Author"}""");
        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Preserve Mod",
            ["Folder"] = "Current/Preserve",
            ["CustomField"] = "keep-me",
        });
        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Other Mod",
            ["Folder"] = "Current/Other",
            ["CustomField"] = "other-value",
        });

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Preserve Mod", "Target/Preserve"), ("Other Mod", "Current/Other"));
        var first = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var second = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        first.FileChanges.Single().ExpectedSha256.Should().Be(second.FileChanges.Single().ExpectedSha256);

        var tempPath = Path.Combine(context.RootPath, "expected.db");
        await File.WriteAllBytesAsync(tempPath, Convert.FromBase64String(first.FileChanges.Single().ExpectedBytesBase64));
        using var db = new LiteDatabase($"Filename={tempPath};Connection=Direct");
        var collection = db.GetCollection("LocalModData");
        collection.FindById("Preserve Mod")!["CustomField"].AsString.Should().Be("keep-me");
        collection.FindById("Other Mod")!["CustomField"].AsString.Should().Be("other-value");
        collection.FindById("Preserve Mod")!["Folder"].AsString.Should().Be("Target/Preserve");
        collection.FindById("Other Mod")!["Folder"].AsString.Should().Be("Current/Other");
    }

    [Fact]
    public async Task PlanInvalidates_WhenSourceHashChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Plan Mod", """{"FileVersion":3,"Name":"Plan Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Plan Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Plan Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Plan Mod",
            ["Folder"] = "Changed/OutsidePlan",
        });

        var validation = await context.ValidationService.ValidateAsync(plan, context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        validation.Status.Should().Be(DryRunPlanValidationStatus.Stale);
        validation.InvalidationReasons.Should().Contain(PlanInvalidationReason.SourceFileHashChanged);
    }

    [Fact]
    public async Task PlanInvalidates_WhenProposalChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Plan Mod", """{"FileVersion":3,"Name":"Plan Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Plan Mod", "Current/Folder"));

        await context.ScanAsync();
        var original = context.BuildSnapshot(("Plan Mod", "Target/Folder"));
        var changed = context.BuildSnapshot(("Plan Mod", "Another/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, original, CancellationToken.None);

        var validation = await context.ValidationService.ValidateAsync(plan, context.Installation, context.Inventory!, changed, CancellationToken.None);

        validation.Status.Should().Be(DryRunPlanValidationStatus.Stale);
        validation.InvalidationReasons.Should().Contain(PlanInvalidationReason.ProposalChanged);
    }

    [Fact]
    public async Task PlanInvalidates_WhenProtectionChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Plan Mod", """{"FileVersion":3,"Name":"Plan Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Plan Mod", "Current/Folder"));

        await context.ScanAsync();
        var original = context.BuildSnapshot(("Plan Mod", "Target/Folder"));
        var protectedSnapshot = context.BuildSnapshot(["Plan Mod"], ("Plan Mod", "Current/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, original, CancellationToken.None);

        var validation = await context.ValidationService.ValidateAsync(plan, context.Installation, context.Inventory!, protectedSnapshot, CancellationToken.None);

        validation.InvalidationReasons.Should().Contain(PlanInvalidationReason.ProtectionChanged);
    }

    [Fact]
    public async Task PlanInvalidates_WhenVersionChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Version Mod", """{"FileVersion":3,"Name":"Version Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Version Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Version Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        context.Fixture.WritePluginManifest("1.6.1.11");

        var validation = await context.ValidationService.ValidateAsync(plan, context.Installation with { InstalledVersion = "1.6.1.11" }, context.Inventory!, snapshot, CancellationToken.None);

        validation.InvalidationReasons.Should().Contain(PlanInvalidationReason.PenumbraVersionChanged);
    }

    [Fact]
    public async Task DuplicateStateOperations_AreRejected()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Duplicate", """{"FileVersion":3,"Name":"Duplicate","Author":"Author"}""");
        context.Fixture.WriteModData(("Duplicate", "Current/Folder"));

        await context.ScanAsync();
        var entries = new[]
        {
            new DryRunPlanEntry("Duplicate", "Duplicate", "Current/Folder", "Target/One", OrganizerProposalSource.Manual, false, OrganizerRowStatus.ValidChange, "LocalModData:Duplicate:Folder", context.Fixture.ModDataDbPath, "Duplicate", "A", "B", Array.Empty<string>(), true),
            new DryRunPlanEntry("Duplicate", "Duplicate", "Current/Folder", "Target/Two", OrganizerProposalSource.Manual, false, OrganizerRowStatus.ValidChange, "LocalModData:Duplicate:Folder", context.Fixture.ModDataDbPath, "Duplicate", "A", "C", Array.Empty<string>(), true),
        };

        var act = () => context.Writer.BuildExpectedFileChangesAsync(context.Installation, entries, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate authoritative state operations*");
    }

    [Fact]
    public async Task Preflight_PassesReadableWritableTarget_AndCleansUpTempProbe()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Writable Mod", """{"FileVersion":3,"Name":"Writable Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Writable Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Writable Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var result = await context.PreflightService.CheckAsync(plan, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.EnumerateFiles(context.Fixture.PenumbraConfigPath, ".penumbraorganizer-*.tmp").Should().BeEmpty();
        Directory.EnumerateFiles(context.BackupsRoot, ".penumbraorganizer-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Preflight_ReadOnlyTarget_Blocks()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("ReadOnly Mod", """{"FileVersion":3,"Name":"ReadOnly Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("ReadOnly Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("ReadOnly Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        File.SetAttributes(context.Fixture.ModDataDbPath, File.GetAttributes(context.Fixture.ModDataDbPath) | FileAttributes.ReadOnly);
        try
        {
            var result = await context.PreflightService.CheckAsync(plan, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(context.Fixture.ModDataDbPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Prepare_CreatesVerifiedBackup_AndRollbackTransaction()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Backup Mod", """{"FileVersion":3,"Name":"Backup Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Backup Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Backup Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var details = await context.HistoryService.TryLoadOperationAsync(operation.OperationId, CancellationToken.None);

        details.Should().NotBeNull();
        details!.Operation.VerificationStatus.Should().Be(OperationVerificationStatus.Verified);
        details.Operation.ApplyStatus.Should().Be(ApplyStatus.Ready);
        details.RollbackTransaction.Should().NotBeNull();
        details.RollbackTransaction!.Files.Single().ExpectedAppliedSha256.Should().Be(plan.FileChanges.Single().ExpectedSha256);
        details.Manifest!.Files.Single().SourceTargetPath.Should().Be(context.Fixture.ModDataDbPath);
        details.Operation.OperationFolder.Should().StartWith(context.BackupsRoot);
    }

    [Fact]
    public async Task Apply_Succeeds_PreservesUnrelatedData_AndEnablesRollback()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Apply Mod", """{"FileVersion":3,"Name":"Apply Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Other Mod", """{"FileVersion":3,"Name":"Other Mod","Author":"Author"}""");
        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Apply Mod",
            ["Folder"] = "Current/Apply",
            ["Extra"] = "preserve",
        });
        context.Fixture.WriteModDataDocument(new BsonDocument
        {
            ["_id"] = "Other Mod",
            ["Folder"] = "Current/Other",
            ["Extra"] = "other",
        });

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Apply Mod", "Target/Apply"), ("Other Mod", "Current/Other"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);

        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        var history = await context.HistoryService.GetOperationsAsync(CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Completed);
        result.RollbackAvailable.Should().BeTrue();
        result.Files.Should().ContainSingle(file => file.Status == ApplyResultStatus.Applied);
        history.Should().ContainSingle(entry => entry.OperationId == operation.OperationId && entry.ApplyStatus == ApplyStatus.Completed && entry.RollbackAvailable);

        using var db = new LiteDatabase($"Filename={context.Fixture.ModDataDbPath};Connection=Direct");
        var collection = db.GetCollection("LocalModData");
        collection.FindById("Apply Mod")!["Folder"].AsString.Should().Be("Target/Apply");
        collection.FindById("Apply Mod")!["Extra"].AsString.Should().Be("preserve");
        collection.FindById("Other Mod")!["Folder"].AsString.Should().Be("Current/Other");
        collection.FindById("Other Mod")!["Extra"].AsString.Should().Be("other");
    }

    [Fact]
    public async Task Apply_BlocksWhenSourceHashChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Hash Mod", """{"FileVersion":3,"Name":"Hash Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Hash Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Hash Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        context.Fixture.WriteModData(("Hash Mod", "Changed/OutsideApply"));

        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Failed);
        result.Files.Should().ContainSingle(file => file.Status == ApplyResultStatus.Failed && file.Message.Contains("source hash", StringComparison.OrdinalIgnoreCase));
        using var db = new LiteDatabase($"Filename={context.Fixture.ModDataDbPath};Connection=Direct");
        db.GetCollection("LocalModData").FindById("Hash Mod")!["Folder"].AsString.Should().Be("Changed/OutsideApply");
    }

    [Fact]
    public async Task SuccessfulApply_CanBeRolledBackExactly()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Rollback Mod", """{"FileVersion":3,"Name":"Rollback Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Rollback Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Rollback Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        var rollback = await context.RollbackService.ExecuteAsync(operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        rollback.Status.Should().Be(RollbackTransactionStatus.Completed);
        using var db = new LiteDatabase($"Filename={context.Fixture.ModDataDbPath};Connection=Direct");
        db.GetCollection("LocalModData").FindById("Rollback Mod")!["Folder"].AsString.Should().Be("Current/Folder");
    }

    [Fact]
    public async Task ExternalModificationAfterApply_CreatesRollbackConflict()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Conflict Mod", """{"FileVersion":3,"Name":"Conflict Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Conflict Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Conflict Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        context.Fixture.WriteModData(("Conflict Mod", "External/Change"));

        var rollback = await context.RollbackService.ExecuteAsync(operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);

        rollback.Status.Should().Be(RollbackTransactionStatus.CompletedWithConflicts);
        using var db = new LiteDatabase($"Filename={context.Fixture.ModDataDbPath};Connection=Direct");
        db.GetCollection("LocalModData").FindById("Conflict Mod")!["Folder"].AsString.Should().Be("External/Change");
    }

    private sealed class ApplyTestContext : IDisposable
    {
        private ApplyTestContext(TemporaryPenumbraFixture fixture)
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
            Writer = new PenumbraVirtualFolderWriter();
            var invalidation = new PlanInvalidationService(Writer);
            ValidationService = new DryRunValidationService(invalidation);
            Planner = new DryRunPlanner(Writer, ValidationService);
            PreflightService = new WritePermissionPreflightService(BackupsRoot);
            HistoryService = new OperationHistoryService(BackupsRoot);
            var backupVerification = new BackupVerificationService(BackupsRoot, HistoryService);
            var rollbackVerification = new RollbackVerificationService(BackupsRoot, HistoryService);
            var backupService = new BackupService(BackupsRoot, backupVerification, HistoryService);
            RollbackService = new RollbackService(BackupsRoot, rollbackVerification, HistoryService);
            ApplyService = new ApplyService(ValidationService, PreflightService, backupService, RollbackService, new PostApplyVerificationService(), HistoryService, BackupsRoot);
        }

        public TemporaryPenumbraFixture Fixture { get; }
        public string RootPath { get; }
        public string BackupsRoot { get; }
        public PenumbraInstallation Installation { get; }
        public IPenumbraScanService ScanService { get; }
        public IOrganizerProposalValidationService ProposalValidationService { get; }
        public PenumbraVirtualFolderWriter Writer { get; }
        public IDryRunValidationService ValidationService { get; }
        public IDryRunPlanner Planner { get; }
        public IWritePermissionPreflightService PreflightService { get; }
        public OperationHistoryService HistoryService { get; }
        public RollbackService RollbackService { get; }
        public ApplyService ApplyService { get; }
        public ScanInventory? Inventory { get; private set; }

        public static Task<ApplyTestContext> CreateAsync()
        {
            var fixture = new TemporaryPenumbraFixture();
            fixture.WriteMainConfig();
            fixture.WritePluginManifest();
            return Task.FromResult(new ApplyTestContext(fixture));
        }

        public async Task ScanAsync()
        {
            Inventory = await ScanService.ScanAsync(Installation, null, CancellationToken.None);
        }

        public ProposalSnapshot BuildSnapshot(params (string StableScanId, string ProposedFolder)[] changes)
            => BuildSnapshot(changes, Array.Empty<string>());

        public ProposalSnapshot BuildSnapshot(IReadOnlyList<string> protectIds, params (string StableScanId, string ProposedFolder)[] changes)
            => BuildSnapshot(changes, protectIds);

        public ProposalSnapshot BuildSnapshot((string StableScanId, string ProposedFolder)[] changes, IReadOnlyList<string> protectIds)
        {
            var proposals = Inventory!.Mods
                .OrderBy(mod => mod.StableScanId, StringComparer.Ordinal)
                .Select(mod =>
                {
                    var changed = changes.FirstOrDefault(change => change.StableScanId == mod.StableScanId);
                    return new OrganizerModProposal
                    {
                        StableScanId = mod.StableScanId,
                        Name = mod.Name,
                        CurrentVirtualFolder = mod.CurrentVirtualFolder,
                        ProposedVirtualFolder = string.IsNullOrWhiteSpace(changed.ProposedFolder) ? mod.CurrentVirtualFolder : changed.ProposedFolder,
                        OriginalCreator = mod.Author,
                        OrganizerCreatorLabel = string.IsNullOrWhiteSpace(mod.Author) ? "Unknown creator" : mod.Author,
                        OrganizerTypeLabel = "Unknown type",
                        Protected = protectIds.Contains(mod.StableScanId, StringComparer.Ordinal),
                        OriginalProtected = mod.Protected,
                        Source = OrganizerProposalSource.Manual,
                    };
                })
                .ToArray();

            var folders = proposals
                .Select(proposal => proposal.ProposedVirtualFolder)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(folder => new OrganizerFolder(folder, true, protectIds.Any(id => proposals.Any(proposal => proposal.StableScanId == id && proposal.ProposedVirtualFolder.Equals(folder, StringComparison.Ordinal)))))
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

        public void Dispose()
        {
            Fixture.Dispose();
        }
    }
}
