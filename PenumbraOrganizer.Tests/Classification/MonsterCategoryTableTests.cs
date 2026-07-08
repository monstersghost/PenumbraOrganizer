namespace PenumbraOrganizer.Tests.Classification;

using FluentAssertions;
using PenumbraOrganizer.Core.Classification;

public sealed class MonsterCategoryTableTests
{
    [Theory]
    [InlineData("m8355", ModCategory.Minion)]
    [InlineData("m0466", ModCategory.Mount)]
    [InlineData("m7102", ModCategory.Pet)]
    [InlineData("m6001", ModCategory.Ornament)]
    [InlineData("m6002", ModCategory.Ornament)]
    [InlineData("d0001", ModCategory.Mount)]
    public void TryGetCategory_KnownId_ReturnsExpectedCategory(string modelId, ModCategory expected)
    {
        var found = MonsterCategoryTable.TryGetCategory(modelId, out var category);

        found.Should().BeTrue();
        category.Should().Be(expected);
    }

    [Fact]
    public void TryGetCategory_UnknownId_ReturnsFalse()
    {
        var found = MonsterCategoryTable.TryGetCategory("m9999", out _);

        found.Should().BeFalse();
    }
}
