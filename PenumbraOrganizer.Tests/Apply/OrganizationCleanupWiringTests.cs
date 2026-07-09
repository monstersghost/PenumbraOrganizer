namespace PenumbraOrganizer.Tests.Apply;

using FluentAssertions;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Apply;

public sealed class OrganizationCleanupWiringTests
{
    [Fact]
    public async Task CreatePlanAsync_NoOrganizationCleanupWriterConfigured_NeverProducesOrganizationJsonChange()
    {
        using var context = await DryRunAndApplyTests.ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{}},"Separators":{}}""");
        await context.ScanAsync();

        // context.Planner was built with no IOrganizationCleanupWriter (Task 2's default-null
        // path) -- this is the pre-Task-3 behavior and must remain unchanged for any caller that
        // hasn't opted in.
        var snapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var plan = await context.Planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Should().NotContain(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson);
        plan.OrganizationCleanupSourceFile.Should().BeNull();
    }

    [Fact]
    public async Task CreatePlanAsync_WithOrganizationCleanupWriterAndConfirmedSelections_AppendsOrganizationJsonChange()
    {
        using var context = await DryRunAndApplyTests.ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{}},"Separators":{}}""");
        await context.ScanAsync();

        var cleanupWriter = new OrganizationCleanupWriter();
        var planner = new DryRunPlanner(context.Writer, context.ValidationService, cleanupWriter);
        var baseSnapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var snapshot = baseSnapshot with { OrganizationCleanupSelections = ["Orphaned"] };

        var plan = await planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Should().ContainSingle(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson);
        plan.OrganizationCleanupSourceFile.Should().NotBeNull();
        plan.OrganizationCleanupSourceFile!.Path.Should().Be(context.Fixture.OrganizationJsonPath);
    }

    [Fact]
    public async Task CreatePlanAsync_OrganizationJsonMissing_NeverBlocksThePrimaryWrite()
    {
        using var context = await DryRunAndApplyTests.ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        // No organization.json written at all -- must not throw, must not block sort_order.json.
        await context.ScanAsync();

        var cleanupWriter = new OrganizationCleanupWriter();
        var planner = new DryRunPlanner(context.Writer, context.ValidationService, cleanupWriter);
        var baseSnapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var snapshot = baseSnapshot with { OrganizationCleanupSelections = ["Whatever"] };

        var plan = await planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        plan.FileChanges.Should().ContainSingle(change => change.TargetPath == context.Fixture.SortOrderPath);
        plan.FileChanges.Should().NotContain(change => change.WriteTargetKind == PenumbraWriteTargetKind.OrganizationJson);
        plan.OrganizationCleanupSourceFile.Should().BeNull();
    }

    [Fact]
    public async Task GetInvalidationReasonsAsync_OrganizationJsonContentChangedSincePlan_ReportsSourceFileHashChanged()
    {
        using var context = await DryRunAndApplyTests.ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{}},"Separators":{}}""");
        await context.ScanAsync();

        var cleanupWriter = new OrganizationCleanupWriter();
        var invalidationService = new PlanInvalidationService(context.Writer, cleanupWriter);
        var planner = new DryRunPlanner(context.Writer, context.ValidationService, cleanupWriter);
        var baseSnapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var snapshot = baseSnapshot with { OrganizationCleanupSelections = ["Orphaned"] };
        var plan = await planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        // Penumbra (or the user) touches organization.json after the plan was built.
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{},"New":{}},"Separators":{}}""");

        var reasons = await invalidationService.GetInvalidationReasonsAsync(plan, context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        reasons.Should().Contain(PlanInvalidationReason.SourceFileHashChanged);
    }

    [Fact]
    public async Task GetInvalidationReasonsAsync_OrganizationJsonUnchanged_DoesNotReportSourceFileHashChanged()
    {
        using var context = await DryRunAndApplyTests.ApplyTestContext.CreateAsync();
        context.Fixture.CreateMod("Mapped Mod", """{"FileVersion":3,"Name":"Mapped Mod","Author":"Author"}""");
        context.Fixture.WriteModData(("Mapped Mod", "Current/FromDb"));
        context.Fixture.WriteOrganizationJson("""{"Version":1,"Folders":{"Orphaned":{}},"Separators":{}}""");
        await context.ScanAsync();

        var cleanupWriter = new OrganizationCleanupWriter();
        var invalidationService = new PlanInvalidationService(context.Writer, cleanupWriter);
        var planner = new DryRunPlanner(context.Writer, context.ValidationService, cleanupWriter);
        var baseSnapshot = context.BuildSnapshot(("Mapped Mod", "Target/Folder"));
        var snapshot = baseSnapshot with { OrganizationCleanupSelections = ["Orphaned"] };
        var plan = await planner.CreatePlanAsync(context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        var reasons = await invalidationService.GetInvalidationReasonsAsync(plan, context.Installation, context.Inventory!, snapshot, CancellationToken.None);

        reasons.Should().NotContain(PlanInvalidationReason.SourceFileHashChanged);
    }
}
