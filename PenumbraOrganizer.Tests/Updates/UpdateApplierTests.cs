namespace PenumbraOrganizer.Tests.Updates;

using FluentAssertions;
using PenumbraOrganizer.Updater;

public sealed class UpdateApplierTests
{
    [Fact]
    public void Apply_CopiesNewFilesOverOld_AndCleansUpBackupAndSource()
    {
        var (sourceDir, destDir) = CreateTempDirs();
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");
        File.WriteAllText(Path.Combine(sourceDir, "README_FOR_USERS.txt"), "readme");
        File.WriteAllText(Path.Combine(destDir, "PenumbraOrganizer.exe"), "old-exe-content");

        var result = UpdateApplier.Apply(sourceDir, destDir);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().Be("new-exe-content");
        File.ReadAllText(Path.Combine(destDir, "README_FOR_USERS.txt")).Should().Be("readme");
        File.Exists(Path.Combine(destDir, "PenumbraOrganizer.exe.old")).Should().BeFalse();
        Directory.Exists(sourceDir).Should().BeFalse();
    }

    [Fact]
    public void Apply_SucceedsEvenWithNoPreviousExe()
    {
        var (sourceDir, destDir) = CreateTempDirs();
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");

        var result = UpdateApplier.Apply(sourceDir, destDir);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().Be("new-exe-content");
    }

    [Fact]
    public void Apply_RestoresBackup_WhenCopyFails()
    {
        var (sourceDir, destDir) = CreateTempDirs();
        var lockedSourceFile = Path.Combine(sourceDir, "locked.txt");
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");
        File.WriteAllText(lockedSourceFile, "locked-content");
        File.WriteAllText(Path.Combine(destDir, "PenumbraOrganizer.exe"), "old-exe-content");

        UpdateApplyResult result;
        using (new FileStream(lockedSourceFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = UpdateApplier.Apply(sourceDir, destDir);
        }

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().Be("old-exe-content");
        Directory.Exists(destDir + ".old").Should().BeFalse();
    }

    [Fact]
    public void Apply_ClearsStaleBackup_FromPreviousFailedAttempt()
    {
        var (sourceDir, destDir) = CreateTempDirs();
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");
        File.WriteAllText(Path.Combine(destDir, "PenumbraOrganizer.exe"), "old-exe-content");
        Directory.CreateDirectory(destDir + ".old");
        File.WriteAllText(Path.Combine(destDir + ".old", "stale.txt"), "stale");

        var result = UpdateApplier.Apply(sourceDir, destDir);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().Be("new-exe-content");
        Directory.Exists(destDir + ".old").Should().BeFalse();
    }

    [Fact]
    public void Apply_PreservesNestedSubdirectoryStructure()
    {
        var (sourceDir, destDir) = CreateTempDirs();
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        File.WriteAllText(Path.Combine(sourceDir, "nested", "asset.dat"), "nested-content");
        File.WriteAllText(Path.Combine(destDir, "PenumbraOrganizer.exe"), "old-exe-content");

        var result = UpdateApplier.Apply(sourceDir, destDir);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "nested", "asset.dat")).Should().Be("nested-content");
    }

    private static (string SourceDir, string DestDir) CreateTempDirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenumbraOrganizer.Tests.Updater", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source");
        var destDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        return (sourceDir, destDir);
    }
}
