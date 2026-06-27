using System.Text;
using System.Text.Json;

namespace PenumbraOrganizer.Infrastructure.Recovery;

internal static class AtomicJsonFileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
    };

    public static async Task WriteAsync<T>(
        string path,
        T value,
        Func<T, bool>? validator,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("A target directory is required.");
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Utf8NoBom.GetBytes(json);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        var tempValue = await ReadRequiredAsync<T>(tempPath, cancellationToken);
        if (validator is not null && !validator(tempValue))
            throw new InvalidOperationException($"The temporary JSON document at {tempPath} did not pass validation.");

        File.Move(tempPath, path, overwrite: true);

        var finalValue = await ReadRequiredAsync<T>(path, cancellationToken);
        if (validator is not null && !validator(finalValue))
            throw new InvalidOperationException($"The finalized JSON document at {path} did not pass validation.");
    }

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    public static async Task<T> ReadRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        var value = await ReadAsync<T>(path, cancellationToken);
        return value ?? throw new InvalidOperationException($"The JSON document at {path} could not be read.");
    }
}
