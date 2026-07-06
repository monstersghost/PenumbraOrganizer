namespace PenumbraOrganizer.Tests.Scanning;

using FluentAssertions;
using PenumbraOrganizer.Infrastructure.Penumbra;

public sealed class PenumbraSortOrderTests
{
    [Fact]
    public void Parse_ReadsDataAndEmptyFolders()
    {
        var sortOrder = PenumbraSortOrder.Parse(
            """{"Data":{"ModA":"old/Air Force","ModB":"Dt New/Akako dt"},"EmptyFolders":["telegram"]}""");

        sortOrder.Data.Should().HaveCount(2);
        sortOrder.EmptyFolders.Should().ContainSingle().Which.Should().Be("telegram");
    }

    [Fact]
    public void GetFolderFor_ReturnsParentFolder_NotDisplayLeaf()
    {
        var sortOrder = PenumbraSortOrder.Parse("""{"Data":{"ModA":"old/june 2024/My Mod"}}""");

        // The leaf "My Mod" is the display name; the containing folder is everything before it.
        sortOrder.GetFolderFor("ModA").Should().Be("old/june 2024");
        sortOrder.GetFullPathFor("ModA").Should().Be("old/june 2024/My Mod");
    }

    [Fact]
    public void GetFolderFor_RootEntry_ReturnsEmpty()
    {
        var sortOrder = PenumbraSortOrder.Parse("""{"Data":{"Renamed":"Just A Display Name"}}""");

        sortOrder.GetFolderFor("Renamed").Should().BeEmpty();
        sortOrder.GetFullPathFor("Renamed").Should().Be("Just A Display Name");
    }

    [Fact]
    public void MissingEntry_ReturnsRootAndNull()
    {
        var sortOrder = PenumbraSortOrder.Parse("""{"Data":{}}""");

        sortOrder.GetFolderFor("Unknown").Should().BeEmpty();
        sortOrder.GetFullPathFor("Unknown").Should().BeNull();
    }

    [Fact]
    public void Load_MissingFile_TreatedAsEmpty()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var sortOrder = PenumbraSortOrder.Load(emptyDir);
            sortOrder.Data.Should().BeEmpty();
            sortOrder.EmptyFolders.Should().BeEmpty();
            sortOrder.LoadedFromBackup.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingLiveFile_FallsBackToBakFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "sort_order.json.bak"),
                """{"Data":{"ModA":"Gear/Hats/Cool Hat"},"EmptyFolders":["Empty/Shelf"]}""");

            var sortOrder = PenumbraSortOrder.Load(dir);

            sortOrder.LoadedFromBackup.Should().BeTrue();
            sortOrder.GetFolderFor("ModA").Should().Be("Gear/Hats");
            sortOrder.EmptyFolders.Should().ContainSingle().Which.Should().Be("Empty/Shelf");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_LiveFilePresent_DoesNotFallBackEvenWhenBakDiffers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sort_order.json"), """{"Data":{"ModA":"Live/Cool Hat"}}""");
            File.WriteAllText(Path.Combine(dir, "sort_order.json.bak"), """{"Data":{"ModA":"Stale/Cool Hat"}}""");

            var sortOrder = PenumbraSortOrder.Load(dir);

            sortOrder.LoadedFromBackup.Should().BeFalse();
            sortOrder.GetFolderFor("ModA").Should().Be("Live");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParentFolderAndDisplayLeaf_HandleNestedAndRootPaths()
    {
        PenumbraSortOrder.ParentFolder("a/b/c").Should().Be("a/b");
        PenumbraSortOrder.DisplayLeaf("a/b/c").Should().Be("c");
        PenumbraSortOrder.ParentFolder("root-only").Should().BeEmpty();
        PenumbraSortOrder.DisplayLeaf("root-only").Should().Be("root-only");
    }
}
