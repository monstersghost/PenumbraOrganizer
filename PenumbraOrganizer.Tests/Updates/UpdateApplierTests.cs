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
        File.WriteAllText(Path.Combine(sourceDir, "PenumbraOrganizer.exe"), "new-exe-content");
        // Force a copy failure: make the destination path for "extra.txt" a directory instead of
        // a file, so File.Copy throws when it tries to write there.
        File.WriteAllText(Path.Combine(sourceDir, "extra.txt"), "extra-content");
        Directory.CreateDirectory(Path.Combine(destDir, "extra.txt"));
        File.WriteAllText(Path.Combine(destDir, "PenumbraOrganizer.exe"), "old-exe-content");

        var result = UpdateApplier.Apply(sourceDir, destDir);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        // The old exe must be restored, not left renamed to .old or missing.
        File.Exists(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "PenumbraOrganizer.exe")).Should().Be("old-exe-content");
        File.Exists(Path.Combine(destDir, "PenumbraOrganizer.exe.old")).Should().BeFalse();
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
