namespace PenumbraOrganizer.Tests.Updates;

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Infrastructure.Updates;

public sealed class GitHubUpdateCheckServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsUpdateAvailable_WhenNewerReleaseExists()
    {
        var body = """
        [
            { "tag_name": "v0.3.4-beta", "html_url": "https://github.com/monstersghost/PenumbraOrganizer/releases/tag/v0.3.4-beta", "draft": false },
            { "tag_name": "v0.3.3-beta", "html_url": "https://github.com/monstersghost/PenumbraOrganizer/releases/tag/v0.3.3-beta", "draft": false }
        ]
        """;
        var service = new GitHubUpdateCheckService(new HttpClient(new FakeHandler(HttpStatusCode.OK, body)), NullLogger<GitHubUpdateCheckService>.Instance);

        var result = await service.CheckForUpdateAsync("0.3.3-beta", CancellationToken.None);

        result.UpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("v0.3.4-beta");
        result.ReleaseUrl.Should().Be("https://github.com/monstersghost/PenumbraOrganizer/releases/tag/v0.3.4-beta");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNoUpdate_WhenAlreadyOnLatest()
    {
        var body = """[{ "tag_name": "v0.3.3-beta", "html_url": "https://example.com/v0.3.3-beta", "draft": false }]""";
        var service = new GitHubUpdateCheckService(new HttpClient(new FakeHandler(HttpStatusCode.OK, body)), NullLogger<GitHubUpdateCheckService>.Instance);

        var result = await service.CheckForUpdateAsync("0.3.3-beta", CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresDraftReleases()
    {
        var body = """
        [
            { "tag_name": "v9.9.9-beta", "html_url": "https://example.com/draft", "draft": true },
            { "tag_name": "v0.3.3-beta", "html_url": "https://example.com/v0.3.3-beta", "draft": false }
        ]
        """;
        var service = new GitHubUpdateCheckService(new HttpClient(new FakeHandler(HttpStatusCode.OK, body)), NullLogger<GitHubUpdateCheckService>.Instance);

        var result = await service.CheckForUpdateAsync("0.3.3-beta", CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdateAsync_FailsGracefully_WhenRequestErrors()
    {
        var service = new GitHubUpdateCheckService(new HttpClient(new FakeHandler(HttpStatusCode.ServiceUnavailable, "{}")), NullLogger<GitHubUpdateCheckService>.Instance);

        var result = await service.CheckForUpdateAsync("0.3.3-beta", CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
