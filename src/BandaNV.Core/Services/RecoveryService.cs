using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed record StartupRecoveryResult(
    int RecoveredExecutions,
    int ReconciledMoves,
    int InterruptedItems,
    int TemporaryFilesDeleted);

public sealed class RecoveryService
{
    private const long TimestampToleranceTicks = 20_000_000L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<StartupRecoveryResult> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => RecoverCoreAsync(cancellationToken),
            cancellationToken);
    }

    private static async Task<StartupRecoveryResult> RecoverCoreAsync(
        CancellationToken cancellationToken)
    {
        PortablePaths.EnsureDirectories();

        var entries = await LoadRecordsAsync(cancellationToken);
        var byExecutionId = entries
            .GroupBy(
                entry => entry.Record.ExecutionId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var recoveredExecutions = 0;
        var reconciledMoves = 0;
        var interruptedItems = 0;
        var temporaryFilesDeleted = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = entry.Record;

            if (record.Status != OrganizationExecutionStatus.Running &&
                !record.Items.Any(item =>
                    item.Status == OrganizationExecutionItemStatus.Moving))
            {
                continue;
            }

            var stats = record.Type.Equals(
                    "UNDO",
                    StringComparison.OrdinalIgnoreCase)
                ? await RecoverUndoAsync(
                    record,
                    byExecutionId,
                    cancellationToken)
                : await RecoverOrganizationAsync(
                    record,
                    cancellationToken);

            FinalizeRecoveredRecord(record, stats);

            await PersistRecoveredRecordAsync(
                record,
                entry.HistoryPath,
                cancellationToken);

            recoveredExecutions++;
            reconciledMoves += stats.ReconciledMoves;
            interruptedItems += stats.InterruptedItems;
            temporaryFilesDeleted += stats.TemporaryFilesDeleted;
        }

        return new StartupRecoveryResult(
            recoveredExecutions,
            reconciledMoves,
            interruptedItems,
            temporaryFilesDeleted);
    }

    private static async Task<List<RecoveryEntry>> LoadRecordsAsync(
        CancellationToken cancellationToken)
    {
        var records = new List<RecoveryEntry>();

        foreach (var historyPath in Directory.EnumerateFiles(
                     PortablePaths.HistoryDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    historyPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                var record =
                    await JsonSerializer.DeserializeAsync<OrganizationExecutionRecord>(
                        stream,
                        JsonOptions,
                        cancellationToken);

                if (record is null ||
                    !record.Format.StartsWith(
                        "BandaNV.Execution.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                records.Add(new RecoveryEntry(record, historyPath));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Un journal dañado no se toca automáticamente.
            }
        }

        return records;
    }

    private static async Task<RecoveryStats> RecoverOrganizationAsync(
        OrganizationExecutionRecord record,
        CancellationToken cancellationToken)
    {
        var stats =
            new RecoveryStats();

        foreach (var item in record.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Status ==
                OrganizationExecutionItemStatus.Planned)
            {
                MarkInterrupted(
                    item,
                    item.IsDirectory
                        ? "La aplicación se cerró antes de que esta carpeta comenzara a moverse."
                        : "La aplicación se cerró antes de que este archivo comenzara a moverse.");

                stats.InterruptedItems++;
                continue;
            }

            if (item.Status !=
                OrganizationExecutionItemStatus.Moving)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    item.FinalPath) ||
                !IsSameOrInside(
                    item.OriginalPath,
                    record.SourceFolder) ||
                !IsSameOrInside(
                    item.FinalPath,
                    record.DestinationFolder))
            {
                item.Status =
                    OrganizationExecutionItemStatus.Error;
                item.Message =
                    "La ejecución se interrumpió y las rutas registradas no permiten una recuperación automática segura.";

                stats.InterruptedItems++;
                continue;
            }

            var sourcePath =
                Path.GetFullPath(
                    item.OriginalPath);

            var targetPath =
                Path.GetFullPath(
                    item.FinalPath);

            var backupPath =
                GetReplacementBackupCandidate(
                    record,
                    item,
                    targetPath);

            var sourceExists =
                OrganizationEntrySafety.Exists(
                    sourcePath);

            var targetExists =
                OrganizationEntrySafety.Exists(
                    targetPath);

            var backupExists =
                !string.IsNullOrWhiteSpace(
                    backupPath) &&
                OrganizationEntrySafety.Exists(
                    backupPath);

            if (sourceExists &&
                targetExists)
            {
                if (OrganizationEntrySafety.MatchesExpected(
                        sourcePath,
                        item) &&
                    OrganizationEntrySafety.MatchesExpected(
                        targetPath,
                        item) &&
                    await OrganizationEntrySafety.AreIdenticalAsync(
                        sourcePath,
                        targetPath,
                        item,
                        cancellationToken))
                {
                    if (OrganizationEntrySafety.TryDeleteEntry(
                            sourcePath))
                    {
                        if (backupExists)
                        {
                            item.ReplacedBackupPath =
                                backupPath;
                        }

                        item.Status =
                            OrganizationExecutionItemStatus.Moved;

                        item.Message =
                            item.IsDirectory
                                ? "Recuperado al iniciar: la carpeta ya se había copiado y verificado correctamente; se eliminó la copia duplicada del origen."
                                : "Recuperado al iniciar: el movimiento había terminado, pero faltaba eliminar la copia de origen.";

                        stats.ReconciledMoves++;
                    }
                    else
                    {
                        MarkInterrupted(
                            item,
                            "Se detectaron dos copias idénticas después del cierre, pero no se pudo eliminar de forma segura la copia de origen.");

                        stats.InterruptedItems++;
                    }
                }
                else
                {
                    MarkInterrupted(
                        item,
                        "Se encontraron elementos tanto en origen como en destino, pero no se pudo demostrar que sean la misma copia analizada. BandaNV no modificó ninguno.");

                    stats.InterruptedItems++;
                }
            }
            else if (sourceExists)
            {
                if (backupExists &&
                    !targetExists)
                {
                    try
                    {
                        OrganizationEntrySafety.MoveEntrySafely(
                            backupPath!,
                            targetPath,
                            cancellationToken);

                        item.ReplacedBackupPath =
                            null;
                    }
                    catch (Exception ex)
                    {
                        item.Status =
                            OrganizationExecutionItemStatus.Error;

                        item.Message =
                            $"La ejecución se interrumpió antes de mover el elemento nuevo y no se pudo restaurar el elemento reemplazado: {ex.Message}";

                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            OrganizationEntrySafety.CleanupTemporaryEntries(
                                targetPath);

                        continue;
                    }
                }

                MarkInterrupted(
                    item,
                    backupExists
                        ? "La ejecución se interrumpió antes de completar el movimiento. El elemento anterior del destino fue restaurado y el nuevo permanece en origen."
                        : "La ejecución se interrumpió antes de completar el movimiento. El elemento permanece en origen.");

                stats.InterruptedItems++;
            }
            else if (targetExists)
            {
                if (OrganizationEntrySafety.MatchesExpected(
                        targetPath,
                        item))
                {
                    if (backupExists)
                    {
                        item.ReplacedBackupPath =
                            backupPath;
                    }

                    item.Status =
                        OrganizationExecutionItemStatus.Moved;

                    item.Message =
                        item.IsDirectory
                            ? "Recuperado al iniciar: la carpeta ya se encontraba completa y validada en destino."
                            : "Recuperado al iniciar: el archivo ya se encontraba correctamente en destino.";

                    stats.ReconciledMoves++;
                }
                else
                {
                    item.Status =
                        OrganizationExecutionItemStatus.Error;

                    item.Message =
                        "La ejecución se interrumpió y el elemento encontrado en destino no coincide con el que se había analizado. No se modificó.";

                    stats.InterruptedItems++;
                }
            }
            else
            {
                if (backupExists)
                {
                    try
                    {
                        OrganizationEntrySafety.MoveEntrySafely(
                            backupPath!,
                            targetPath,
                            cancellationToken);

                        item.ReplacedBackupPath =
                            null;
                    }
                    catch (Exception ex)
                    {
                        item.Status =
                            OrganizationExecutionItemStatus.Error;

                        item.Message =
                            $"No se encontró el elemento nuevo y tampoco se pudo restaurar el elemento anterior del destino: {ex.Message}";

                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            OrganizationEntrySafety.CleanupTemporaryEntries(
                                targetPath);

                        continue;
                    }
                }

                item.Status =
                    OrganizationExecutionItemStatus.SourceMissing;

                item.Message =
                    backupExists
                        ? "La ejecución se interrumpió y el elemento nuevo ya no estaba disponible. El elemento anterior del destino fue restaurado. Los temporales asociados se conservaron por seguridad."
                        : "La ejecución se interrumpió y no se encontró el elemento ni en origen ni en destino. Los temporales asociados se conservaron por seguridad.";

                stats.InterruptedItems++;

                // Si faltan ambas copias principales, un temporal puede ser
                // la única copia restante. No se elimina automáticamente.
                continue;
            }

            stats.TemporaryFilesDeleted +=
                OrganizationEntrySafety.CleanupTemporaryEntries(
                    targetPath);
        }

        return stats;
    }

    private static async Task<RecoveryStats> RecoverUndoAsync(
        OrganizationExecutionRecord undoRecord,
        IReadOnlyDictionary<string, RecoveryEntry> recordsByExecutionId,
        CancellationToken cancellationToken)
    {
        var stats =
            new RecoveryStats();

        var relatedExecutionId =
            ResolveRelatedExecutionId(
                undoRecord);

        if (string.IsNullOrWhiteSpace(
                relatedExecutionId) ||
            !recordsByExecutionId.TryGetValue(
                relatedExecutionId,
                out var originalEntry) ||
            !originalEntry.Record.Type.Equals(
                "ORGANIZE",
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in undoRecord.Items.Where(item =>
                         item.Status is
                             OrganizationExecutionItemStatus.Planned or
                             OrganizationExecutionItemStatus.Moving))
            {
                MarkInterrupted(
                    item,
                    "El Undo se interrumpió y no se pudo identificar con certeza su ejecución original. BandaNV no modificó elementos durante la recuperación.");

                stats.InterruptedItems++;
            }

            return stats;
        }

        undoRecord.RelatedExecutionId =
            relatedExecutionId;

        var originalRecord =
            originalEntry.Record;

        foreach (var undoItem in undoRecord.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (undoItem.Status ==
                OrganizationExecutionItemStatus.Planned)
            {
                MarkInterrupted(
                    undoItem,
                    undoItem.IsDirectory
                        ? "La aplicación se cerró antes de que comenzara la restauración de esta carpeta."
                        : "La aplicación se cerró antes de que comenzara la restauración de este archivo.");

                stats.InterruptedItems++;
                continue;
            }

            if (undoItem.Status !=
                OrganizationExecutionItemStatus.Moving)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    undoItem.FinalPath) ||
                !IsSameOrInside(
                    undoItem.OriginalPath,
                    undoRecord.SourceFolder) ||
                !IsSameOrInside(
                    undoItem.FinalPath,
                    undoRecord.DestinationFolder))
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Error;

                undoItem.Message =
                    "El Undo se interrumpió y las rutas registradas no permiten una recuperación automática segura.";

                stats.InterruptedItems++;
                continue;
            }

            var originalItem =
                ResolveOriginalUndoItem(
                    originalRecord,
                    undoItem);

            if (originalItem is null)
            {
                MarkInterrupted(
                    undoItem,
                    "El Undo se interrumpió y no se pudo vincular este elemento con un movimiento original de forma inequívoca.");

                stats.InterruptedItems++;
                continue;
            }

            var organizedPath =
                Path.GetFullPath(
                    undoItem.OriginalPath);

            var restoredPath =
                Path.GetFullPath(
                    undoItem.FinalPath);

            var backupPath =
                GetSafeRecordedBackupPath(
                    originalItem.ReplacedBackupPath);

            var replacementWasUsed =
                !string.IsNullOrWhiteSpace(
                    originalItem.ReplacedBackupPath);

            var organizedExists =
                OrganizationEntrySafety.Exists(
                    organizedPath);

            var restoredExists =
                OrganizationEntrySafety.Exists(
                    restoredPath);

            var backupExists =
                replacementWasUsed &&
                OrganizationEntrySafety.Exists(
                    backupPath);

            if (!restoredExists)
            {
                if (organizedExists)
                {
                    MarkInterrupted(
                        undoItem,
                        "El Undo se interrumpió antes de completar la restauración. El elemento organizado permanece en destino.");
                }
                else
                {
                    undoItem.Status =
                        OrganizationExecutionItemStatus.SourceMissing;

                    undoItem.Message =
                        "El Undo se interrumpió y el elemento ya no está disponible ni en la ubicación organizada ni en su ubicación original. Los temporales asociados se conservaron por seguridad.";

                    stats.InterruptedItems++;
                    continue;
                }

                stats.InterruptedItems++;
            }
            else if (!OrganizationEntrySafety.MatchesExpected(
                         restoredPath,
                         undoItem))
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Error;

                undoItem.Message =
                    "El elemento presente en la ubicación original no coincide con el que se estaba restaurando. BandaNV no modificó nada.";

                stats.InterruptedItems++;
            }
            else if (!replacementWasUsed)
            {
                if (!organizedExists)
                {
                    undoItem.Status =
                        OrganizationExecutionItemStatus.Moved;

                    undoItem.Message =
                        "Recuperado al iniciar: el elemento ya había sido restaurado a su ubicación original.";

                    stats.ReconciledMoves++;
                }
                else if (OrganizationEntrySafety.MatchesExpected(
                             organizedPath,
                             undoItem) &&
                         await OrganizationEntrySafety.AreIdenticalAsync(
                             organizedPath,
                             restoredPath,
                             undoItem,
                             cancellationToken) &&
                         OrganizationEntrySafety.TryDeleteEntry(
                             organizedPath))
                {
                    undoItem.Status =
                        OrganizationExecutionItemStatus.Moved;

                    undoItem.Message =
                        "Recuperado al iniciar: la restauración había terminado y se eliminó la copia duplicada que quedó en destino.";

                    stats.ReconciledMoves++;
                }
                else
                {
                    MarkInterrupted(
                        undoItem,
                        "Se encontraron elementos en ambas ubicaciones y no se pudo demostrar que fueran la misma copia. BandaNV no modificó ninguno.");

                    stats.InterruptedItems++;
                }
            }
            else if (backupExists)
            {
                if (organizedExists)
                {
                    if (!OrganizationEntrySafety.MatchesExpected(
                            organizedPath,
                            undoItem) ||
                        !await OrganizationEntrySafety.AreIdenticalAsync(
                            organizedPath,
                            restoredPath,
                            undoItem,
                            cancellationToken) ||
                        !OrganizationEntrySafety.TryDeleteEntry(
                            organizedPath))
                    {
                        MarkInterrupted(
                            undoItem,
                            "El elemento nuevo quedó presente en ambas ubicaciones y no pudo reconciliarse de forma segura. El backup protegido se conserva.");

                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            OrganizationEntrySafety.CleanupTemporaryEntries(
                                restoredPath);
                        stats.TemporaryFilesDeleted +=
                            OrganizationEntrySafety.CleanupTemporaryEntries(
                                organizedPath);

                        continue;
                    }
                }

                if (!OrganizationEntrySafety.MatchesReplacement(
                        backupPath!,
                        originalItem))
                {
                    undoItem.Status =
                        OrganizationExecutionItemStatus.Error;

                    undoItem.Message =
                        "La copia protegida del elemento reemplazado no coincide con el registro original. BandaNV no realizó cambios adicionales.";

                    stats.InterruptedItems++;
                }
                else
                {
                    try
                    {
                        OrganizationEntrySafety.MoveEntrySafely(
                            backupPath!,
                            organizedPath,
                            cancellationToken);

                        undoItem.Status =
                            OrganizationExecutionItemStatus.Moved;

                        undoItem.Message =
                            "Recuperado al iniciar: se completó la restauración y se repuso el elemento que había sido reemplazado.";

                        stats.ReconciledMoves++;
                    }
                    catch (Exception ex)
                    {
                        undoItem.Status =
                            OrganizationExecutionItemStatus.Error;

                        undoItem.Message =
                            $"El elemento nuevo fue restaurado, pero no se pudo reponer el elemento reemplazado desde su backup protegido: {ex.Message}";

                        stats.InterruptedItems++;
                    }
                }
            }
            else if (organizedExists &&
                     OrganizationEntrySafety.MatchesReplacement(
                         organizedPath,
                         originalItem))
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Moved;

                undoItem.Message =
                    "Recuperado al iniciar: el Undo ya había restaurado el elemento nuevo y repuesto correctamente el elemento reemplazado.";

                stats.ReconciledMoves++;
            }
            else
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Error;

                undoItem.Message =
                    "El elemento nuevo fue restaurado, pero no se pudo comprobar que el elemento reemplazado también haya sido repuesto. BandaNV no realizó cambios adicionales.";

                stats.InterruptedItems++;
            }

            stats.TemporaryFilesDeleted +=
                OrganizationEntrySafety.CleanupTemporaryEntries(
                    restoredPath);

            stats.TemporaryFilesDeleted +=
                OrganizationEntrySafety.CleanupTemporaryEntries(
                    organizedPath);
        }

        return stats;
    }

    private static OrganizationExecutionItemRecord? ResolveOriginalUndoItem(
        OrganizationExecutionRecord originalRecord,
        OrganizationExecutionItemRecord undoItem)
    {
        if (!string.IsNullOrWhiteSpace(
                undoItem.RelatedItemUndoId))
        {
            var linked =
                originalRecord.Items.FirstOrDefault(item =>
                    item.UndoId.Equals(
                        undoItem.RelatedItemUndoId,
                        StringComparison.OrdinalIgnoreCase));

            if (linked is not null)
            {
                return linked;
            }
        }

        return originalRecord.Items.FirstOrDefault(item =>
            item.Status ==
                OrganizationExecutionItemStatus.Moved &&
            item.Kind ==
                undoItem.Kind &&
            PathsEqual(
                item.OriginalPath,
                undoItem.FinalPath ?? string.Empty) &&
            item.SizeBytes ==
                undoItem.SizeBytes &&
            item.ModifiedUtcTicks ==
                undoItem.ModifiedUtcTicks);
    }

    private static void FinalizeRecoveredRecord(
        OrganizationExecutionRecord record,
        RecoveryStats stats)
    {
        var recoveredAt = DateTime.Now;

        record.RecoveredAt = recoveredAt;
        record.FinishedAt ??= recoveredAt;

        var hasIssues = record.Items.Any(item =>
            item.Status is OrganizationExecutionItemStatus.Error or
                OrganizationExecutionItemStatus.SourceMissing or
                OrganizationExecutionItemStatus.SourceChanged or
                OrganizationExecutionItemStatus.SkippedConflict or
                OrganizationExecutionItemStatus.ConflictNeedsDecision or
                OrganizationExecutionItemStatus.Interrupted or
                OrganizationExecutionItemStatus.Planned or
                OrganizationExecutionItemStatus.Moving);

        record.Status = hasIssues
            ? OrganizationExecutionStatus.CompletedWithIssues
            : OrganizationExecutionStatus.Completed;

        var parts = new List<string>();

        if (stats.ReconciledMoves > 0)
        {
            parts.Add(
                $"{stats.ReconciledMoves} movimiento{(stats.ReconciledMoves == 1 ? string.Empty : "s")} reconciliado{(stats.ReconciledMoves == 1 ? string.Empty : "s")}");
        }

        if (stats.InterruptedItems > 0)
        {
            parts.Add(
                $"{stats.InterruptedItems} elemento{(stats.InterruptedItems == 1 ? string.Empty : "s")} marcado{(stats.InterruptedItems == 1 ? string.Empty : "s")} como interrumpido o con incidencia");
        }

        if (stats.TemporaryFilesDeleted > 0)
        {
            parts.Add(
                $"{stats.TemporaryFilesDeleted} temporal{(stats.TemporaryFilesDeleted == 1 ? string.Empty : "es")} huérfano{(stats.TemporaryFilesDeleted == 1 ? string.Empty : "s")} eliminado{(stats.TemporaryFilesDeleted == 1 ? string.Empty : "s")}");
        }

        record.RecoveryMessage = parts.Count == 0
            ? "La ejecución había quedado abierta y fue cerrada de forma segura al iniciar BandaNV."
            : "Recuperación automática: " + string.Join("; ", parts) + ".";
    }

    private static string? ResolveRelatedExecutionId(
        OrganizationExecutionRecord undoRecord)
    {
        if (!string.IsNullOrWhiteSpace(
                undoRecord.RelatedExecutionId))
        {
            return undoRecord.RelatedExecutionId;
        }

        var candidates = undoRecord.Items
            .Select(item => item.Message)
            .Where(message =>
                !string.IsNullOrWhiteSpace(message) &&
                message.StartsWith(
                    "Undo de ",
                    StringComparison.OrdinalIgnoreCase))
            .Select(message =>
                message!["Undo de ".Length..].Trim())
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count == 1
            ? candidates[0]
            : null;
    }

    private static string? GetReplacementBackupCandidate(
        OrganizationExecutionRecord record,
        OrganizationExecutionItemRecord item,
        string targetPath)
    {
        var recordedBackupPath = GetSafeRecordedBackupPath(
            item.ReplacedBackupPath);

        if (!string.IsNullOrWhiteSpace(recordedBackupPath))
        {
            return recordedBackupPath;
        }

        var candidate = Path.Combine(
            PortablePaths.HistoryDirectory,
            "replaced",
            record.ExecutionId,
            $"{item.UndoId}_{Path.GetFileName(targetPath)}");

        return OrganizationEntrySafety.Exists(
                candidate)
            ? candidate
            : null;
    }

    private static string? GetSafeRecordedBackupPath(
        string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(backupPath);
            var replacedRoot = Path.Combine(
                PortablePaths.HistoryDirectory,
                "replaced");

            return IsSameOrInside(fullPath, replacedRoot)
                ? fullPath
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task PersistRecoveredRecordAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            record,
            JsonOptions);

        await WriteTextAtomicAsync(
            historyPath,
            json,
            cancellationToken);

        var logPath = Path.Combine(
            PortablePaths.LogsDirectory,
            Path.GetFileNameWithoutExtension(historyPath) +
            ".txt");

        await WriteTextAtomicAsync(
            logPath,
            OrganizationExecutionService.FormatHumanLog(record),
            cancellationToken);
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        var temporaryPath =
            path + $".tmp_recovery_{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static bool TryDeleteFile(
        string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return true;
            }

            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static void MarkInterrupted(
        OrganizationExecutionItemRecord item,
        string message)
    {
        item.Status =
            OrganizationExecutionItemStatus.Interrupted;
        item.Message = message;
    }

    private static bool IsSameOrInside(
        string candidate,
        string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var fullCandidate =
            NormalizeDirectoryPath(candidate);
        var fullRoot =
            NormalizeDirectoryPath(root);

        return fullCandidate.Equals(
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string left,
        string right) =>
        NormalizeDirectoryPath(left).Equals(
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectoryPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    path.Trim()))
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    private sealed record RecoveryEntry(
        OrganizationExecutionRecord Record,
        string HistoryPath);

    private sealed class RecoveryStats
    {
        public int ReconciledMoves { get; set; }
        public int InterruptedItems { get; set; }
        public int TemporaryFilesDeleted { get; set; }
    }
}
