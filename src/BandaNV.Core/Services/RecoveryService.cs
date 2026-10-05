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
        var stats = new RecoveryStats();

        foreach (var item in record.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Status == OrganizationExecutionItemStatus.Planned)
            {
                MarkInterrupted(
                    item,
                    "La aplicación se cerró antes de que este archivo comenzara a moverse.");
                stats.InterruptedItems++;
                continue;
            }

            if (item.Status != OrganizationExecutionItemStatus.Moving)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.FinalPath) ||
                !IsSameOrInside(item.OriginalPath, record.SourceFolder) ||
                !IsSameOrInside(item.FinalPath, record.DestinationFolder))
            {
                item.Status = OrganizationExecutionItemStatus.Error;
                item.Message =
                    "La ejecución se interrumpió y las rutas registradas no permiten una recuperación automática segura.";
                stats.InterruptedItems++;
                continue;
            }

            var sourcePath = Path.GetFullPath(item.OriginalPath);
            var targetPath = Path.GetFullPath(item.FinalPath);
            var backupPath = GetReplacementBackupCandidate(
                record,
                item,
                targetPath);

            var sourceExists = File.Exists(sourcePath);
            var targetExists = File.Exists(targetPath);
            var backupExists =
                !string.IsNullOrWhiteSpace(backupPath) &&
                File.Exists(backupPath);

            if (sourceExists && targetExists)
            {
                if (MatchesExpectedFile(sourcePath, item) &&
                    MatchesExpectedFile(targetPath, item) &&
                    await FilesAreIdenticalAsync(
                        sourcePath,
                        targetPath,
                        cancellationToken))
                {
                    if (TryDeleteFile(sourcePath))
                    {
                        if (backupExists)
                        {
                            item.ReplacedBackupPath = backupPath;
                        }

                        item.Status = OrganizationExecutionItemStatus.Moved;
                        item.Message =
                            "Recuperado al iniciar: el movimiento había terminado, pero faltaba eliminar la copia de origen.";
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
                        "Se encontraron archivos tanto en origen como en destino, pero no son idénticos. BandaNV no modificó ninguno.");
                    stats.InterruptedItems++;
                }
            }
            else if (sourceExists)
            {
                if (backupExists && !targetExists)
                {
                    try
                    {
                        MoveFileSafely(backupPath!, targetPath);
                        item.ReplacedBackupPath = null;
                    }
                    catch (Exception ex)
                    {
                        item.Status = OrganizationExecutionItemStatus.Error;
                        item.Message =
                            $"La ejecución se interrumpió antes de mover el archivo nuevo y no se pudo restaurar el archivo reemplazado: {ex.Message}";
                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            CleanupTemporaryCopies(targetPath);
                        continue;
                    }
                }

                MarkInterrupted(
                    item,
                    backupExists
                        ? "La ejecución se interrumpió antes de completar el movimiento. El archivo anterior del destino fue restaurado y el archivo nuevo permanece en origen."
                        : "La ejecución se interrumpió antes de completar el movimiento. El archivo permanece en origen.");
                stats.InterruptedItems++;
            }
            else if (targetExists)
            {
                if (MatchesExpectedFile(targetPath, item))
                {
                    if (backupExists)
                    {
                        item.ReplacedBackupPath = backupPath;
                    }

                    item.Status = OrganizationExecutionItemStatus.Moved;
                    item.Message =
                        "Recuperado al iniciar: el archivo ya se encontraba correctamente en destino.";
                    stats.ReconciledMoves++;
                }
                else
                {
                    item.Status = OrganizationExecutionItemStatus.Error;
                    item.Message =
                        "La ejecución se interrumpió y el archivo encontrado en destino no coincide con el que se había analizado. No se modificó.";
                    stats.InterruptedItems++;
                }
            }
            else
            {
                if (backupExists)
                {
                    try
                    {
                        MoveFileSafely(backupPath!, targetPath);
                        item.ReplacedBackupPath = null;
                    }
                    catch (Exception ex)
                    {
                        item.Status = OrganizationExecutionItemStatus.Error;
                        item.Message =
                            $"No se encontró el archivo nuevo y tampoco se pudo restaurar el archivo anterior del destino: {ex.Message}";
                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            CleanupTemporaryCopies(targetPath);
                        continue;
                    }
                }

                item.Status = OrganizationExecutionItemStatus.SourceMissing;
                item.Message = backupExists
                    ? "La ejecución se interrumpió y el archivo nuevo ya no estaba disponible. El archivo anterior del destino fue restaurado."
                    : "La ejecución se interrumpió y no se encontró el archivo ni en origen ni en destino.";
                stats.InterruptedItems++;
            }

            stats.TemporaryFilesDeleted +=
                CleanupTemporaryCopies(targetPath);
        }

        return stats;
    }

    private static async Task<RecoveryStats> RecoverUndoAsync(
        OrganizationExecutionRecord undoRecord,
        IReadOnlyDictionary<string, RecoveryEntry> recordsByExecutionId,
        CancellationToken cancellationToken)
    {
        var stats = new RecoveryStats();

        var relatedExecutionId =
            ResolveRelatedExecutionId(undoRecord);

        if (string.IsNullOrWhiteSpace(relatedExecutionId) ||
            !recordsByExecutionId.TryGetValue(
                relatedExecutionId,
                out var originalEntry) ||
            !originalEntry.Record.Type.Equals(
                "ORGANIZE",
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in undoRecord.Items.Where(item =>
                         item.Status is OrganizationExecutionItemStatus.Planned or
                             OrganizationExecutionItemStatus.Moving))
            {
                MarkInterrupted(
                    item,
                    "El Undo se interrumpió y no se pudo identificar con certeza su ejecución original. BandaNV no modificó archivos durante la recuperación.");
                stats.InterruptedItems++;

                if (!string.IsNullOrWhiteSpace(item.FinalPath))
                {
                    stats.TemporaryFilesDeleted +=
                        CleanupTemporaryCopies(item.FinalPath);
                }

                if (!string.IsNullOrWhiteSpace(item.OriginalPath))
                {
                    stats.TemporaryFilesDeleted +=
                        CleanupTemporaryCopies(item.OriginalPath);
                }
            }

            return stats;
        }

        undoRecord.RelatedExecutionId = relatedExecutionId;
        var originalRecord = originalEntry.Record;

        foreach (var undoItem in undoRecord.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (undoItem.Status == OrganizationExecutionItemStatus.Planned)
            {
                MarkInterrupted(
                    undoItem,
                    "La aplicación se cerró antes de que comenzara la restauración de este archivo.");
                stats.InterruptedItems++;
                continue;
            }

            if (undoItem.Status != OrganizationExecutionItemStatus.Moving)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(undoItem.FinalPath) ||
                !IsSameOrInside(
                    undoItem.OriginalPath,
                    undoRecord.SourceFolder) ||
                !IsSameOrInside(
                    undoItem.FinalPath,
                    undoRecord.DestinationFolder))
            {
                undoItem.Status = OrganizationExecutionItemStatus.Error;
                undoItem.Message =
                    "El Undo se interrumpió y las rutas registradas no permiten una recuperación automática segura.";
                stats.InterruptedItems++;
                continue;
            }

            var originalItem = originalRecord.Items.FirstOrDefault(item =>
                item.Status == OrganizationExecutionItemStatus.Moved &&
                PathsEqual(item.OriginalPath, undoItem.FinalPath) &&
                item.SizeBytes == undoItem.SizeBytes &&
                item.ModifiedUtcTicks == undoItem.ModifiedUtcTicks);

            if (originalItem is null)
            {
                MarkInterrupted(
                    undoItem,
                    "El Undo se interrumpió y no se pudo vincular este archivo con un movimiento original de forma inequívoca.");
                stats.InterruptedItems++;
                continue;
            }

            var organizedPath = Path.GetFullPath(undoItem.OriginalPath);
            var restoredPath = Path.GetFullPath(undoItem.FinalPath);

            var backupPath = originalItem.ReplacedBackupPath;
            var replacementWasUsed =
                !string.IsNullOrWhiteSpace(backupPath);

            var organizedExists = File.Exists(organizedPath);
            var restoredExists = File.Exists(restoredPath);
            var backupExists =
                replacementWasUsed &&
                File.Exists(backupPath);

            if (!restoredExists)
            {
                if (organizedExists)
                {
                    MarkInterrupted(
                        undoItem,
                        "El Undo se interrumpió antes de completar la restauración. El archivo organizado permanece en destino.");
                }
                else
                {
                    undoItem.Status =
                        OrganizationExecutionItemStatus.SourceMissing;
                    undoItem.Message =
                        "El Undo se interrumpió y el archivo ya no está disponible ni en la ubicación organizada ni en su ubicación original.";
                }

                stats.InterruptedItems++;
            }
            else if (!MatchesExpectedFile(restoredPath, undoItem))
            {
                undoItem.Status = OrganizationExecutionItemStatus.Error;
                undoItem.Message =
                    "El archivo presente en la ubicación original no coincide con el que se estaba restaurando. BandaNV no modificó nada.";
                stats.InterruptedItems++;
            }
            else if (!replacementWasUsed)
            {
                if (!organizedExists)
                {
                    undoItem.Status = OrganizationExecutionItemStatus.Moved;
                    undoItem.Message =
                        "Recuperado al iniciar: el archivo ya había sido restaurado a su ubicación original.";
                    stats.ReconciledMoves++;
                }
                else if (MatchesExpectedFile(organizedPath, undoItem) &&
                         await FilesAreIdenticalAsync(
                             organizedPath,
                             restoredPath,
                             cancellationToken) &&
                         TryDeleteFile(organizedPath))
                {
                    undoItem.Status = OrganizationExecutionItemStatus.Moved;
                    undoItem.Message =
                        "Recuperado al iniciar: la restauración había terminado y se eliminó la copia duplicada que quedó en destino.";
                    stats.ReconciledMoves++;
                }
                else
                {
                    MarkInterrupted(
                        undoItem,
                        "Se encontraron archivos en ambas ubicaciones y no se pudo demostrar que fueran la misma copia. BandaNV no modificó ninguno.");
                    stats.InterruptedItems++;
                }
            }
            else if (backupExists)
            {
                if (organizedExists)
                {
                    if (!MatchesExpectedFile(organizedPath, undoItem) ||
                        !await FilesAreIdenticalAsync(
                            organizedPath,
                            restoredPath,
                            cancellationToken) ||
                        !TryDeleteFile(organizedPath))
                    {
                        MarkInterrupted(
                            undoItem,
                            "El archivo nuevo quedó presente en ambas ubicaciones y no pudo reconciliarse de forma segura. El backup protegido se conserva.");
                        stats.InterruptedItems++;
                        stats.TemporaryFilesDeleted +=
                            CleanupTemporaryCopies(restoredPath);
                        stats.TemporaryFilesDeleted +=
                            CleanupTemporaryCopies(organizedPath);
                        continue;
                    }
                }

                try
                {
                    MoveFileSafely(backupPath!, organizedPath);

                    undoItem.Status = OrganizationExecutionItemStatus.Moved;
                    undoItem.Message =
                        "Recuperado al iniciar: se completó la restauración y se repuso el archivo que había sido reemplazado.";
                    stats.ReconciledMoves++;
                }
                catch (Exception ex)
                {
                    undoItem.Status = OrganizationExecutionItemStatus.Error;
                    undoItem.Message =
                        $"El archivo nuevo fue restaurado, pero no se pudo reponer el archivo reemplazado desde su backup protegido: {ex.Message}";
                    stats.InterruptedItems++;
                }
            }
            else if (organizedExists &&
                     MatchesReplacedFile(
                         organizedPath,
                         originalItem))
            {
                undoItem.Status = OrganizationExecutionItemStatus.Moved;
                undoItem.Message =
                    "Recuperado al iniciar: el Undo ya había restaurado el archivo nuevo y repuesto correctamente el archivo reemplazado.";
                stats.ReconciledMoves++;
            }
            else
            {
                undoItem.Status = OrganizationExecutionItemStatus.Error;
                undoItem.Message =
                    "El archivo nuevo fue restaurado, pero no se pudo comprobar que el archivo reemplazado también haya sido repuesto. BandaNV no realizó cambios adicionales.";
                stats.InterruptedItems++;
            }

            stats.TemporaryFilesDeleted +=
                CleanupTemporaryCopies(restoredPath);
            stats.TemporaryFilesDeleted +=
                CleanupTemporaryCopies(organizedPath);
        }

        return stats;
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
                $"{stats.InterruptedItems} archivo{(stats.InterruptedItems == 1 ? string.Empty : "s")} marcado{(stats.InterruptedItems == 1 ? string.Empty : "s")} como interrumpido o con incidencia");
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
        if (!string.IsNullOrWhiteSpace(
                item.ReplacedBackupPath))
        {
            return Path.GetFullPath(
                item.ReplacedBackupPath);
        }

        var candidate = Path.Combine(
            PortablePaths.HistoryDirectory,
            "replaced",
            record.ExecutionId,
            $"{item.UndoId}_{Path.GetFileName(targetPath)}");

        return File.Exists(candidate)
            ? candidate
            : null;
    }

    private static bool MatchesExpectedFile(
        string path,
        OrganizationExecutionItemRecord item)
    {
        try
        {
            var info = new FileInfo(path);

            return info.Exists &&
                   info.Length == item.SizeBytes &&
                   Math.Abs(
                       info.LastWriteTimeUtc.Ticks -
                       item.ModifiedUtcTicks) <=
                   TimestampToleranceTicks;
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesReplacedFile(
        string path,
        OrganizationExecutionItemRecord originalItem)
    {
        if (!originalItem.ReplacedSizeBytes.HasValue ||
            !originalItem.ReplacedModifiedUtcTicks.HasValue)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);

            return info.Exists &&
                   info.Length ==
                   originalItem.ReplacedSizeBytes.Value &&
                   Math.Abs(
                       info.LastWriteTimeUtc.Ticks -
                       originalItem.ReplacedModifiedUtcTicks.Value) <=
                   TimestampToleranceTicks;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> FilesAreIdenticalAsync(
        string leftPath,
        string rightPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);

            if (!leftInfo.Exists ||
                !rightInfo.Exists ||
                leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            await using var left = new FileStream(
                leftPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);

            await using var right = new FileStream(
                rightPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);

            using var leftHash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            using var rightHash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            var leftBuffer = new byte[1024 * 128];
            var rightBuffer = new byte[1024 * 128];

            while (true)
            {
                var leftRead = await left.ReadAsync(
                    leftBuffer,
                    cancellationToken);
                var rightRead = await right.ReadAsync(
                    rightBuffer,
                    cancellationToken);

                if (leftRead != rightRead)
                {
                    return false;
                }

                if (leftRead == 0)
                {
                    break;
                }

                leftHash.AppendData(
                    leftBuffer,
                    0,
                    leftRead);
                rightHash.AppendData(
                    rightBuffer,
                    0,
                    rightRead);
            }

            return CryptographicOperations.FixedTimeEquals(
                leftHash.GetHashAndReset(),
                rightHash.GetHashAndReset());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static int CleanupTemporaryCopies(
        string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return 0;
        }

        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullPath);

            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return 0;
            }

            var fileName = Path.GetFileName(fullPath);
            var pattern =
                fileName + ".bandanv_tmp_*";

            var deleted = 0;

            foreach (var temporaryPath in Directory.EnumerateFiles(
                         directory,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (TryDeleteFile(temporaryPath))
                {
                    deleted++;
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
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

    private static void MoveFileSafely(
        string source,
        string destination)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        try
        {
            File.Move(
                source,
                destination);
            return;
        }
        catch (IOException)
        {
            // Puede ser un movimiento entre unidades.
        }

        var temporaryDestination =
            destination +
            $".bandanv_tmp_{Guid.NewGuid():N}";

        try
        {
            File.Copy(
                source,
                temporaryDestination,
                overwrite: false);

            var sourceLength =
                new FileInfo(source).Length;
            var copiedLength =
                new FileInfo(
                    temporaryDestination).Length;

            if (sourceLength != copiedLength)
            {
                throw new IOException(
                    "La copia entre unidades no pudo validarse.");
            }

            File.Move(
                temporaryDestination,
                destination,
                overwrite: false);

            try
            {
                File.Delete(source);
            }
            catch
            {
                TryDeleteFile(destination);
                throw;
            }
        }
        finally
        {
            TryDeleteFile(temporaryDestination);
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
