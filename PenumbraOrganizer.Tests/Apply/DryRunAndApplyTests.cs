namespace PenumbraOrganizer.Tests.Apply;

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;
using PenumbraOrganizer.Infrastructure.Apply;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Infrastructure.Recovery;
using PenumbraOrganizer.Infrastructure.Scanning;
using PenumbraOrganizer.Infrastructure.Sessions;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class DryRunAndApplyTests
{
    [Fact]
    public async Task Mapping_UsesSortOrderAsAuthoritativeTarget_AndDisambiguatesDuplicateNames()
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
            entry.AuthoritativeStateEntryIdentity == "sort_order.json:Alpha One" &&
            entry.TargetPath == context.Fixture.SortOrderPath &&
            entry.RecordKey == "Alpha One" &&
            entry.ProposedSortPath == "Moves/One/Alpha One");
        plan.Entries.Should().Contain(entry =>
            entry.StableScanId == "Alpha Two" &&
            entry.AuthoritativeStateEntryIdentity == "sort_order.json:Alpha Two" &&
            entry.RecordKey == "Alpha Two");
        plan.FileChanges.Should().ContainSingle(change =>
            change.TargetPath == context.Fixture.SortOrderPath &&
            change.WriteTargetKind == PenumbraWriteTargetKind.SortOrderJson);
    }

    [Fact]
    public async Task ModWithoutSortEntry_MapsAsRootMod_AndCanMove()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Placed Mod", """{"FileVersion":3,"Name":"Placed Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Root Mod", """{"FileVersion":3,"Name":"Root Mod","Author":"Author"}""");
        // Only "Placed Mod" has an explicit entry; "Root Mod" lives at the root with no entry.
        context.Fixture.WriteModData(("Placed Mod", "Current/Mapped"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Placed Mod", "Target/Mapped"), ("Root Mod", "Target/Root"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.ApplyPermitted.Should().BeTrue();
        var rootEntry = plan.Entries.Single(entry => entry.StableScanId == "Root Mod");
        rootEntry.CurrentVirtualFolder.Should().BeEmpty();
        rootEntry.RequiresWrite.Should().BeTrue();
        rootEntry.ProposedSortPath.Should().Be("Target/Root/Root Mod");
    }

    [Fact]
    public async Task Plan_WithNoProposedChanges_IsValidWithoutBenignRowNotesAsWarnings()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Stay Mod", """{"FileVersion":3,"Name":"Stay Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Stay Mod", "Current/Folder"));
        await context.ScanAsync();

        // No changes proposed: every mod keeps its current folder, matching a user who has not
        // chosen an organization strategy or made any manual assignment yet.
        var snapshot = context.BuildSnapshot();
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Should().BeEmpty();
        plan.ApplyPermitted.Should().BeFalse();
        plan.Validation.Status.Should().Be(DryRunPlanValidationStatus.Valid);
        plan.Validation.Errors.Should().BeEmpty();
        plan.Warnings.Should().NotContain(w => w.Contains("No folder change", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmptyFolder_IsPersisted_EvenWithoutModMoves()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Stay Mod", """{"FileVersion":3,"Name":"Stay Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Stay Mod", "Current/Folder"));
        await context.ScanAsync();

        // The mod does not move, but the user created a new empty folder.
        var snapshot = context.BuildSnapshotWithEmptyFolders(
            [("Stay Mod", "Current/Folder")],
            ["Brand New Empty"]);
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Should().ContainSingle();
        var expectedJson = Encoding.UTF8.GetString(Convert.FromBase64String(plan.FileChanges.Single().ExpectedBytesBase64));
        using var document = JsonDocument.Parse(expectedJson);
        document.RootElement.GetProperty("EmptyFolders").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("Brand New Empty");
    }

    [Fact]
    public async Task EmptyFolder_IsDropped_WhenAModMovesIntoIt()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mover", """{"FileVersion":3,"Name":"Mover","Author":"Author"}""");
        // "Reserved" starts as an explicitly empty folder.
        context.Fixture.WriteSortOrder([("Mover", "Current/Mover")], ["Reserved"]);
        await context.ScanAsync();

        // The mod moves into the previously-empty folder, which must no longer be listed as empty.
        var snapshot = context.BuildSnapshotWithEmptyFolders([("Mover", "Reserved")], ["Reserved"]);
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var expectedJson = Encoding.UTF8.GetString(Convert.FromBase64String(plan.FileChanges.Single().ExpectedBytesBase64));
        using var document = JsonDocument.Parse(expectedJson);
        document.RootElement.GetProperty("EmptyFolders").EnumerateArray()
            .Select(element => element.GetString())
            .Should().NotContain("Reserved");
        document.RootElement.GetProperty("Data").GetProperty("Mover").GetString().Should().Be("Reserved/Mover");
    }

    [Fact]
    public async Task EmptyFolder_Deletion_Persists()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Stay Mod", """{"FileVersion":3,"Name":"Stay Mod","Author":"Author"}""");
        context.Fixture.WriteSortOrder([("Stay Mod", "Current/Folder")], ["ToDelete", "ToKeep"]);
        await context.ScanAsync();

        // The proposed folder set keeps "ToKeep" but omits "ToDelete" (the user deleted it).
        var snapshot = context.BuildSnapshotWithEmptyFolders([("Stay Mod", "Current/Folder")], ["ToKeep"]);
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var expectedJson = Encoding.UTF8.GetString(Convert.FromBase64String(plan.FileChanges.Single().ExpectedBytesBase64));
        using var document = JsonDocument.Parse(expectedJson);
        var empty = document.RootElement.GetProperty("EmptyFolders").EnumerateArray().Select(e => e.GetString()).ToArray();
        empty.Should().Contain("ToKeep");
        empty.Should().NotContain("ToDelete");
    }

    [Fact]
    public async Task EmptyFolder_Rename_Persists()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Stay Mod", """{"FileVersion":3,"Name":"Stay Mod","Author":"Author"}""");
        context.Fixture.WriteSortOrder([("Stay Mod", "Current/Folder")], ["OldName"]);
        await context.ScanAsync();

        var snapshot = context.BuildSnapshotWithEmptyFolders([("Stay Mod", "Current/Folder")], ["NewName"]);
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var expectedJson = Encoding.UTF8.GetString(Convert.FromBase64String(plan.FileChanges.Single().ExpectedBytesBase64));
        using var document = JsonDocument.Parse(expectedJson);
        var empty = document.RootElement.GetProperty("EmptyFolders").EnumerateArray().Select(e => e.GetString()).ToArray();
        empty.Should().Contain("NewName");
        empty.Should().NotContain("OldName");
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
        context.Fixture.WriteSortOrderRaw("""{"Data":{"Broken Schema":42},"EmptyFolders":[]}""");

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Broken Schema", "Target/Folder"));

        var act = () => context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*string folder path*");
    }

    [Fact]
    public async Task DryRun_IsDeterministic_AndPreservesUnknownFields()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Preserve Mod", """{"FileVersion":3,"Name":"Preserve Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Other Mod", """{"FileVersion":3,"Name":"Other Mod","Author":"Author"}""");
        // An unknown top-level property (e.g. a future Penumbra key) must survive a rewrite.
        context.Fixture.WriteSortOrderRaw("""
        {
          "Data": { "Preserve Mod": "Current/Preserve/Preserve Mod", "Other Mod": "Current/Other/Other Mod" },
          "EmptyFolders": [],
          "FutureKey": "keep-me"
        }
        """);

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Preserve Mod", "Target/Preserve"), ("Other Mod", "Current/Other"));
        var first = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var second = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        first.FileChanges.Single().ExpectedSha256.Should().Be(second.FileChanges.Single().ExpectedSha256);

        var expectedJson = Encoding.UTF8.GetString(Convert.FromBase64String(first.FileChanges.Single().ExpectedBytesBase64));
        using var document = JsonDocument.Parse(expectedJson);
        var data = document.RootElement.GetProperty("Data");
        data.GetProperty("Preserve Mod").GetString().Should().Be("Target/Preserve/Preserve Mod");
        data.GetProperty("Other Mod").GetString().Should().Be("Current/Other/Other Mod");
        document.RootElement.GetProperty("FutureKey").GetString().Should().Be("keep-me");
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

        context.Fixture.WriteModData(("Plan Mod", "Changed/OutsidePlan"));

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
            new DryRunPlanEntry("Duplicate", "Duplicate", "Current/Folder", "Target/One", OrganizerProposalSource.Manual, false, OrganizerRowStatus.ValidChange, "sort_order.json:Duplicate", context.Fixture.SortOrderPath, "Duplicate", "A", "B", Array.Empty<string>(), true, "Target/One/Duplicate"),
            new DryRunPlanEntry("Duplicate", "Duplicate", "Current/Folder", "Target/Two", OrganizerProposalSource.Manual, false, OrganizerRowStatus.ValidChange, "sort_order.json:Duplicate", context.Fixture.SortOrderPath, "Duplicate", "A", "C", Array.Empty<string>(), true, "Target/Two/Duplicate"),
        };

        var snapshot = context.BuildSnapshot(("Duplicate", "Target/One"));
        var act = () => context.Writer.BuildExpectedFileChangesAsync(context.Installation, entries, snapshot, CancellationToken.None);

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
        File.SetAttributes(context.Fixture.SortOrderPath, File.GetAttributes(context.Fixture.SortOrderPath) | FileAttributes.ReadOnly);
        try
        {
            var result = await context.PreflightService.CheckAsync(plan, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(context.Fixture.SortOrderPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Prepare_CreatesVerifiedBackup_AndRollbackTransaction()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Backup Mod", """{"FileVersion":3,"Name":"Backup Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Backup Mod", "Current/Folder"));
        context.Fixture.WriteLocalModData("Backup Mod", """{"FileVersion":3,"Favorite":false}""");

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Backup Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var details = await context.HistoryService.TryLoadOperationAsync(operation.OperationId, CancellationToken.None);

        details.Should().NotBeNull();
        details!.Operation.VerificationStatus.Should().Be(OperationVerificationStatus.Verified);
        details.Operation.ApplyStatus.Should().Be(ApplyStatus.Ready);
        details.RollbackTransaction.Should().NotBeNull();

        // Backup/rollback now cover the whole Penumbra config directory, not just the file(s)
        // Apply is about to write.
        var writeTarget = details.RollbackTransaction!.Files.Should().ContainSingle(file => file.TargetPath == plan.FileChanges.Single().TargetPath).Subject;
        writeTarget.ExpectedAppliedSha256.Should().Be(plan.FileChanges.Single().ExpectedSha256);
        writeTarget.ApplyResultStatus.Should().Be(ApplyResultStatus.Pending);
        details.RollbackTransaction.Files.Should().Contain(file => file.TargetPath != plan.FileChanges.Single().TargetPath && file.ApplyResultStatus == ApplyResultStatus.Applied);
        details.Manifest!.Files.Should().Contain(file => file.SourceTargetPath == context.Fixture.SortOrderPath);
        details.Manifest.Files.Count.Should().BeGreaterThan(1);
        details.Operation.OperationFolder.Should().StartWith(context.BackupsRoot);
    }

    [Fact]
    public async Task Apply_Succeeds_PreservesUnrelatedData_AndEnablesRollback()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Apply Mod", """{"FileVersion":3,"Name":"Apply Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Other Mod", """{"FileVersion":3,"Name":"Other Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Apply Mod", "Current/Apply"), ("Other Mod", "Current/Other"));

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

        // The moved mod lands in its new folder with the display leaf preserved, and the
        // unrelated entry is untouched.
        context.Fixture.CurrentFolderOf("Apply Mod").Should().Be("Target/Apply");
        context.Fixture.CurrentSortPathOf("Apply Mod").Should().Be("Target/Apply/Apply Mod");
        context.Fixture.CurrentFolderOf("Other Mod").Should().Be("Current/Other");
    }

    [Fact]
    public async Task Apply_CreatesSortOrderJson_WhenAbsent_AndRollbackRestoresEmptyBaseline()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("New Mod", """{"FileVersion":3,"Name":"New Mod","Author":"Author"}""");
        // Fresh install: no sort_order.json yet, so the mod sits at the root.
        File.Exists(context.Fixture.SortOrderPath).Should().BeFalse();

        await context.ScanAsync();
        context.Inventory!.Mods.Single().CurrentVirtualFolder.Should().BeEmpty();

        var snapshot = context.BuildSnapshot(("New Mod", "Clothing"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        plan.ApplyPermitted.Should().BeTrue();

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Completed);
        File.Exists(context.Fixture.SortOrderPath).Should().BeTrue();
        context.Fixture.CurrentSortPathOf("New Mod").Should().Be("Clothing/New Mod");

        var rollback = await context.RollbackService.ExecuteAsync(operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        rollback.Status.Should().Be(RollbackTransactionStatus.Completed);
        // Rolled back to the empty baseline: the mod is no longer placed (back at root).
        context.Fixture.CurrentSortPathOf("New Mod").Should().BeNull();
    }

    [Fact]
    public async Task Apply_LiveSortOrderMissing_RecoversFromBak_AndPreservesUntouchedEntries()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Apply Mod", """{"FileVersion":3,"Name":"Apply Mod","Author":"Author"}""");
        context.Fixture.CreateMod("Other Mod", """{"FileVersion":3,"Name":"Other Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Apply Mod", "Current/Apply"), ("Other Mod", "Current/Other"));
        // Simulate an unclean shutdown: the live sort_order.json is gone, only Penumbra's own
        // .bak survives. Both mods' real organization must still make it through Apply.
        File.Move(context.Fixture.SortOrderPath, context.Fixture.SortOrderPath + ".bak");

        await context.ScanAsync();
        context.Inventory!.Mods.Single(m => m.StableScanId == "Other Mod").CurrentVirtualFolder.Should().Be("Current/Other");

        var snapshot = context.BuildSnapshot(("Apply Mod", "Target/Apply"), ("Other Mod", "Current/Other"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Completed);
        File.Exists(context.Fixture.SortOrderPath).Should().BeTrue();
        // The moved mod lands in its new folder, and the untouched mod's real folder (recovered
        // from .bak) survives instead of being silently reset to root.
        context.Fixture.CurrentFolderOf("Apply Mod").Should().Be("Target/Apply");
        context.Fixture.CurrentFolderOf("Other Mod").Should().Be("Current/Other");
    }

    [Fact]
    public async Task Apply_ModDataDb_ModWithNoDocument_IsExcludedFromWriteWithWarning_OtherModsStillApply()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CopyRealLiteDbAssembly();
        context.Fixture.CreateMod("Mover", """{"FileVersion":3,"Name":"Mover","Author":"Author"}""");
        context.Fixture.CreateMod("Orphan", """{"FileVersion":3,"Name":"Orphan","Author":"Author"}""");
        // "Orphan" exists on disk but has no mod_data.db document at all -- mirrors a real-world
        // mod Penumbra never registered in its LiteDB store (reported on a real Linux install).
        context.Fixture.WriteModDataDb(("Mover", "Current/Folder"));

        await context.ScanAsync();
        context.Inventory!.Mods.Single(m => m.StableScanId == "Orphan").CurrentVirtualFolder.Should().BeEmpty();

        var snapshot = context.BuildSnapshot(("Mover", "Target/Folder"), ("Orphan", "Target/Orphan"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.ApplyPermitted.Should().BeTrue();
        var orphanEntry = plan.Entries.Single(entry => entry.StableScanId == "Orphan");
        orphanEntry.RequiresWrite.Should().BeFalse();
        orphanEntry.Warnings.Should().Contain(w => w.Contains("mod_data.db has no record", StringComparison.Ordinal));
        // Downgraded to NeedsReview (not left as ValidChange) so DryRunPlanner's ChangedRowCount
        // and MainViewModel's "All target records mapped" apply-checklist line -- both of which
        // key off ValidationStatus == ValidChange -- don't misreport this as a mapped-but-unwritten
        // change.
        orphanEntry.ValidationStatus.Should().Be(OrganizerRowStatus.NeedsReview);
        plan.Entries.Single(entry => entry.StableScanId == "Mover").RequiresWrite.Should().BeTrue();
        plan.Summary.ChangedRowCount.Should().Be(1);
        plan.Summary.AffectedModCount.Should().Be(1);

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Completed);
        var afterApply = PenumbraModDataDb.Load(context.Fixture.PenumbraConfigPath, context.Installation);
        afterApply.Status.Should().Be(PenumbraModDataDbLoadStatus.Success);
        afterApply.Data!.GetFolderFor("Mover").Should().Be("Target/Folder");
        afterApply.Data.GetEntry("Orphan").Should().BeNull();
    }

    [Fact]
    public async Task Apply_ModDataDbIsAuthoritative_MovesFolder_PreservesUnrelatedAndProtected_AndRollbackReverts()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CopyRealLiteDbAssembly();
        context.Fixture.CreateMod("Mover", """{"FileVersion":3,"Name":"Mover","Author":"Author"}""");
        context.Fixture.CreateMod("Stationary", """{"FileVersion":3,"Name":"Stationary","Author":"Author"}""");
        context.Fixture.CreateMod("Guarded", """{"FileVersion":3,"Name":"Guarded","Author":"Author"}""");
        // No sort_order.json at all: mod_data.db is unambiguously the authoritative format here.
        context.Fixture.WriteModDataDb(("Mover", "Current/Folder"), ("Stationary", "Current/Folder"), ("Guarded", "Current/Folder"));

        await context.ScanAsync();
        context.Inventory!.Mods.Single(m => m.StableScanId == "Mover").CurrentVirtualFolder.Should().Be("Current/Folder");

        var snapshot = context.BuildSnapshot(
            ["Guarded"],
            ("Mover", "Target/Folder"), ("Stationary", "Current/Folder"), ("Guarded", "Current/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.ApplyPermitted.Should().BeTrue();
        plan.FileChanges.Should().ContainSingle(change => change.WriteTargetKind == PenumbraWriteTargetKind.ModDataDb);
        plan.Entries.Single(entry => entry.StableScanId == "Mover").RequiresWrite.Should().BeTrue();
        plan.Entries.Single(entry => entry.StableScanId == "Guarded").RequiresWrite.Should().BeFalse();

        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Completed);
        result.RollbackAvailable.Should().BeTrue();

        var afterApply = PenumbraModDataDb.Load(context.Fixture.PenumbraConfigPath, context.Installation);
        afterApply.Status.Should().Be(PenumbraModDataDbLoadStatus.Success);
        afterApply.Data!.GetFolderFor("Mover").Should().Be("Target/Folder");
        afterApply.Data.GetFolderFor("Stationary").Should().Be("Current/Folder");
        afterApply.Data.GetFolderFor("Guarded").Should().Be("Current/Folder");

        var rollback = await context.RollbackService.ExecuteAsync(operation.OperationId, RollbackExecutionOptions.Default, CancellationToken.None);
        rollback.Status.Should().Be(RollbackTransactionStatus.Completed);

        var afterRollback = PenumbraModDataDb.Load(context.Fixture.PenumbraConfigPath, context.Installation);
        afterRollback.Status.Should().Be(PenumbraModDataDbLoadStatus.Success);
        afterRollback.Data!.GetFolderFor("Mover").Should().Be("Current/Folder");
    }

    [Fact]
    public async Task PostApplyVerification_ModDataDb_NullInstallation_DegradesToHashChecksWithWarning()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CopyRealLiteDbAssembly();
        context.Fixture.CreateMod("Mover", """{"FileVersion":3,"Name":"Mover","Author":"Author"}""");
        context.Fixture.WriteModDataDb(("Mover", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Mover", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);
        result.Status.Should().Be(ApplyStatus.Completed);

        var verificationService = new PostApplyVerificationService();

        var withoutInstallation = await verificationService.VerifyAsync(plan, result, installation: null, CancellationToken.None);
        withoutInstallation.Succeeded.Should().BeTrue();
        withoutInstallation.VerifiedChangedModCount.Should().Be(0);
        withoutInstallation.Warnings.Should().Contain(w => w.Contains("without a live installation", StringComparison.Ordinal));

        var withInstallation = await verificationService.VerifyAsync(plan, result, context.Installation, CancellationToken.None);
        withInstallation.Succeeded.Should().BeTrue();
        withInstallation.VerifiedChangedModCount.Should().Be(1);
    }

    [Fact]
    public async Task CreatePlan_ModDataDbEngineUnavailable_ThrowsClearError()
    {
        using var context = await ApplyTestContext.CreateAsync();
        // Deliberately do NOT call CopyRealLiteDbAssembly(): no LiteDB.dll next to the plugin.
        context.Fixture.CreateMod("Mover", """{"FileVersion":3,"Name":"Mover","Author":"Author"}""");
        context.Fixture.WriteModDataDb(("Mover", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Mover", "Target/Folder"));

        var act = () => context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("LiteDB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_ModDataDbBlocksWhenSourceHashChanges()
    {
        using var context = await ApplyTestContext.CreateAsync();
        context.Fixture.CopyRealLiteDbAssembly();
        context.Fixture.CreateMod("Hash Mod", """{"FileVersion":3,"Name":"Hash Mod","Author":"Author"}""");
        context.Fixture.WriteModDataDb(("Hash Mod", "Current/Folder"));

        await context.ScanAsync();
        var snapshot = context.BuildSnapshot(("Hash Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);
        var operation = await context.ApplyService.PrepareAsync(plan, context.Installation, snapshot, CancellationToken.None);
        // Simulate Penumbra (or the user's own tool) changing mod_data.db between dry run and
        // Apply: a full rewrite, since a real LiteDB file can't have a duplicate "_id" re-inserted.
        File.Delete(context.Fixture.ModDataDbPath);
        context.Fixture.WriteModDataDb(("Hash Mod", "Changed/OutsideApply"));

        var result = await context.ApplyService.ApplyAsync(plan, operation, context.Installation, snapshot, CancellationToken.None);

        result.Status.Should().Be(ApplyStatus.Failed);
        result.Files.Should().ContainSingle(file => file.Status == ApplyResultStatus.Failed && file.Message.Contains("source hash", StringComparison.OrdinalIgnoreCase));
        var afterFailedApply = PenumbraModDataDb.Load(context.Fixture.PenumbraConfigPath, context.Installation);
        afterFailedApply.Data!.GetFolderFor("Hash Mod").Should().Be("Changed/OutsideApply");
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
        context.Fixture.CurrentFolderOf("Hash Mod").Should().Be("Changed/OutsideApply");
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
        context.Fixture.CurrentFolderOf("Rollback Mod").Should().Be("Current/Folder");
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
        context.Fixture.CurrentFolderOf("Conflict Mod").Should().Be("External/Change");
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
            Writer = new PenumbraOrganizationWriter();
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
        public IPenumbraVirtualFolderWriter Writer { get; }
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

        public ProposalSnapshot BuildSnapshotWithEmptyFolders(
            (string StableScanId, string ProposedFolder)[] changes,
            string[] emptyFolders)
        {
            var baseSnapshot = BuildSnapshot(changes, Array.Empty<string>());
            var folders = baseSnapshot.Folders
                .Concat(emptyFolders.Select(path => new OrganizerFolder(path, ManuallyCreated: true, Protected: false)))
                .ToArray();
            return baseSnapshot with { Folders = folders };
        }

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
