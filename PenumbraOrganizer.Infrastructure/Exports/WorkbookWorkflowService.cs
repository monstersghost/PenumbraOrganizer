namespace PenumbraOrganizer.Infrastructure.Exports;

using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;
using PenumbraOrganizer.Infrastructure.Sessions;

public sealed class WorkbookWorkflowService : IWorkbookWorkflowService
{
    private const string EditableSheetName = "Edit Destinations";
    private const string CategorySheetName = "Category Mapping";
    private const string MetadataSheetName = "_Metadata";
    private readonly ICreatorCanonicalizer _creatorCanonicalizer;
    private readonly ILogger<WorkbookWorkflowService> _logger;

    public WorkbookWorkflowService(ICreatorCanonicalizer creatorCanonicalizer, ILogger<WorkbookWorkflowService> logger)
    {
        _creatorCanonicalizer = creatorCanonicalizer;
        _logger = logger;
    }

    public Task<WorkbookExportResult> ExportAsync(
        ScanInventory inventory,
        OrganizationPreferences organizationPreferences,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exportsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenumbraOrganizer",
            "Exports",
            "Workbook");
        Directory.CreateDirectory(exportsDirectory);

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var sourceExportId = $"workbook-{generatedAtUtc:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var scanIdentity = OrganizerSessionService.BuildScanIdentity(inventory);
        var installationIdentity = OrganizerSessionService.BuildInstallationIdentity(inventory.Installation);
        var workbookPath = Path.Combine(
            exportsDirectory,
            $"PenumbraOrganizer-{generatedAtUtc:yyyy-MM-dd-HHmmss}.xlsx");

        using var workbook = new XLWorkbook();
        BuildEditableSheet(workbook, inventory, organizationPreferences);
        BuildCategorySheet(workbook);
        BuildMetadataSheet(workbook, sourceExportId, generatedAtUtc, scanIdentity, installationIdentity, organizationPreferences);
        workbook.SaveAs(workbookPath);

