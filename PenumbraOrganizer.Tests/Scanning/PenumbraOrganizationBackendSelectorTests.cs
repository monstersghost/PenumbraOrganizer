namespace PenumbraOrganizer.Tests.Scanning;

using FluentAssertions;
using PenumbraOrganizer.Infrastructure.Penumbra;

public sealed class PenumbraOrganizationBackendSelectorTests
{
    [Fact]
    public void Detect_NeitherFileExists_DefaultsToSortOrderJson()
    {
        using var dir = new TempDirectory();
        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.SortOrderJson);
    }

    [Fact]
    public void Detect_OnlyModDataDbExists_ReturnsModDataDb()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(PenumbraModDataDb.GetPath(dir.Path), "stub");

        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.ModDataDb);
    }

    [Fact]
    public void Detect_OnlySortOrderExists_ReturnsSortOrderJson()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(PenumbraSortOrder.GetPath(dir.Path), "{}");

        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.SortOrderJson);
    }

    [Fact]
    public void Detect_OnlySortOrderBakExists_ReturnsSortOrderJson()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(PenumbraSortOrder.GetBackupPath(dir.Path), "{}");

        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.SortOrderJson);
    }

    [Fact]
    public void Detect_BothExist_PicksMostRecentlyWritten()
    {
        using var dir = new TempDirectory();
        var sortOrderPath = PenumbraSortOrder.GetBackupPath(dir.Path);
        var modDataDbPath = PenumbraModDataDb.GetPath(dir.Path);
        File.WriteAllText(sortOrderPath, "{}");
        File.WriteAllText(modDataDbPath, "stub");

        File.SetLastWriteTimeUtc(sortOrderPath, DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc(modDataDbPath, DateTime.UtcNow);
        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.ModDataDb);

        File.SetLastWriteTimeUtc(modDataDbPath, DateTime.UtcNow.AddDays(-10));
        PenumbraOrganizationBackendSelector.Detect(dir.Path).Should().Be(PenumbraOrganizationBackend.SortOrderJson);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PenumbraOrganizerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
