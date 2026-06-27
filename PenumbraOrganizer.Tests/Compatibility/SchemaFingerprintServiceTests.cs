namespace PenumbraOrganizer.Tests.Compatibility;

using FluentAssertions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

public sealed class SchemaFingerprintServiceTests
{
    [Fact]
    public void Create_ReportsMissingRequiredField()
    {
        var fingerprint = SchemaFingerprintService.Create("meta.json", """{ "Name": "Mod" }""", new HashSet<string> { "FileVersion", "Name" });

        fingerprint.DifferenceKind.Should().Be(SchemaDifferenceKind.MissingKnownRequiredField);
        fingerprint.Notes.Should().Contain(note => note.Contains("FileVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_DetectsIdenticalFingerprint()
    {
        var result = SchemaFingerprintService.Compare("{Name:string;}", "{Name:string;}");
        result.Should().Be(SchemaDifferenceKind.None);
    }
}
