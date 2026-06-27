using System.Security.Cryptography;
using System.Text.Json;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

internal static class RecoveryFileInspector
{
    public static async Task<(long Length, string Sha256)> GetLengthAndHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexString(hash));
    }

    public static async Task<JsonValidationStatus> ValidateJsonAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var _ = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return JsonValidationStatus.Valid;
        }
        catch (JsonException)
        {
            return JsonValidationStatus.Invalid;
        }
    }

    public static BackupFileClassification Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return BackupFileClassification.Json;
        if (extension is ".txt" or ".log" or ".cfg" or ".ini" or ".csv" or ".yaml" or ".yml" or ".xml")
            return BackupFileClassification.Text;
        return BackupFileClassification.Binary;
    }
}
