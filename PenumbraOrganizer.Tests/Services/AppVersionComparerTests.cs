namespace PenumbraOrganizer.Tests.Services;

using FluentAssertions;
using PenumbraOrganizer.Core.Services;

public sealed class AppVersionComparerTests
{
    [Theory]
    [InlineData("0.3.3-beta", "v0.3.4-beta", true)]
    [InlineData("0.3.3-beta", "v0.4.0-beta", true)]
    [InlineData("0.3.3-beta", "v0.3.3-beta", false)]
    [InlineData("0.3.3-beta", "v0.3.2-beta", false)]
    [InlineData("0.3.3-beta", "not-a-version", false)]
    [InlineData("not-a-version", "v0.3.3-beta", false)]
    public void IsNewer_ComparesNumericPortionIgnoringSuffix(string current, string candidateTag, bool expected)
        => AppVersionComparer.IsNewer(current, candidateTag).Should().Be(expected);
}
