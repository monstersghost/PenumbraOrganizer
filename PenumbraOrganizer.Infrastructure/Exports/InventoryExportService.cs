namespace PenumbraOrganizer.Infrastructure.Exports;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class InventoryExportService : IInventoryExportService
{
    private const string InventoryFileName = "Penumbra_Mod_Inventory.json";
    private const string InstructionsFileName = "AI_INSTRUCTIONS.txt";
    private const string HowToUseFileName = "HOW_TO_USE.txt";
    private const string ZipFileName = "Penumbra_AI_Review_Package.zip";
    private static readonly string[] ExpectedZipEntries = [InventoryFileName, InstructionsFileName, HowToUseFileName];
    private readonly ILogger<InventoryExportService> _logger;

    public InventoryExportService(ILogger<InventoryExportService> logger)
    {
        _logger = logger;
    }

    public async Task<InventoryExportResult> CreateAiReviewPackageAsync(
        ScanInventory inventory,
        CancellationToken cancellationToken,
        OrganizationPreferences? organizationPreferences = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        organizationPreferences ??= OrganizationPreferences.DefaultManual;

        var baseRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenumbraOrganizer",
            "Exports");
        Directory.CreateDirectory(baseRoot);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var exportFolder = Path.Combine(baseRoot, $"{timestamp}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportFolder);

        var sourceExportId = $"export-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var inventoryPath = Path.Combine(exportFolder, InventoryFileName);
        var instructionsPath = Path.Combine(exportFolder, InstructionsFileName);
        var howToUsePath = Path.Combine(exportFolder, HowToUseFileName);
        var zipPath = Path.Combine(exportFolder, ZipFileName);

        try
        {
            var exportPayload = BuildInventoryPayload(inventory, sourceExportId, organizationPreferences);
            var json = JsonSerializer.Serialize(exportPayload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(inventoryPath, json, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(instructionsPath, BuildMasterPromptText(), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(howToUsePath, BuildHowToUseText(), new UTF8Encoding(false), cancellationToken);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(inventoryPath, InventoryFileName);
                archive.CreateEntryFromFile(instructionsPath, InstructionsFileName);
                archive.CreateEntryFromFile(howToUsePath, HowToUseFileName);
            }

            await ValidateExportPackageAsync(exportFolder, cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(exportFolder);
            throw;
        }

        _logger.LogInformation("Created AI review export package at {ExportFolder}", exportFolder);

        return new InventoryExportResult(
            exportFolder,
            inventoryPath,
            instructionsPath,
            howToUsePath,
            zipPath,
            sourceExportId,
            inventory.Mods.Count);
    }

    public async Task ValidateExportPackageAsync(string exportFolder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inventoryPath = Path.Combine(exportFolder, InventoryFileName);
        var promptPath = Path.Combine(exportFolder, InstructionsFileName);
        var howToUsePath = Path.Combine(exportFolder, HowToUseFileName);
        var zipPath = Path.Combine(exportFolder, ZipFileName);

        if (!File.Exists(inventoryPath))
            throw new InvalidOperationException("The AI inventory export is missing Penumbra_Mod_Inventory.json.");
        if (!File.Exists(promptPath))
            throw new InvalidOperationException("The AI inventory export is missing AI_INSTRUCTIONS.txt.");
        if (!File.Exists(howToUsePath))
            throw new InvalidOperationException("The AI inventory export is missing HOW_TO_USE.txt.");
        if (!File.Exists(zipPath))
            throw new InvalidOperationException("The AI inventory export is missing Penumbra_AI_Review_Package.zip.");

        await using (var stream = File.OpenRead(inventoryPath))
        {
            var inventory = await JsonSerializer.DeserializeAsync<AiInventoryExport>(stream, cancellationToken: cancellationToken);
            if (inventory is null)
                throw new InvalidOperationException("The exported inventory JSON could not be read.");
            if (inventory.FormatVersion != AiExchangeFormat.CurrentFormatVersion)
                throw new InvalidOperationException("The exported inventory uses an unsupported format version.");
            if (string.IsNullOrWhiteSpace(inventory.SourceExportId))
                throw new InvalidOperationException("The exported inventory is missing sourceExportId.");
            if (inventory.OrganizationPreferences is null)
                throw new InvalidOperationException("The exported inventory is missing organizationPreferences.");
            if (inventory.Mods.Count == 0)
                throw new InvalidOperationException("The exported inventory does not contain any mod rows.");
            if (inventory.Mods.Any(mod => string.IsNullOrWhiteSpace(mod.ScanId)))
                throw new InvalidOperationException("The exported inventory contains a mod row without scanId.");
            if (inventory.Mods.Select(mod => mod.ScanId).Distinct(StringComparer.Ordinal).Count() != inventory.Mods.Count)
                throw new InvalidOperationException("The exported inventory contains duplicate scanId values.");
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (!string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(entry.Name))
                throw new InvalidOperationException("The AI review ZIP contains entries outside the archive root.");
            if (!ExpectedZipEntries.Contains(entry.Name, StringComparer.Ordinal))
                throw new InvalidOperationException($"The AI review ZIP contains unexpected file {entry.Name}.");
        }

        foreach (var required in ExpectedZipEntries)
        {
            var matches = archive.Entries.Where(entry => string.Equals(entry.FullName, required, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"The AI review ZIP must contain exactly one {required}.");

            var standalonePath = Path.Combine(exportFolder, required);
            await using var zipStream = matches[0].Open();
            await using var standaloneStream = File.OpenRead(standalonePath);
            if (!await StreamsEqualAsync(zipStream, standaloneStream, cancellationToken))
                throw new InvalidOperationException($"The zipped {required} does not match the standalone file.");
        }

        var inventoryEntry = archive.GetEntry(InventoryFileName)!;
        await using var inventoryEntryStream = inventoryEntry.Open();
        using var doc = await JsonDocument.ParseAsync(inventoryEntryStream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("formatVersion", out var formatVersion) ||
            formatVersion.GetInt32() != AiExchangeFormat.CurrentFormatVersion)
            throw new InvalidOperationException("The zipped inventory JSON has an unsupported format version.");
    }

    private static AiInventoryExport BuildInventoryPayload(
        ScanInventory inventory,
        string sourceExportId,
        OrganizationPreferences organizationPreferences)
        => new()
        {
            FormatVersion = AiExchangeFormat.CurrentFormatVersion,
            SourceExportId = sourceExportId,
            GeneratedAtUtc = inventory.ScannedAtUtc,
            InstalledPenumbraVersion = inventory.Installation.InstalledVersion,
            OrganizationPreferences = new AiOrganizationPreferences
            {
                Strategy = organizationPreferences.Strategy.ToString(),
                UseTypeFolders = organizationPreferences.UseTypeFolders,
                UseCreatorFolders = organizationPreferences.UseCreatorFolders,
                FolderOrder = organizationPreferences.FolderOrder.Select(component => component.ToString()).ToArray(),
                FixedRootFolder = organizationPreferences.FixedRootFolder,
                PreserveMeaningfulExistingFolders = organizationPreferences.PreserveMeaningfulExistingFolders,
                FlattenTemporarySourceFolders = organizationPreferences.FlattenTemporarySourceFolders,
                NormalizeCreatorAliases = organizationPreferences.NormalizeCreatorAliases,
                UnknownCreatorBehavior = organizationPreferences.UnknownCreatorBehavior.ToString(),
                UnknownTypeBehavior = organizationPreferences.UnknownTypeBehavior.ToString(),
                UncertainClassificationBehavior = organizationPreferences.UncertainClassificationBehavior.ToString(),
                PreserveCurrentFolderWhenUncertain = organizationPreferences.PreserveCurrentFolderWhenUncertain,
                CustomPattern = organizationPreferences.CustomPattern,
            },
            Mods = inventory.Mods.Select(mod => new AiInventoryMod
            {
                ScanId = mod.StableScanId,
                ProtectedRow = mod.Protected,
                CurrentVirtualFolder = mod.CurrentVirtualFolder,
                Name = mod.Name,
                Author = mod.Author,
                Version = mod.Version,
                Website = mod.Website,
                Description = mod.Description,
                Tags = mod.Tags,
                RecognizedMetadataFiles = SanitizePathList(mod.RecognizedMetadataFiles, inventory, mod),
                UnknownMetadataFiles = SanitizePathList(mod.UnknownMetadataFiles, inventory, mod),
                MalformedMetadataFiles = SanitizePathList(mod.MalformedMetadataFiles, inventory, mod),
                CollectionReferenceCount = mod.CollectionStates.Count,
                Warnings = mod.Warnings.Select(warning => SanitizeFreeText(warning, inventory, mod)).ToArray(),
                ContentSignalSummary = SanitizeFreeText(mod.ContentSignalSummary, inventory, mod),
                SchemaFingerprints = mod.SchemaFingerprints
                    .Select(fp => SanitizeSchemaFingerprint(fp, inventory, mod))
                    .Where(fp => fp is not null)
                    .Cast<AiSchemaFingerprint>()
                    .ToArray(),
            }).ToArray(),
        };

    private static AiSchemaFingerprint? SanitizeSchemaFingerprint(
        SchemaFingerprint fingerprint,
        ScanInventory inventory,
        ModScanResult mod)
    {
        var fileName = SanitizePathForExport(fingerprint.FileName, inventory, mod);
        if (fileName is null)
            return null;

        return new AiSchemaFingerprint
        {
            FileName = fileName,
            Fingerprint = fingerprint.Fingerprint,
            DifferenceKind = fingerprint.DifferenceKind.ToString(),
            Notes = fingerprint.Notes.Select(note => SanitizeFreeText(note, inventory, mod)).ToArray(),
        };
    }

    private static IReadOnlyList<string> SanitizePathList(
        IReadOnlyList<string> values,
        ScanInventory inventory,
        ModScanResult mod)
        => values
            .Select(value => SanitizePathForExport(value, inventory, mod))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? SanitizePathForExport(string value, ScanInventory inventory, ModScanResult mod)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        string? relative;
        if (Path.IsPathRooted(trimmed))
        {
            relative = TryGetRelativeWithin(trimmed, mod.PhysicalDirectory) ??
                       TryGetRelativeWithin(trimmed, inventory.Installation.ModRoot);
            if (relative is null)
                return null;
        }
        else
        {
            relative = trimmed;
        }

        relative = relative.Replace('\\', '/');
        while (relative.StartsWith("./", StringComparison.Ordinal))
            relative = relative[2..];

        return IsSafeRelativePath(relative) ? relative : null;
    }

    private static string SanitizeFreeText(string value, ScanInventory inventory, ModScanResult mod)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace(mod.PhysicalDirectory, "[mod]", StringComparison.OrdinalIgnoreCase)
            .Replace(inventory.Installation.ModRoot, "[mod-library]", StringComparison.OrdinalIgnoreCase)
            .Replace(inventory.Installation.ConfigDirectory, "[penumbra-state]", StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[profile]", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/');
    }

    private static string? TryGetRelativeWithin(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return null;

        var fullRoot = Path.GetFullPath(rootPath);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.GetRelativePath(fullRoot, fullCandidate);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.Any(char.IsControl))
            return false;

        var parts = path.Split('/', StringSplitOptions.None);
        return parts.All(part => !string.IsNullOrWhiteSpace(part) && part != "." && part != "..");
    }

    private static async Task<bool> StreamsEqualAsync(Stream left, Stream right, CancellationToken cancellationToken)
    {
        var leftBuffer = new byte[8192];
        var rightBuffer = new byte[8192];
        while (true)
        {
            var leftRead = await left.ReadAsync(leftBuffer, cancellationToken);
            var rightRead = await right.ReadAsync(rightBuffer, cancellationToken);
            if (leftRead != rightRead)
                return false;
            if (leftRead == 0)
                return true;
            for (var i = 0; i < leftRead; i++)
            {
                if (leftBuffer[i] != rightBuffer[i])
                    return false;
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best effort cleanup; preserve the original exception for callers.
        }
    }

    private static string BuildMasterPromptText()
        => """
Upload Penumbra_AI_Review_Package.zip to your AI assistant, then paste the master prompt below.

The AI cannot inspect your inventory unless you upload the exported package. Copying the prompt alone is not enough.

Master prompt:

You are assisting me in reorganizing my Final Fantasy XIV Penumbra mod library.

Read Penumbra_Mod_Inventory.json from the uploaded ZIP and treat that file as the only source of truth about my installed mods, existing virtual folders, detected authors, content signals, protected items, current organization, and organizationPreferences.

Return JSON only. Do not include Markdown fences. Do not include prose outside the JSON.

Use this exact proposal structure:

{
  "formatVersion": 1,
  "sourceExportId": "copy exactly from inventory",
  "generatedBy": {
    "provider": "optional provider name",
    "model": "optional model name"
  },
  "summary": {
    "totalRowsReceived": 0,
    "totalRowsReturned": 0,
    "protectedRows": 0,
    "changedRows": 0,
    "unchangedRows": 0,
    "reviewRows": 0
  },
  "creatorAliases": [
    {
      "original": "Original value",
      "canonical": "Canonical value",
      "confidence": "high",
      "reason": "Brief explanation"
    }
  ],
  "proposals": [
    {
      "scanId": "copy exactly from inventory",
      "protected": false,
      "currentVirtualFolder": "copy exactly from inventory",
      "proposedVirtualFolder": "Creator or Type/Creator",
      "proposedType": null,
      "proposedCreator": null,
      "action": "keep",
      "confidence": "high",
      "reason": "Brief explanation",
      "evidence": [],
      "warnings": []
    }
  ]
}

Contract:
- formatVersion must be 1.
- Copy sourceExportId exactly from the inventory.
- Read organizationPreferences and follow the selected strategy exactly.
- Return every input scanId exactly once.
- Never add unknown scanIds.
- Never omit rows.
- Copy each currentVirtualFolder exactly from the inventory.
- Leave protected rows unchanged.
- Protected rows must use action = keep and confidence = protected.
- Allowed actions are keep, move, and review.
- Allowed confidence values are high, medium, low, and protected.
- Use keep only when currentVirtualFolder and proposedVirtualFolder match.
- Use move only when currentVirtualFolder and proposedVirtualFolder differ.
- Use review for uncertain decisions.
- Never emit physical filesystem paths as proposed virtual folders.
- Never propose deleting, merging, renaming, or moving physical mod directories.
- Never alter Final Fantasy XIV game files.
- Avoid classification dimensions disabled by the selected strategy.

Strategy rules:
- CreatorOnly: creator inference may be performed, type inference is unnecessary, and type folders must not be created.
- TypeOnly: type classification may be performed, creator inference is unnecessary, and creator folders must not be created.
- TypeThenCreator and CreatorThenType: resolve type and creator independently, then construct the final path in the configured order.
- PreserveAndClean: minimize changes, preserve meaningful folders, and flatten only clearly temporary source/import wrappers.
- StartManually: this package normally should not be generated; if generated through Advanced mode, return unchanged rows unless I supplied a separate explicit AI instruction.
- Custom: obey the validated pattern and fixed root, and do not invent unsupported path components.
""";

    private static string BuildHowToUseText()
        => """
1. Open your preferred AI assistant.
2. Start a new conversation.
3. Upload Penumbra_AI_Review_Package.zip.
4. Paste the master prompt.
5. Ask the AI to read Penumbra_Mod_Inventory.json from the uploaded ZIP.
6. Save the AI's JSON response as Penumbra_AI_Proposal.json.
7. Import that JSON into Penumbra Organizer.
8. Review every proposed change before applying it.

Warning:
The AI cannot inspect your inventory unless you upload the exported package. Copying the prompt alone is not enough.
""";
}
