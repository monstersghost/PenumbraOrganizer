namespace PenumbraOrganizer.Infrastructure.Sessions;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class OrganizerSessionService : IOrganizerSessionService
{
    private const string LastSessionFileName = "last-session.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public OrganizerSessionService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenumbraOrganizer",
            "Sessions"))
    {
    }

    public OrganizerSessionService(string sessionsDirectory)
    {
        SessionsDirectory = sessionsDirectory;
        LastSessionPath = Path.Combine(SessionsDirectory, LastSessionFileName);
    }

    public string SessionsDirectory { get; }
    public string LastSessionPath { get; }

    public async Task SaveLastSessionAsync(OrganizerSessionDocument session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SessionsDirectory);
        var tempPath = LastSessionPath + ".tmp";
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false).GetBytes(json);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        await using (var validationStream = File.OpenRead(tempPath))
        {
            var parsed = await JsonSerializer.DeserializeAsync<OrganizerSessionDocument>(validationStream, cancellationToken: cancellationToken);
            if (parsed is null || parsed.FormatVersion != 1)
                throw new InvalidOperationException("The saved organizer session could not be validated.");
        }

        File.Move(tempPath, LastSessionPath, overwrite: true);
    }

    public async Task<OrganizerSessionRestoreResult> TryLoadLastSessionAsync(ScanInventory inventory, CancellationToken cancellationToken)
    {
        if (!File.Exists(LastSessionPath))
            return new OrganizerSessionRestoreResult(false, false, "No saved organizer session was found.", null);

        OrganizerSessionDocument? session;
        await using (var stream = File.OpenRead(LastSessionPath))
        {
            session = await JsonSerializer.DeserializeAsync<OrganizerSessionDocument>(stream, cancellationToken: cancellationToken);
        }

        if (session is null || session.FormatVersion != 1)
            return new OrganizerSessionRestoreResult(false, true, "The saved organizer session uses an unsupported format.", session);

        var currentInstallation = BuildInstallationIdentity(inventory.Installation);
        if (!string.Equals(session.InstallationIdentity, currentInstallation, StringComparison.Ordinal))
            return new OrganizerSessionRestoreResult(false, true, "This saved organizer session belongs to a different Penumbra setup.", session);

        if (!string.Equals(session.InstalledPenumbraVersion, inventory.Installation.InstalledVersion, StringComparison.Ordinal))
            return new OrganizerSessionRestoreResult(false, true, "The Penumbra version changed since this organizer session was saved.", session);

        var currentIds = inventory.Mods.Select(mod => mod.StableScanId).ToHashSet(StringComparer.Ordinal);
        var savedIds = session.Mods.Select(mod => mod.StableScanId).ToHashSet(StringComparer.Ordinal);
        var commonCount = currentIds.Intersect(savedIds, StringComparer.Ordinal).Count();
        var denominator = Math.Max(currentIds.Count, savedIds.Count);
        var matchRatio = denominator == 0 ? 0 : (double)commonCount / denominator;
        if (matchRatio < 0.95)
            return new OrganizerSessionRestoreResult(false, true, "Your mod library changed since this organizer session was saved. Review which proposals can still be restored.", session);

        return new OrganizerSessionRestoreResult(true, false, "A saved organizer session can be resumed.", session);
    }

    public Task DiscardLastSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(LastSessionPath))
            File.Delete(LastSessionPath);
        return Task.CompletedTask;
    }

    public static string BuildScanIdentity(ScanInventory inventory)
    {
        var input = string.Join('\n', inventory.Mods.OrderBy(mod => mod.StableScanId, StringComparer.Ordinal).Select(mod => $"{mod.StableScanId}|{mod.CurrentVirtualFolder}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    public static string BuildInstallationIdentity(PenumbraInstallation installation)
    {
        var input = $"{NormalizeForIdentity(installation.ConfigDirectory)}|{NormalizeForIdentity(installation.ModRoot)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static string NormalizeForIdentity(string path)
        => path.Trim().Replace('\\', '/').ToUpperInvariant();
}
