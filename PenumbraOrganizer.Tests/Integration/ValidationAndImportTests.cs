namespace PenumbraOrganizer.Tests.Integration;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Diagnostics;
using PenumbraOrganizer.Infrastructure.Exports;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Infrastructure.Recovery;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Infrastructure.Sessions;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class ValidationAndImportTests
{
    [Fact]
    public async Task OrganizationJson_NoConfirmedSelections_IsNeverAWriteTarget()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned/Empty":{}},"Separators":{}}""");

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        context.Inventory!.Mods.Single().CurrentVirtualFolder.Should().Be("Current/FromDb");
        plan.FileChanges.Should().ContainSingle(change => change.TargetPath == context.Fixture.SortOrderPath);
        plan.FileChanges.Should().NotContain(change => change.TargetPath.Equals(context.Fixture.OrganizationJsonPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrganizationJson_ConfirmedSelection_PrunesOnlyThatFolder_LeavesEverythingElseUntouched()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""
        {
          "Version": 1,
          "Folders": {
            "Orphaned/Empty": {},
            "Kept/Customized": { "ExpandedColor": 123 }
          },
          "Separators": {}
        }
        """);

        await context.ScanAsync();
        var baseSnapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var snapshot = baseSnapshot with { OrganizationCleanupSelections = ["Orphaned/Empty"] };
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var organizationChange = plan.FileChanges.Should()
            .ContainSingle(change => change.TargetPath.Equals(context.Fixture.OrganizationJsonPath, StringComparison.OrdinalIgnoreCase))
            .Subject;
        var updatedJson = Encoding.UTF8.GetString(Convert.FromBase64String(organizationChange.ExpectedBytesBase64));
        var updated = PenumbraOrganizationJson.Parse(updatedJson).Data!;
        updated.Folders.Should().NotContainKey("Orphaned/Empty");
        updated.Folders.Should().ContainKey("Kept/Customized");
        // Mod placement is unaffected by organization.json cleanup -- confirms the two write
        // targets are genuinely independent.
        plan.FileChanges.Should().ContainSingle(change => change.TargetPath == context.Fixture.SortOrderPath);
    }

    [Fact]
    public async Task RealValidation_RequiresExplicitAuthorization()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Read Only", """{"FileVersion":3,"Name":"Read Only","Author":"Author"}""");
        context.Fixture.WriteModData(("Read Only", "Current/Folder"));

        var act = () => context.RealValidationService.ValidateAsync(
            context.Installation,
            proposalSnapshot: null,
            new RealInstallationValidationOptions(Authorized: false, CreateVerifiedBackup: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit user authorization*");
    }

    [Fact]
    public async Task RealValidation_ReadOnlyMode_MakesNoWrites_ToPenumbraState()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Validation Mod", """{"FileVersion":3,"Name":"Validation Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Validation Mod", "Current/Folder"));
        await context.ScanAsync();

        var beforeHash = HashFile(context.Fixture.SortOrderPath);
        var snapshot = context.BuildSnapshot(("Validation Mod", "Target/Folder"));
        var result = await context.RealValidationService.ValidateAsync(
            context.Installation,
            snapshot,
            new RealInstallationValidationOptions(Authorized: true, CreateVerifiedBackup: false),
            CancellationToken.None);

        result.Plan.FileChanges.Should().ContainSingle();
        HashFile(context.Fixture.SortOrderPath).Should().Be(beforeHash);
        Directory.Exists(context.BackupsRoot).Should().BeTrue();
        Directory.EnumerateDirectories(context.BackupsRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task RealValidation_CanOptionallyCreateVerifiedBackup()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Backup Mod", """{"FileVersion":3,"Name":"Backup Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Backup Mod", "Current/Folder"));
        await context.ScanAsync();

        var snapshot = context.BuildSnapshot(("Backup Mod", "Target/Folder"));
        var result = await context.RealValidationService.ValidateAsync(
            context.Installation,
            snapshot,
            new RealInstallationValidationOptions(Authorized: true, CreateVerifiedBackup: true),
            CancellationToken.None);

        result.BackupCreated.Should().BeTrue();
        result.BackupOperationId.Should().NotBeNull();
        var details = await context.HistoryService.TryLoadOperationAsync(result.BackupOperationId!.Value, CancellationToken.None);
        details.Should().NotBeNull();
        details!.Operation.VerificationStatus.Should().Be(OperationVerificationStatus.Verified);
    }

    [Fact]
    public async Task Preflight_BlocksRunningProcesses_AndLowDiskSpace()
    {
        using var context = await ValidationContext.CreateAsync(processes: ["ffxiv_dx11"], freeSpaceBytes: 1);
        context.Fixture.CreateMod("Blocked Mod", """{"FileVersion":3,"Name":"Blocked Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Blocked Mod", "Current/Folder"));
        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Blocked Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var preflight = await context.PreflightService.CheckAsync(plan, CancellationToken.None);

        preflight.Succeeded.Should().BeFalse();
        preflight.BlockingProcesses.Should().Contain("ffxiv_dx11");
        preflight.Errors.Should().Contain(error => error.Contains("Not enough free disk space", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticExport_RedactsAbsolutePaths_AndOmitsStateDatabase()
    {
        using var context = await ValidationContext.CreateAsync();
        context.Fixture.CreateMod("Alpha", """{"FileVersion":3,"Name":"Alpha","Author":"Creator"}""");
        context.Fixture.WriteModData(("Alpha", "Current/Alpha"));
        await context.ScanAsync();
        var summary = await context.Diagnostics.CreateAsync(
            new DiagnosticExportRequest(
                "test-version",
                context.Installation,
                context.Inventory,
                ReviewValidation: null,
                DryRunPlan: null,
                ApplyOperation: null,
                ApplyResult: null,
                RealInstallationValidation: null,
                Operations: Array.Empty<OperationHistoryEntry>(),
                ActivityLog: $"Log path {context.Fixture.SortOrderPath} under {context.Fixture.ModRoot}"),
            CancellationToken.None);

        File.Exists(summary.ZipPath).Should().BeTrue();
        var combined = string.Join(
            "\n",
            System.IO.Compression.ZipFile.OpenRead(summary.ZipPath).Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }));

        combined.Should().NotContain(context.Fixture.SortOrderPath.Replace('\\', '/'));
        combined.Should().NotContain(context.Fixture.ModRoot.Replace('\\', '/'));
        combined.Should().Contain("[penumbra-state]");
        combined.Should().Contain("[mod-library]");
        combined.Should().NotContain("sort_order.json");
    }

    [Fact]
    public void AppManifest_RemainsAsInvoker_WithoutElevation()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PenumbraOrganizer.App", "app.manifest"));
        var manifest = File.ReadAllText(manifestPath);

        manifest.Should().Contain("asInvoker");
        manifest.Should().NotContain("requireAdministrator");
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class ValidationContext : IDisposable
    {
        private ValidationContext(TemporaryPenumbraFixture fixture, IReadOnlyList<string> processes, long? freeSpaceBytes)
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
            Writer = new PenumbraOrganizationWriter();
            var organizationCleanupWriter = new OrganizationCleanupWriter();
            ValidationService = new DryRunValidationService(new PlanInvalidationService(Writer, organizationCleanupWriter));
            Planner = new DryRunPlanner(Writer, ValidationService, organizationCleanupWriter);
            PreflightService = new WritePermissionPreflightService(
                BackupsRoot,
                () => processes,
                _ => freeSpaceBytes ?? long.MaxValue);
            HistoryService = new OperationHistoryService(BackupsRoot);
            var backupVerification = new BackupVerificationService(BackupsRoot, HistoryService);
            var rollbackVerification = new RollbackVerificationService(BackupsRoot, HistoryService);
            var backupService = new BackupService(BackupsRoot, backupVerification, HistoryService);
            var rollbackService = new RollbackService(BackupsRoot, rollbackVerification, HistoryService);
            var applyService = new ApplyService(ValidationService, PreflightService, backupService, rollbackService, new PostApplyVerificationService(), HistoryService, BackupsRoot);
            RealValidationService = new RealInstallationValidationService(ScanService, Planner, PreflightService, applyService);
            Diagnostics = new DiagnosticExportService();
        }

        public TemporaryPenumbraFixture Fixture { get; }
        public string RootPath { get; }
        public string BackupsRoot { get; }
        public PenumbraInstallation Installation { get; }
        public IPenumbraScanService ScanService { get; }
        public IOrganizerProposalValidationService ProposalValidationService { get; }
        public IPenumbraVirtualFolderWriter Writer { get; }
        public IDryRunValidationService ValidationService { get; }
        public IDryRunPlanner Planner { get; }
        public IWritePermissionPreflightService PreflightService { get; }
        public OperationHistoryService HistoryService { get; }
        public IRealInstallationValidationService RealValidationService { get; }
        public IDiagnosticExportService Diagnostics { get; }
        public ScanInventory? Inventory { get; private set; }

        public static Task<ValidationContext> CreateAsync(IReadOnlyList<string>? processes = null, long? freeSpaceBytes = null)
        {
            var fixture = new TemporaryPenumbraFixture();
            fixture.WriteMainConfig();
            fixture.WritePluginManifest();
            return Task.FromResult(new ValidationContext(fixture, processes ?? Array.Empty<string>(), freeSpaceBytes));
        }

        public async Task ScanAsync()
        {
            Inventory = await ScanService.ScanAsync(Installation, null, CancellationToken.None);
        }

        public ProposalSnapshot BuildSnapshot(params (string StableScanId, string ProposedFolder)[] changes)
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
                        Protected = mod.Protected,
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

        public void Dispose()
        {
            Fixture.Dispose();
        }
    }
}
