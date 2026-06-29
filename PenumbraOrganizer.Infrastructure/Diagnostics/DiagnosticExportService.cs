namespace PenumbraOrganizer.Infrastructure.Diagnostics;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PenumbraOrganizer.Core.Interfaces;
using PenumbraOrganizer.Core.Models;

public sealed class DiagnosticExportService : IDiagnosticExportService
{
    public async Task<DiagnosticExportResult> CreateAsync(DiagnosticExportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var baseRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenumbraOrganizer",
            "Diagnostics");
        Directory.CreateDirectory(baseRoot);

        var exportFolder = Path.Combine(baseRoot, $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportFolder);

        var summaryPath = Path.Combine(exportFolder, "diagnostic-summary.json");
        var logsPath = Path.Combine(exportFolder, "sanitized-logs.txt");
        var zipPath = Path.Combine(exportFolder, "PenumbraOrganizer_Diagnostic_Package.zip");
        var includedItems = new[] { "diagnostic-summary.json", "sanitized-logs.txt" };

        var summary = BuildSummary(request);
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(summaryPath, json, new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(logsPath, Sanitize(request.ActivityLog, request.Installation, request.Inventory), new UTF8Encoding(false), cancellationToken);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(summaryPath, Path.GetFileName(summaryPath));
            archive.CreateEntryFromFile(logsPath, Path.GetFileName(logsPath));
        }

        return new DiagnosticExportResult(exportFolder, zipPath, includedItems);
    }

    private static DiagnosticSummaryDocument BuildSummary(DiagnosticExportRequest request)
    {
        var installation = request.Installation;
        var inventory = request.Inventory;

        return new DiagnosticSummaryDocument
        {
            ApplicationVersion = request.ApplicationVersion,
            WindowsVersion = Environment.OSVersion.VersionString,
            PenumbraVersion = installation?.InstalledVersion,
            CompatibilityStatus = request.RealInstallationValidation is null
                ? null
                : request.RealInstallationValidation.AppearsSafeForApply ? "ValidationReady" : "ValidationBlocked",
            Installation = installation is null
                ? null
                : new
                {
                    configurationPath = RedactPath(installation.ConfigurationPath, installation, inventory),
                    configDirectory = RedactPath(installation.ConfigDirectory, installation, inventory),
                    modRoot = RedactPath(installation.ModRoot, installation, inventory),
                    installedVersion = installation.InstalledVersion,
                    warnings = installation.Warnings.Select(w => Sanitize(w, installation, inventory)).ToArray(),
                },
            Validation = request.RealInstallationValidation is null
                ? null
                : new
                {
                    summary = Sanitize(request.RealInstallationValidation.Summary, installation, inventory),
                    report = new
                    {
                        penumbraStateDirectory = RedactPath(request.RealInstallationValidation.Report.PenumbraStateDirectory, installation, inventory),
                        modLibraryRoot = RedactPath(request.RealInstallationValidation.Report.ModLibraryRoot, installation, inventory),
                        request.RealInstallationValidation.Report.InstalledPenumbraVersion,
                        request.RealInstallationValidation.Report.ModsScanned,
                        request.RealInstallationValidation.Report.ProposedChanges,
                        request.RealInstallationValidation.Report.MappedRecords,
                        request.RealInstallationValidation.Report.MissingRecords,
                        request.RealInstallationValidation.Report.AmbiguousRecords,
                        request.RealInstallationValidation.Report.ProtectedMods,
                        request.RealInstallationValidation.Report.UnsupportedRecords,
                        request.RealInstallationValidation.Report.UnsupportedStructures,
                        writableTargetStatus = request.RealInstallationValidation.Report.WritableTargetStatus,
                        gameOrLauncherStatus = request.RealInstallationValidation.Report.GameOrLauncherStatus,
                        backupReadiness = request.RealInstallationValidation.Report.BackupReadiness,
                        rollbackReadiness = request.RealInstallationValidation.Report.RollbackReadiness,
                        request.RealInstallationValidation.Report.ApplyCurrentlySafe,
                    },
                    errors = request.RealInstallationValidation.Errors.Select(e => Sanitize(e, installation, inventory)).ToArray(),
                    warnings = request.RealInstallationValidation.Warnings.Select(w => Sanitize(w, installation, inventory)).ToArray(),
                    schemaFingerprints = request.RealInstallationValidation.Plan.SourceSchemaFingerprints.Select(fp => new
                    {
                        fileName = fp.FileName,
                        fingerprint = fp.Fingerprint,
                        differenceKind = fp.DifferenceKind.ToString(),
                        notes = fp.Notes.Select(note => Sanitize(note, installation, inventory)).ToArray(),
                    }).ToArray(),
                },
            Operations = request.Operations.Select(operation => (object)new
            {
                operationId = operation.OperationId,
                createdAtUtc = operation.CreatedAtUtc,
                backupStatus = operation.BackupStatus.ToString(),
                applyStatus = operation.ApplyStatus.ToString(),
                rollbackStatus = operation.RollbackStatus.ToString(),
                verificationStatus = operation.VerificationStatus.ToString(),
                affectedFileCount = operation.AffectedFileCount,
                affectedModCount = operation.AffectedModCount,
                conflictCount = operation.ConflictCount,
                failureCount = operation.FailureCount,
                operationFolder = Sanitize(operation.OperationFolder, installation, inventory),
                observationStatus = operation.ObservationStatus?.ToString(),
                observationRecordedAtUtc = operation.ObservationRecordedAtUtc,
            }).ToArray(),
        };
    }

    private static string RedactPath(string path, PenumbraInstallation? installation, ScanInventory? inventory)
        => Sanitize(path, installation, inventory);

    private static string Sanitize(string text, PenumbraInstallation? installation, ScanInventory? inventory)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = text.Replace('\\', '/');
        if (installation is not null)
        {
            sanitized = sanitized.Replace(installation.ConfigDirectory.Replace('\\', '/'), "[penumbra-state]", StringComparison.OrdinalIgnoreCase);
            sanitized = sanitized.Replace(installation.ConfigurationPath.Replace('\\', '/'), "[penumbra-config]", StringComparison.OrdinalIgnoreCase);
            sanitized = sanitized.Replace(installation.ModRoot.Replace('\\', '/'), "[mod-library]", StringComparison.OrdinalIgnoreCase);
        }

        if (inventory is not null)
        {
            foreach (var mod in inventory.Mods)
                sanitized = sanitized.Replace(mod.PhysicalDirectory.Replace('\\', '/'), "[mod]", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = sanitized.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/'), "[profile]", StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("sort_order.json", "[state-file]", StringComparison.OrdinalIgnoreCase);
        return sanitized;
    }
}
