namespace PenumbraOrganizer.Infrastructure.Updates;

using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AppUpdateService> _logger;

    public AppUpdateService(HttpClient httpClient, ILogger<AppUpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AppUpdatePrepareResult> PrepareUpdateAsync(UpdateCheckResult update, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ZipDownloadUrl) || string.IsNullOrWhiteSpace(update.ChecksumsDownloadUrl))
            return new AppUpdatePrepareResult(false, null, "This release doesn't provide a downloadable update package.");

        string? extractedFolder = null;
        try
        {
            progress?.Report("Downloading checksums...");
            var checksumsText = await _httpClient.GetStringAsync(update.ChecksumsDownloadUrl, cancellationToken);
            var checksums = ParseChecksums(checksumsText);

            var zipFileName = Path.GetFileName(new Uri(update.ZipDownloadUrl).LocalPath);
            if (!checksums.TryGetValue(zipFileName, out var expectedZipHash))
                return new AppUpdatePrepareResult(false, null, $"No checksum entry found for {zipFileName}.");

            progress?.Report("Downloading update...");
            var zipBytes = await _httpClient.GetByteArrayAsync(update.ZipDownloadUrl, cancellationToken);
            var actualZipHash = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
            if (!string.Equals(actualZipHash, expectedZipHash, StringComparison.OrdinalIgnoreCase))
                return new AppUpdatePrepareResult(false, null, "The downloaded update failed checksum verification.");

            progress?.Report("Extracting update...");
            extractedFolder = Path.Combine(Path.GetTempPath(), "PenumbraOrganizerUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractedFolder);
            using (var zipStream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(extractedFolder);
            }

            var extractedExePath = Path.Combine(extractedFolder, "PenumbraOrganizer.exe");
            if (!File.Exists(extractedExePath))
            {
                TryDeleteDirectory(extractedFolder);
                return new AppUpdatePrepareResult(false, null, "The extracted update is missing PenumbraOrganizer.exe.");
            }

            if (!checksums.TryGetValue("PenumbraOrganizer.exe", out var expectedExeHash))
            {
                TryDeleteDirectory(extractedFolder);
                return new AppUpdatePrepareResult(false, null, "No checksum entry found for PenumbraOrganizer.exe.");
            }

            var actualExeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(extractedExePath, cancellationToken))).ToLowerInvariant();
            if (!string.Equals(actualExeHash, expectedExeHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(extractedFolder);
                return new AppUpdatePrepareResult(false, null, "The extracted update failed checksum verification.");
            }

            return new AppUpdatePrepareResult(true, extractedFolder, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare update");
            if (extractedFolder is not null)
                TryDeleteDirectory(extractedFolder);
            return new AppUpdatePrepareResult(false, null, "Could not download or prepare the update.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup -- a leftover temp folder is harmless, unlike surfacing a
            // secondary error that would mask the real failure reason.
        }
    }

    private static Dictionary<string, string> ParseChecksums(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('﻿');
            if (line.Length == 0)
                continue;

            var spaceIndex = line.IndexOf(' ');
            if (spaceIndex < 0)
                continue;

            var hash = line[..spaceIndex];
            var fileName = line[(spaceIndex + 1)..].TrimStart('*').Trim();
            result[fileName] = hash;
        }

        return result;
    }
}
