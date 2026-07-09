namespace PenumbraOrganizer.Tests.Scanning;

using FluentAssertions;
using PenumbraOrganizer.Infrastructure.Penumbra;
using PenumbraOrganizer.Tests.Fixtures;

public sealed class PenumbraOrganizationJsonTests
{
    [Fact]
    public void Parse_ReadsVersionFoldersAndSeparators()
    {
        var result = PenumbraOrganizationJson.Parse("""
        {
          "Version": 1,
          "Folders": {
            "Weapons": { "ExpandedColor": 4294901760, "SortMode": "FoldersFirst" },
            "Weapons/OldCreator": {}
          },
          "Separators": {
            "Gear/---divider---": { "CreationDate": 638123456789 }
          }
        }
        """);

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        result.Data.Should().NotBeNull();
        result.Data!.Version.Should().Be(1);
        result.Data.Folders.Should().HaveCount(2);
        result.Data.Folders["Weapons"].ExpandedColor.Should().Be(4294901760);
        result.Data.Folders["Weapons"].SortMode.Should().Be("FoldersFirst");
        result.Data.Folders["Weapons/OldCreator"].ExpandedColor.Should().BeNull();
        result.Data.SeparatorPaths.Should().ContainSingle().Which.Should().Be("Gear/---divider---");
    }

    [Fact]
    public void Parse_FolderWithNoCustomization_IsCustomizedFalse()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":1,"Folders":{"Plain/Empty":{}}}""");

        result.Data!.Folders["Plain/Empty"].IsCustomized.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"Version":1,"Folders":{"F":{"ExpandedColor":123}}}""")]
    [InlineData("""{"Version":1,"Folders":{"F":{"CollapsedColor":123}}}""")]
    [InlineData("""{"Version":1,"Folders":{"F":{"SortMode":"FoldersFirst"}}}""")]
    [InlineData("""{"Version":1,"Folders":{"F":{"IsSeparator":true}}}""")]
    public void Parse_FolderWithAnyCustomization_IsCustomizedTrue(string json)
    {
        var result = PenumbraOrganizationJson.Parse(json);

        result.Data!.Folders["F"].IsCustomized.Should().BeTrue();
    }

    [Fact]
    public void Parse_UnsupportedVersion_ReturnsUnsupportedVersionStatus()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":2,"Folders":{}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.UnsupportedVersion);
        result.Version.Should().Be(2);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingVersionField_ReturnsMalformed()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Folders":{}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsMalformed()
    {
        var result = PenumbraOrganizationJson.Parse("not json at all");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
    }

    [Fact]
    public void Parse_RootIsArrayNotObject_ReturnsMalformed()
    {
        var result = PenumbraOrganizationJson.Parse("[1,2,3]");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
    }

    [Fact]
    public void Parse_NoFoldersOrSeparatorsProperties_StillSucceedsWithEmptyCollections()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":1}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        result.Data!.Folders.Should().BeEmpty();
        result.Data.SeparatorPaths.Should().BeEmpty();
    }

    [Fact]
    public void Load_MissingFile_ReturnsNotFound()
    {
        using var fixture = new TemporaryPenumbraFixture();

        var result = PenumbraOrganizationJson.Load(fixture.PenumbraConfigPath);

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.NotFound);
    }

    [Fact]
    public void Load_RealFile_ReturnsSuccessWithParsedData()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""
        {
          "Version": 1,
          "Folders": {
            "Gear/Hats": { "SortMode": "FoldersFirst" }
          },
          "Separators": {}
        }
        """);

        var result = PenumbraOrganizationJson.Load(fixture.PenumbraConfigPath);

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        result.Data!.Folders.Should().ContainKey("Gear/Hats");
    }

    [Fact]
    public void GetPath_CombinesConfigDirectoryWithModFilesystemSubfolder()
    {
        var path = PenumbraOrganizationJson.GetPath(@"C:\Config");

        path.Should().Be(Path.Combine(@"C:\Config", "mod_filesystem", "organization.json"));
    }

    [Fact]
    public void Parse_NonIntegerVersion_ReturnsMalformed()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":1.5,"Folders":{}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Parse_Int32OverflowVersion_ReturnsMalformed()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":99999999999,"Folders":{}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Load_FileLockedExclusively_ReturnsMalformed()
    {
        using var fixture = new TemporaryPenumbraFixture();
        fixture.WriteOrganizationJson("""{"Version":1,"Folders":{}}""");

        // Open the file with exclusive access (FileShare.None) to simulate an access-denied scenario
        using var lockStream = File.Open(fixture.OrganizationJsonPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = PenumbraOrganizationJson.Load(fixture.PenumbraConfigPath);

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Malformed);
        result.Data.Should().BeNull();
    }

    [Fact]
    public void Parse_NegativeExpandedColor_FolderIncludedWithNullColor()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":1,"Folders":{"F":{"ExpandedColor":-1}}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        result.Data!.Folders.Should().ContainKey("F");
        result.Data.Folders["F"].ExpandedColor.Should().BeNull();
    }

    [Fact]
    public void Parse_NonIntegerCollapsedColor_FolderIncludedWithNullColor()
    {
        var result = PenumbraOrganizationJson.Parse("""{"Version":1,"Folders":{"F":{"CollapsedColor":1.5}}}""");

        result.Status.Should().Be(PenumbraOrganizationJsonLoadStatus.Success);
        result.Data!.Folders.Should().ContainKey("F");
        result.Data.Folders["F"].CollapsedColor.Should().BeNull();
    }
}
