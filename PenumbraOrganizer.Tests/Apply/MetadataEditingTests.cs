namespace PenumbraOrganizer.Tests.Apply;

using System.Text.Json;
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

public sealed class MetadataEditingTests
{
    [Fact]
    public async Task LocalDataEdit_UpdatesFavoriteAndTags_PreservingVersionAndUnknownFields()
    {
        using var context = await Context.CreateAsync();
        context.Fixture.CreateMod("Fav Mod", """{"FileVersion":3,"Name":"Fav Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Fav Mod", "Current/Folder"));
        context.Fixture.WriteLocalModData("Fav Mod",
            """{"FileVersion":2,"ImportDate":1667578364817,"LocalTags":[],"Note":"","Favorite":false,"UnknownKey":"keep"}""");
        await context.ScanAsync();

        var snapshot = context.BuildSnapshotWithMetadata(
            [("Fav Mod", "Current/Folder")],
            new ModMetadataEdit("Fav Mod", Favorite: true, LocalTags: ["cute"], Note: "my note"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        plan.ApplyPermitted.Should().BeTrue();
        plan.FileChanges.Should().ContainSingle(change => change.WriteTargetKind == PenumbraWriteTargetKind.LocalModDataJson);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        result.Status.Should().Be(ApplyStatus.Completed);

        using var document = JsonDocument.Parse(context.Fixture.ReadLocalModData("Fav Mod"));
        document.RootElement.GetProperty("Favorite").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("LocalTags").EnumerateArray().Select(e => e.GetString()).Should().Equal("cute");
        document.RootElement.GetProperty("Note").GetString().Should().Be("my note");
        document.RootElement.GetProperty("FileVersion").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("ImportDate").GetInt64().Should().Be(1667578364817);
        document.RootElement.GetProperty("UnknownKey").GetString().Should().Be("keep");
    }

    [Fact]
    public async Task MetaJsonEdit_UpdatesAuthorAndTags_PreservingNameAndUnknownFields()
    {
        using var context = await Context.CreateAsync();
        context.Fixture.CreateMod("Meta Mod",
            """{"FileVersion":3,"Name":"Meta Mod","Author":"Old Author","ModTags":[],"CustomX":"keep"}""");
        context.Fixture.WriteModData(("Meta Mod", "Current/Folder"));
        await context.ScanAsync();

        var snapshot = context.BuildSnapshotWithMetadata(
            [("Meta Mod", "Current/Folder")],
            new ModMetadataEdit("Meta Mod", Author: "New Author", ModTags: ["dress", "yab"]));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        plan.FileChanges.Should().ContainSingle(change => change.WriteTargetKind == PenumbraWriteTargetKind.ModMetaJson);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        using var document = JsonDocument.Parse(context.Fixture.ReadMetaJson("Meta Mod"));
        document.RootElement.GetProperty("Author").GetString().Should().Be("New Author");
        document.RootElement.GetProperty("ModTags").EnumerateArray().Select(e => e.GetString()).Should().Equal("dress", "yab");
        document.RootElement.GetProperty("Name").GetString().Should().Be("Meta Mod");
        document.RootElement.GetProperty("CustomX").GetString().Should().Be("keep");
    }

    [Fact]
    public async Task CombinedFolderAndMetadataEdit_BacksUpAllFiles_AndRollsBackEverything()
    {
        using var context = await Context.CreateAsync();
        context.Fixture.CreateMod("Combo", """{"FileVersion":3,"Name":"Combo","Author":"Original","ModTags":[]}""");
        context.Fixture.WriteModData(("Combo", "Current/Folder"));
        context.Fixture.WriteLocalModData("Combo",
            """{"FileVersion":3,"ImportDate":1,"LocalTags":[],"Note":"","Favorite":false}""");
        await context.ScanAsync();

        var snapshot = context.BuildSnapshotWithMetadata(
            [("Combo", "Target/Folder")],
            new ModMetadataEdit("Combo", Author: "Edited", Favorite: true));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        // Three distinct files change: sort_order.json + meta.json + mod_data/<id>.json.
        plan.FileChanges.Should().HaveCount(3);
        plan.FileChanges.Select(c => c.WriteTargetKind).Should().BeEquivalentTo(new[]
        {
            PenumbraWriteTargetKind.SortOrderJson,
            PenumbraWriteTargetKind.ModMetaJson,
            PenumbraWriteTargetKind.LocalModDataJson,
        });

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var details = await context.HistoryService.TryLoadOperationAsync(operation.OperationId, CancellationToken.None);
        // The multi-file backup captured all three files.
        details!.Manifest!.Files.Should().HaveCount(3);
        details.Operation.VerificationStatus.Should().Be(OperationVerificationStatus.Verified);

        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        result.Status.Should().Be(ApplyStatus.Completed);

        context.Fixture.CurrentFolderOf("Combo").Should().Be("Target/Folder");
        JsonDocument.Parse(context.Fixture.ReadMetaJson("Combo")).RootElement.GetProperty("Author").GetString().Should().Be("Edited");
        JsonDocument.Parse(context.Fixture.ReadLocalModData("Combo")).RootElement.GetProperty("Favorite").GetBoolean().Should().BeTrue();

        // Rolling back restores all three files to their original contents.
        var rollback = await context.RollbackService.ExecuteAsync(operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        rollback.Status.Should().Be(RollbackTransactionStatus.Completed);

        context.Fixture.CurrentFolderOf("Combo").Should().Be("Current/Folder");
        JsonDocument.Parse(context.Fixture.ReadMetaJson("Combo")).RootElement.GetProperty("Author").GetString().Should().Be("Original");
        JsonDocument.Parse(context.Fixture.ReadLocalModData("Combo")).RootElement.GetProperty("Favorite").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task NameEdit_OnPlacedMod_RewritesSortOrderDisplayLeaf()
    {
        using var context = await Context.CreateAsync();
        context.Fixture.CreateMod("Placed", """{"FileVersion":3,"Name":"Old Name","Author":"A"}""");
        context.Fixture.WriteModData(("Placed", "Clothing"));
        await context.ScanAsync();

        // Name edit only, no folder move: the mod stays in "Clothing" but its display leaf changes.
        var snapshot = context.BuildSnapshotWithMetadata(
            Array.Empty<(string, string)>(),
            new ModMetadataEdit("Placed", Name: "New Name"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Select(c => c.WriteTargetKind).Should().Contain(PenumbraWriteTargetKind.SortOrderJson);
        plan.FileChanges.Select(c => c.WriteTargetKind).Should().Contain(PenumbraWriteTargetKind.ModMetaJson);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        // Folder unchanged, leaf rewritten to the new name; meta.json Name updated too.
        context.Fixture.CurrentFolderOf("Placed").Should().Be("Clothing");
        context.Fixture.CurrentSortPathOf("Placed").Should().Be("Clothing/New Name");
        JsonDocument.Parse(context.Fixture.ReadMetaJson("Placed")).RootElement.GetProperty("Name").GetString().Should().Be("New Name");
    }

    [Fact]
    public async Task NameEdit_OnRootMod_DoesNotCreateSortOrderEntry()
    {
        using var context = await Context.CreateAsync();
        context.Fixture.CreateMod("Rooted", """{"FileVersion":3,"Name":"Root Old","Author":"A"}""");
        // No sort_order entry: the mod sits at the root.
        context.Fixture.WriteSortOrder();
        await context.ScanAsync();

        var snapshot = context.BuildSnapshotWithMetadata(
            Array.Empty<(string, string)>(),
            new ModMetadataEdit("Rooted", Name: "Root New"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        // Only meta.json changes; no sort_order leaf is fabricated for a root mod.
        plan.FileChanges.Select(c => c.WriteTargetKind).Should().NotContain(PenumbraWriteTargetKind.SortOrderJson);
        plan.FileChanges.Select(c => c.WriteTargetKind).Should().Contain(PenumbraWriteTargetKind.ModMetaJson);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        context.Fixture.CurrentSortPathOf("Rooted").Should().BeNull();
    }

    [Fact]
    public void SnapshotIdentity_ChangesWhenMetadataEditChanges()
    {
        var proposals = new[]
        {
            new OrganizerModProposal
            {
                StableScanId = "Mod", Name = "Mod", CurrentVirtualFolder = "Folder",
                ProposedVirtualFolder = "Folder", OriginalCreator = "A",
            },
        };
        var folders = new[] { new OrganizerFolder("Folder", true, false) };
        var preferences = OrganizationPreferences.DefaultManual;

        var baseline = OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences);
        var withEdit = OrganizerSessionService.BuildProposalSnapshotIdentity(
            proposals, folders, preferences, new[] { new ModMetadataEdit("Mod", Favorite: true) });
        var withDifferentEdit = OrganizerSessionService.BuildProposalSnapshotIdentity(
            proposals, folders, preferences, new[] { new ModMetadataEdit("Mod", Note: "x") });

        withEdit.Should().NotBe(baseline);
        withDifferentEdit.Should().NotBe(withEdit);
    }

    private sealed class Context : IDisposable
    {
        private Context(TemporaryPenumbraFixture fixture)
        {
            Fixture = fixture;
            BackupsRoot = Path.Combine(fixture.RootPath, "LocalAppData", "PenumbraOrganizer", "Backups");
            Installation = new PenumbraInstallation(
                fixture.PenumbraJsonPath, fixture.PenumbraConfigPath, fixture.ModRoot,
                fixture.PluginAssemblyPath, fixture.PluginManifestPath, "1.6.1.10",
                DiscoveryConfidence.High, Array.Empty<DiscoveryEvidence>(), Array.Empty<string>());

            var protection = new ProtectionService();
            ScanService = new PenumbraScanService(NullLogger<PenumbraScanService>.Instance, protection);
            _proposalValidation = new OrganizerProposalValidationService();
            var writer = new PenumbraVirtualFolderWriter();
            ValidationService = new DryRunValidationService(new PlanInvalidationService(writer));
            Planner = new DryRunPlanner(writer, ValidationService);
            var preflight = new WritePermissionPreflightService(BackupsRoot);
            HistoryService = new OperationHistoryService(BackupsRoot);
            var backupVerification = new BackupVerificationService(BackupsRoot, HistoryService);
            var rollbackVerification = new RollbackVerificationService(BackupsRoot, HistoryService);
            var backup = new BackupService(BackupsRoot, backupVerification, HistoryService);
            RollbackService = new RollbackService(BackupsRoot, rollbackVerification, HistoryService);
            ApplyService = new ApplyService(ValidationService, preflight, backup, RollbackService, new PostApplyVerificationService(), HistoryService, BackupsRoot);
        }

        private readonly OrganizerProposalValidationService _proposalValidation;

        public TemporaryPenumbraFixture Fixture { get; }
        public string BackupsRoot { get; }
        public PenumbraInstallation Installation { get; }
        public IPenumbraScanService ScanService { get; }
        public IDryRunValidationService ValidationService { get; }
        public IDryRunPlanner Planner { get; }
        public OperationHistoryService HistoryService { get; }
        public RollbackService RollbackService { get; }
        public ApplyService ApplyService { get; }
        public ScanInventory? Inventory { get; private set; }

        public static Task<Context> CreateAsync()
        {
            var fixture = new TemporaryPenumbraFixture();
            fixture.WriteMainConfig();
            fixture.WritePluginManifest();
            return Task.FromResult(new Context(fixture));
        }

        public async Task ScanAsync() => Inventory = await ScanService.ScanAsync(Installation, null, CancellationToken.None);

        public ProposalSnapshot BuildSnapshotWithMetadata(
            (string StableScanId, string ProposedFolder)[] changes,
            params ModMetadataEdit[] edits)
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
            var validation = _proposalValidation.Validate(Inventory!, proposals, folders, preferences);
            var session = new OrganizerSessionDocument
            {
                ScanIdentity = OrganizerSessionService.BuildScanIdentity(Inventory!),
                ScanTimestampUtc = Inventory!.ScannedAtUtc,
                InstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(Installation),
                InstalledPenumbraVersion = Installation.InstalledVersion,
                OrganizationPreferences = preferences,
                ProposedFolders = folders.Select(folder => new OrganizerSessionFolder(folder.Path, folder.ManuallyCreated, folder.Protected)).ToArray(),
                Mods = proposals.Select(proposal => new OrganizerSessionMod(
                    proposal.StableScanId, proposal.CurrentVirtualFolder, proposal.ProposedVirtualFolder,
                    proposal.Protected, proposal.OrganizerCreatorLabel, proposal.OrganizerTypeLabel,
                    proposal.Source, proposal.NeedsReview)).ToArray(),
            };

            return new ProposalSnapshot(
                OrganizerSessionService.BuildProposalSnapshotIdentity(proposals, folders, preferences),
                OrganizerSessionService.BuildSessionIdentity(session),
                preferences, proposals, folders, validation, edits);
        }

        public void Dispose() => Fixture.Dispose();
    }
}
