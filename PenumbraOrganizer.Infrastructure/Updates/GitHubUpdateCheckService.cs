namespace PenumbraOrganizer.Infrastructure.Updates;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Core.Services;

public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    private const string ReleasesUrl = "https://api.github.com/repos/monstersghost/PenumbraOrganizer/releases";
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateCheckService> _logger;

    public GitHubUpdateCheckService(HttpClient httpClient, ILogger<GitHubUpdateCheckService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            // GitHub's API rejects requests with no User-Agent header.
            request.Headers.UserAgent.ParseAdd("PenumbraOrganizer-UpdateCheck");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(false, null, null, $"GitHub returned {(int)response.StatusCode}.");

            var releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseDto>>(cancellationToken: cancellationToken);
            if (releases is null || releases.Count == 0)
                return new UpdateCheckResult(false, null, null, "No releases were found.");

            GitHubReleaseDto? latest = null;
            foreach (var release in releases.Where(r => !r.Draft))
            {
                if (latest is null || AppVersionComparer.IsNewer(StripLeadingV(latest.TagName), release.TagName))
                    latest = release;
            }

            if (latest is null)
                return new UpdateCheckResult(false, null, null, "No non-draft releases were found.");

            var updateAvailable = AppVersionComparer.IsNewer(currentVersion, latest.TagName);
            return new UpdateCheckResult(updateAvailable, latest.TagName, latest.HtmlUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult(false, null, null, "Could not reach GitHub to check for updates.");
        }
    }

    private static string StripLeadingV(string tag) => tag.TrimStart('v', 'V');

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft);
}
