namespace PenumbraOrganizer.Tests.Updates;

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Updates;

public sealed class AppUpdateServiceTests
{
    private const string ZipUrl = "https://example.com/PenumbraOrganizer-v0.3.4-beta-win-x64.zip";
    private const string ChecksumsUrl = "https://example.com/SHA256SUMS.txt";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        private readonly string _checksumsText;
        public bool CorruptZipResponse { get; set; }

        public RoutingHandler(byte[] zipBytes, string checksumsText)
        {
            _zipBytes = zipBytes;
            _checksumsText = checksumsText;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.ToString() == ChecksumsUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_checksumsText) });

            var bytes = CorruptZipResponse ? _zipBytes.Concat(new byte[] { 0xFF }).ToArray() : _zipBytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private static (byte[] ZipBytes, string ChecksumsText) BuildFixture(string exeContent)
    {
        var exeBytes = Encoding.UTF8.GetBytes(exeContent);
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("PenumbraOrganizer.exe");
            using var entryStream = entry.Open();
            entryStream.Write(exeBytes);
        }
        var zipBytes = zipStream.ToArray();

        var zipHash = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        var exeHash = Convert.ToHexString(SHA256.HashData(exeBytes)).ToLowerInvariant();
        var checksums = $"{zipHash} *PenumbraOrganizer-v0.3.4-beta-win-x64.zip\n{exeHash} *PenumbraOrganizer.exe\n";

        return (zipBytes, checksums);
    }

    private static UpdateCheckResult Update() => new(true, "v0.3.4-beta", "https://example.com/release", null, ZipUrl, ChecksumsUrl);

    [Fact]
    public async Task PrepareUpdateAsync_ExtractsVerifiedFiles_OnValidDownload()
    {
        var (zipBytes, checksums) = BuildFixture("new-exe-content");
        var service = new AppUpdateService(new HttpClient(new RoutingHandler(zipBytes, checksums)), NullLogger<AppUpdateService>.Instance);

        var result = await service.PrepareUpdateAsync(Update(), null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ExtractedFolderPath.Should().NotBeNullOrEmpty();
        File.ReadAllText(Path.Combine(result.ExtractedFolderPath!, "PenumbraOrganizer.exe")).Should().Be("new-exe-content");

        Directory.Delete(result.ExtractedFolderPath!, recursive: true);
    }

    [Fact]
    public async Task PrepareUpdateAsync_FailsChecksumVerification_WhenZipIsTampered()
    {
        var (zipBytes, checksums) = BuildFixture("new-exe-content");
        var handler = new RoutingHandler(zipBytes, checksums) { CorruptZipResponse = true };
        var service = new AppUpdateService(new HttpClient(handler), NullLogger<AppUpdateService>.Instance);

        var result = await service.PrepareUpdateAsync(Update(), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ExtractedFolderPath.Should().BeNull();
        result.ErrorMessage.Should().Contain("checksum", "the error should tell the user verification failed, not just that something went wrong");
    }

    [Fact]
    public async Task PrepareUpdateAsync_FailsGracefully_WhenAssetUrlsAreMissing()
    {
        var service = new AppUpdateService(new HttpClient(new RoutingHandler(Array.Empty<byte>(), string.Empty)), NullLogger<AppUpdateService>.Instance);
        var updateWithoutAssets = new UpdateCheckResult(true, "v0.3.4-beta", "https://example.com/release", null, null, null);

        var result = await service.PrepareUpdateAsync(updateWithoutAssets, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ExtractedFolderPath.Should().BeNull();
    }
}