        var strategyLabel = StrategyLabel(organizationPreferences.Strategy);
        var summary = $"Workbook exported with {inventory.Mods.Count} mod row(s) using {strategyLabel} suggestions.";
        _logger.LogInformation("Exported workbook workflow file to {WorkbookPath}", workbookPath);
        return Task.FromResult(new WorkbookExportResult(
            workbookPath,
            sourceExportId,
            generatedAtUtc,
            scanIdentity,
            installationIdentity,
            strategyLabel,
            inventory.Mods.Count,
            summary));
    }

    public Task<WorkbookImportResult> ImportAsync(
        string workbookPath,
        ScanInventory inventory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(workbookPath))
            throw new InvalidOperationException("The selected workbook could not be found.");

        using var workbook = new XLWorkbook(workbookPath);
        var editable = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, EditableSheetName, StringComparison.Ordinal))
                       ?? throw new InvalidOperationException("The workbook is missing the Edit Destinations sheet.");
        var metadata = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, MetadataSheetName, StringComparison.Ordinal))
                       ?? throw new InvalidOperationException("The workbook is missing its metadata sheet.");

        var meta = ReadMetadata(metadata);
        var rows = new List<WorkbookImportRow>();
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateMetadata(meta, inventory, errors);
        var metadataErrorCount = errors.Count;

        var inventoryById = inventory.Mods.ToDictionary(mod => mod.StableScanId, StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var headerMap = ReadHeaderMap(editable);
        var requiredHeaders = new[] { "id", "mod name", "author", "current folder", "detected type", "protected", "destination" };
        foreach (var header in requiredHeaders)
        {
            if (!headerMap.ContainsKey(header))
                errors.Add($"The workbook is missing the required column '{header}'.");
        }

        if (errors.Count > metadataErrorCount)
            return Task.FromResult(BuildImportResult(workbookPath, meta, rows, errors, warnings));

        var lastRow = editable.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stableScanId = editable.Cell(rowNumber, headerMap["id"]).GetString().Trim();
            var modName = editable.Cell(rowNumber, headerMap["mod name"]).GetString().Trim();
            var author = editable.Cell(rowNumber, headerMap["author"]).GetString().Trim();
            var currentFolder = editable.Cell(rowNumber, headerMap["current folder"]).GetString();
            var detectedType = editable.Cell(rowNumber, headerMap["detected type"]).GetString().Trim();
            var protectedRaw = editable.Cell(rowNumber, headerMap["protected"]).GetString().Trim();
            var destination = editable.Cell(rowNumber, headerMap["destination"]).GetString().Trim();

            if (string.IsNullOrWhiteSpace(stableScanId) &&
                string.IsNullOrWhiteSpace(modName) &&
                string.IsNullOrWhiteSpace(author) &&
                string.IsNullOrWhiteSpace(currentFolder) &&
                string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(stableScanId))
            {
                errors.Add($"Row {rowNumber} is missing the stable internal id.");
                continue;
            }

            if (!seenIds.Add(stableScanId))
            {
                errors.Add($"Duplicate id detected in the workbook: {stableScanId}.");
                continue;
            }

            if (!inventoryById.TryGetValue(stableScanId, out var mod))
            {
                errors.Add($"The workbook references an unknown mod id: {stableScanId}.");
                continue;
            }

            if (!string.Equals(currentFolder, mod.CurrentVirtualFolder, StringComparison.Ordinal))
            {
                errors.Add($"The current folder for {stableScanId} changed after export. Scan and export a new workbook before importing edits.");
                continue;
            }

            if (!TryParseProtected(protectedRaw, mod.Protected, out var protectedValue))
            {
                errors.Add($"The protected value for {stableScanId} is invalid. Use TRUE or FALSE.");
                continue;
            }

            if (!TryResolveDestination(destination, out var resolvedDestination, out var destinationError))
            {
                errors.Add($"The destination for {stableScanId} is invalid: {destinationError}");
                continue;
            }

            if (protectedValue && resolvedDestination is not null && !string.Equals(resolvedDestination, mod.CurrentVirtualFolder, StringComparison.Ordinal))
            {
                errors.Add($"Protected mod {stableScanId} cannot also move to a different destination.");
                continue;
            }

            rows.Add(new WorkbookImportRow(
                stableScanId,
                string.IsNullOrWhiteSpace(modName) ? mod.Name : modName,
                string.IsNullOrWhiteSpace(author) ? mod.Author : author,
                currentFolder,
                string.IsNullOrWhiteSpace(detectedType) ? WorkbookCategoryCatalog.Detect(mod).Name : detectedType,
                protectedValue,
                destination,
                resolvedDestination));
        }

        return Task.FromResult(BuildImportResult(workbookPath, meta, rows, errors, warnings));
    }

    private void BuildEditableSheet(XLWorkbook workbook, ScanInventory inventory, OrganizationPreferences organizationPreferences)
    {
        var sheet = workbook.Worksheets.Add(EditableSheetName);
        var headers = new[] { "id", "mod name", "author", "current folder", "detected type", "protected", "destination" };
        for (var index = 0; index < headers.Length; index++)
            sheet.Cell(1, index + 1).Value = headers[index];

        var row = 2;
        foreach (var mod in inventory.Mods.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var category = WorkbookCategoryCatalog.Detect(mod);
            sheet.Cell(row, 1).Value = mod.StableScanId;
            sheet.Cell(row, 2).Value = mod.Name;
            sheet.Cell(row, 3).Value = mod.Author;
            sheet.Cell(row, 4).Value = mod.CurrentVirtualFolder;
            sheet.Cell(row, 5).Value = category.Name;
            sheet.Cell(row, 6).Value = mod.Protected ? "TRUE" : "FALSE";
            sheet.Cell(row, 7).Value = BuildSuggestedDestination(mod, category, organizationPreferences);
            row++;
        }

        var range = sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length);
        range.CreateTable();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void BuildCategorySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add(CategorySheetName);
        sheet.Cell(1, 1).Value = "This sheet explains category shorthand and the blank-destination rule.";
        sheet.Cell(2, 1).Value = WorkbookCategoryCatalog.BlankDestinationRule;
        sheet.Cell(4, 1).Value = "code";
        sheet.Cell(4, 2).Value = "name";
        sheet.Cell(4, 3).Value = "description";
        sheet.Cell(4, 4).Value = "example destination";

        var row = 5;
        foreach (var category in WorkbookCategoryCatalog.Definitions)
        {
            sheet.Cell(row, 1).Value = category.Code;
            sheet.Cell(row, 2).Value = category.Name;
            sheet.Cell(row, 3).Value = category.Description;
            sheet.Cell(row, 4).Value = category.ExampleDestination;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildMetadataSheet(
        XLWorkbook workbook,
        string sourceExportId,
        DateTimeOffset generatedAtUtc,
        string scanIdentity,
        string installationIdentity,
        OrganizationPreferences organizationPreferences)
    {
        var sheet = workbook.Worksheets.Add(MetadataSheetName);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formatVersion"] = WorkbookCategoryCatalog.CurrentFormatVersion.ToString(),
            ["sourceExportId"] = sourceExportId,
            ["generatedAtUtc"] = generatedAtUtc.ToString("O"),
            ["scanIdentity"] = scanIdentity,
            ["installationIdentity"] = installationIdentity,
            ["strategy"] = organizationPreferences.Strategy.ToString(),
            ["blankDestinationRule"] = WorkbookCategoryCatalog.BlankDestinationRule,
        };

        var row = 1;
        foreach (var pair in values)
        {
            sheet.Cell(row, 1).Value = pair.Key;
            sheet.Cell(row, 2).Value = pair.Value;
            row++;
        }

        sheet.Visibility = XLWorksheetVisibility.VeryHidden;
    }

    private Dictionary<string, string> ReadMetadata(IXLWorksheet sheet)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= lastRow; row++)
        {
            var key = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            values[key] = sheet.Cell(row, 2).GetString().Trim();
        }

        return values;
    }

    private static Dictionary<string, int> ReadHeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var column = 1; column <= lastColumn; column++)
        {
            var name = sheet.Cell(1, column).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(name))
                map[name] = column;
        }

        return map;
    }

    private static void ValidateMetadata(Dictionary<string, string> meta, ScanInventory inventory, List<string> errors)
    {
        if (!meta.TryGetValue("formatVersion", out var formatVersion) ||
            !int.TryParse(formatVersion, out var parsedVersion) ||
            parsedVersion != WorkbookCategoryCatalog.CurrentFormatVersion)
        {
            errors.Add("The workbook format version is unsupported.");
        }

        var currentInstallationIdentity = OrganizerSessionService.BuildInstallationIdentity(inventory.Installation);
        if (!meta.TryGetValue("installationIdentity", out var installationIdentity) ||
            !string.Equals(installationIdentity, currentInstallationIdentity, StringComparison.Ordinal))
        {
            errors.Add("This workbook belongs to a different Penumbra library.");
        }

        var currentScanIdentity = OrganizerSessionService.BuildScanIdentity(inventory);
        if (!meta.TryGetValue("scanIdentity", out var scanIdentity) ||
            !string.Equals(scanIdentity, currentScanIdentity, StringComparison.Ordinal))
        {
            errors.Add("The library changed after this workbook was exported. Scan and export a new workbook before importing edits.");
        }
    }

    private WorkbookImportResult BuildImportResult(
        string workbookPath,
        Dictionary<string, string> meta,
        IReadOnlyList<WorkbookImportRow> rows,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        meta.TryGetValue("sourceExportId", out var sourceExportId);
        meta.TryGetValue("scanIdentity", out var scanIdentity);
        meta.TryGetValue("installationIdentity", out var installationIdentity);
        meta.TryGetValue("generatedAtUtc", out var generatedAtUtcRaw);
        DateTimeOffset.TryParse(generatedAtUtcRaw, out var generatedAtUtc);

        var changedCount = rows.Count(row => row.ResolvedDestination is not null && !string.Equals(row.ResolvedDestination, row.CurrentVirtualFolder, StringComparison.Ordinal));
        var summary = errors.Count > 0
            ? $"Workbook import blocked. {errors.Count} validation issue(s) were found."
            : $"Workbook imported successfully. {changedCount} mod destination change(s) are ready for review.";

        return new WorkbookImportResult(
            workbookPath,
            sourceExportId ?? string.Empty,
            generatedAtUtc,
            scanIdentity ?? string.Empty,
            installationIdentity ?? string.Empty,
            rows,
            errors,
            warnings,
            summary);
    }

    private string BuildSuggestedDestination(ModScanResult mod, WorkbookCategoryDefinition category, OrganizationPreferences organizationPreferences)
    {
        if (mod.Protected)
            return string.Empty;

        var creator = BuildCreatorFolder(mod.Author);
        var code = category.Code.ToString();
        return organizationPreferences.Strategy switch
        {
            OrganizationStrategy.CreatorOnly => string.IsNullOrWhiteSpace(creator) ? WorkbookCategoryCatalog.Definitions[7].Code.ToString() : creator,
            OrganizationStrategy.TypeOnly => code,
            OrganizationStrategy.TypeThenCreator => string.IsNullOrWhiteSpace(creator) ? code : $"{code}/{creator}",
            OrganizationStrategy.CreatorThenType => string.IsNullOrWhiteSpace(creator) ? code : $"{creator}/{code}",
            OrganizationStrategy.PreserveAndClean => NormalizeRelativePath(mod.CurrentVirtualFolder),
            OrganizationStrategy.Custom => string.IsNullOrWhiteSpace(creator) ? code : $"{code}/{creator}",
            _ => string.Empty,
        };
    }

    private string BuildCreatorFolder(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return string.Empty;

        var canonical = _creatorCanonicalizer.Canonicalize(author).Trim();
        return string.Join(
            " ",
            canonical
                .Split(Path.GetInvalidFileNameChars().Append('/').Append('\\').Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static bool TryParseProtected(string raw, bool fallback, out bool value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
            return true;
        }

        if (bool.TryParse(raw, out value))
            return true;

        if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw == "1")
        {
            value = true;
            return true;
        }

        if (raw.Equals("no", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            value = false;
            return true;
        }

        value = fallback;
        return false;
    }

    private static bool TryResolveDestination(string raw, out string? resolvedDestination, out string error)
    {
        resolvedDestination = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = string.Empty;
            return true;
        }

        var normalized = NormalizeRelativePath(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Destination is empty.";
            return false;
        }

        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal) || normalized.Any(char.IsControl))
        {
            error = "Use a relative Penumbra folder path.";
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.None);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            error = "Destination cannot contain empty segments, . or ..";
            return false;
        }

        if (int.TryParse(parts[0], out var code))
        {
            if (!WorkbookCategoryCatalog.TryGetByCode(code, out var category))
            {
                error = $"Category code {code} is not defined in the workbook mapping sheet.";
                return false;
            }

            parts[0] = category.Name;
        }
        else if (WorkbookCategoryCatalog.TryGetByName(parts[0], out var namedCategory))
        {
            parts[0] = namedCategory.Name;
        }

        resolvedDestination = string.Join('/', parts);
        error = string.Empty;
        return true;
    }

    private static string NormalizeRelativePath(string path)
        => path.Trim().Replace('\\', '/').Trim('/');

    private static string StrategyLabel(OrganizationStrategy strategy)
        => strategy switch
        {
            OrganizationStrategy.CreatorOnly => "creator",
            OrganizationStrategy.TypeOnly => "mod type",
            OrganizationStrategy.TypeThenCreator => "type/author",
            OrganizationStrategy.CreatorThenType => "author/type",
            OrganizationStrategy.PreserveAndClean => "preserve and clean",
            OrganizationStrategy.Custom => "custom",
            _ => "manual",
        };
}
