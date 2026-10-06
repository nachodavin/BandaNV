using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class UndoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool SupportsUndo(
        OrganizationExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(
            record);

        if (record.Type.Equals(
                "ORGANIZE",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (record.Type.Equals(
                "SEARCH",
                StringComparison.OrdinalIgnoreCase))
        {
            return record.Action?.Equals(
                       "CHANGE_CATEGORY",
                       StringComparison.OrdinalIgnoreCase) == true ||
                   record.Action?.Equals(
                       "RENAME",
                       StringComparison.OrdinalIgnoreCase) == true;
        }

        if (record.Type.Equals(
                "ORGANIZE_ACTION",
                StringComparison.OrdinalIgnoreCase))
        {
            return record.Action?.Equals(
                       "RENAME",
                       StringComparison.OrdinalIgnoreCase) == true;
        }

        return false;
    }

    public static bool IsUndoCandidate(
        OrganizationExecutionRecord record,
        OrganizationExecutionItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(
            record);
        ArgumentNullException.ThrowIfNull(
            item);

        if (!SupportsUndo(
                record))
        {
            return false;
        }

        if (record.Type.Equals(
                "ORGANIZE",
                StringComparison.OrdinalIgnoreCase))
        {
            return item.Status ==
                OrganizationExecutionItemStatus.Moved;
        }

        if (record.Type.Equals(
                "SEARCH",
                StringComparison.OrdinalIgnoreCase) &&
            record.Action?.Equals(
                "CHANGE_CATEGORY",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return item.Status ==
                OrganizationExecutionItemStatus.Moved;
        }

        return item.Status ==
            OrganizationExecutionItemStatus.Renamed;
    }

    public Task<OrganizationExecutionResult> UndoAsync(
        AppSettings settings,
        OrganizationExecutionRecord original,
        IProgress<OrganizationExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(original);

        return Task.Run(
            () => UndoCoreAsync(
                settings,
                original,
                progress,
                cancellationToken),
            cancellationToken);
    }

    private static async Task<OrganizationExecutionResult> UndoCoreAsync(
        AppSettings settings,
        OrganizationExecutionRecord original,
        IProgress<OrganizationExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!SupportsUndo(
                original))
        {
            throw new InvalidOperationException(
                "La acción o ejecución seleccionada no admite Undo seguro.");
        }

        PortablePaths.EnsureDirectories();

        var originalUndoItems =
            original.Items
                .Where(item =>
                    IsUndoCandidate(
                        original,
                        item))
                .ToList();

        if (originalUndoItems.Count == 0)
        {
            throw new InvalidOperationException(
                "La acción o ejecución seleccionada no contiene cambios reversibles.");
        }

        var now =
            DateTime.Now;

        var undoRecord =
            new OrganizationExecutionRecord
            {
                Type = "UNDO",
                ShowInHistory =
                    original.ShowInHistory,
                StartedAt = now,
                SourceFolder =
                    original.DestinationFolder,
                DestinationFolder =
                    original.SourceFolder,
                ConflictBehavior =
                    "Undo seguro",
                RelatedExecutionId =
                    original.ExecutionId,
                Items =
                    originalUndoItems
                        .Select(item =>
                            new OrganizationExecutionItemRecord
                            {
                                FileName =
                                    item.FileName,
                                OriginalPath =
                                    item.FinalPath ??
                                    string.Empty,
                                FinalPath =
                                    item.OriginalPath,
                                CategoryId =
                                    item.CategoryId,
                                CategoryName =
                                    item.CategoryName,
                                CategoryOrder =
                                    item.CategoryOrder,
                                SizeBytes =
                                    item.SizeBytes,
                                ModifiedUtcTicks =
                                    item.ModifiedUtcTicks,
                                Kind =
                                    item.Kind,
                                ContainedFileCount =
                                    item.ContainedFileCount,
                                ContentFingerprint =
                                    item.ContentFingerprint,
                                RelatedItemUndoId =
                                    item.UndoId,
                                Status =
                                    OrganizationExecutionItemStatus.Planned,
                                Message =
                                    $"Undo de {original.ExecutionId}"
                            })
                        .ToList()
            };

        var baseName =
            $"BandaNV_{now:dd-MM-yyyy____HH-mm-ss}";

        var historyPath =
            Path.Combine(
                PortablePaths.HistoryDirectory,
                BuildUniqueFileName(
                    PortablePaths.HistoryDirectory,
                    baseName,
                    ".json"));

        var logPath =
            Path.Combine(
                PortablePaths.LogsDirectory,
                BuildUniqueFileName(
                    PortablePaths.LogsDirectory,
                    baseName,
                    ".txt"));

        await PersistRecordAsync(
            undoRecord,
            historyPath,
            logPath,
            cancellationToken);

        var total =
            undoRecord.Items.Count;

        var processed =
            0;

        try
        {
            for (var index = 0;
                 index < undoRecord.Items.Count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var undoItem =
                    undoRecord.Items[index];

                var originalItem =
                    originalUndoItems[index];

                await UndoItemAsync(
                    settings,
                    original,
                    originalItem,
                    undoRecord,
                    undoItem,
                    historyPath,
                    logPath,
                    cancellationToken);

                await PersistRecordAsync(
                    undoRecord,
                    historyPath,
                    logPath,
                    cancellationToken);

                processed++;

                progress?.Report(
                    new OrganizationExecutionProgress(
                        processed,
                        total,
                        undoItem.FileName,
                        GetProgressMessage(
                            undoItem)));
            }

            undoRecord.FinishedAt =
                DateTime.Now;

            undoRecord.Status =
                undoRecord.Items.Any(item =>
                    item.Status is not
                        OrganizationExecutionItemStatus.Moved)
                    ? OrganizationExecutionStatus.CompletedWithIssues
                    : OrganizationExecutionStatus.Completed;

            await PersistRecordAsync(
                undoRecord,
                historyPath,
                logPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            undoRecord.FinishedAt =
                DateTime.Now;

            undoRecord.Status =
                OrganizationExecutionStatus.Cancelled;

            await PersistRecordAsync(
                undoRecord,
                historyPath,
                logPath,
                CancellationToken.None);

            throw;
        }

        return new OrganizationExecutionResult(
            undoRecord,
            historyPath,
            logPath);
    }

    private static async Task UndoItemAsync(
        AppSettings settings,
        OrganizationExecutionRecord original,
        OrganizationExecutionItemRecord originalItem,
        OrganizationExecutionRecord undoRecord,
        OrganizationExecutionItemRecord undoItem,
        string historyPath,
        string logPath,
        CancellationToken cancellationToken)
    {
        var organizedPath =
            CategoryService.ResolveCurrentOrganizedFilePath(
                settings,
                original,
                originalItem);

        if (string.IsNullOrWhiteSpace(
                organizedPath))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.Error;
            undoItem.Message =
                "El historial original no contiene una ruta final válida.";
            return;
        }

        undoItem.OriginalPath =
            organizedPath;

        if (!IsSameOrInside(
                organizedPath,
                original.DestinationFolder) ||
            !IsSameOrInside(
                originalItem.OriginalPath,
                original.SourceFolder))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.Error;
            undoItem.Message =
                "Las rutas registradas quedaron fuera de los límites de la ejecución original.";
            return;
        }

        if (!OrganizationEntrySafety.Exists(
                organizedPath))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.SourceMissing;
            undoItem.Message =
                originalItem.IsDirectory
                    ? "La carpeta ya no existe en la ubicación registrada después de la acción."
                    : "El archivo ya no existe en la ubicación registrada después de la acción.";
            return;
        }

        if (OrganizationEntrySafety.Exists(
                originalItem.OriginalPath))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.ConflictNeedsDecision;
            undoItem.Message =
                "Ya existe un elemento en la ubicación original. Undo no sobrescribió nada.";
            return;
        }

        if (!OrganizationEntrySafety.MatchesExpected(
                organizedPath,
                originalItem))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.SourceChanged;
            undoItem.Message =
                originalItem.IsDirectory
                    ? "La carpeta cambió después de la acción original. Undo no la tocó."
                    : "El archivo cambió después de la acción original. Undo no lo tocó.";
            return;
        }

        var backupPath =
            GetSafeRecordedBackupPath(
                originalItem.ReplacedBackupPath);

        var replacementWasUsed =
            !string.IsNullOrWhiteSpace(
                originalItem.ReplacedBackupPath);

        if (replacementWasUsed)
        {
            if (string.IsNullOrWhiteSpace(
                    backupPath) ||
                !OrganizationEntrySafety.Exists(
                    backupPath))
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Error;
                undoItem.Message =
                    "Falta la copia protegida del elemento reemplazado. Undo se detuvo para no perder datos.";
                return;
            }

            if (!OrganizationEntrySafety.MatchesReplacement(
                    backupPath,
                    originalItem))
            {
                undoItem.Status =
                    OrganizationExecutionItemStatus.Error;
                undoItem.Message =
                    "La copia protegida del elemento reemplazado no coincide con el registro original. Undo se detuvo para no perder datos.";
                return;
            }
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                originalItem.OriginalPath)!);

        undoItem.Status =
            OrganizationExecutionItemStatus.Moving;

        undoItem.Message =
            originalItem.IsDirectory
                ? "Restauración de carpeta iniciada."
                : "Restauración iniciada.";

        await PersistRecordAsync(
            undoRecord,
            historyPath,
            logPath,
            cancellationToken);

        var restoredOrganizedEntry =
            false;

        try
        {
            OrganizationEntrySafety.MoveEntrySafely(
                organizedPath,
                originalItem.OriginalPath,
                cancellationToken);

            restoredOrganizedEntry =
                true;

            if (replacementWasUsed &&
                !string.IsNullOrWhiteSpace(
                    backupPath))
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        organizedPath)!);

                OrganizationEntrySafety.MoveEntrySafely(
                    backupPath,
                    organizedPath,
                    cancellationToken);
            }

            undoItem.Status =
                OrganizationExecutionItemStatus.Moved;

            undoItem.Message =
                replacementWasUsed
                    ? "Restaurado al origen y repuesto el elemento que había sido reemplazado."
                    : "Restaurado al origen.";
        }
        catch (Exception ex)
        {
            var rollbackFailed =
                false;

            if (restoredOrganizedEntry &&
                OrganizationEntrySafety.Exists(
                    originalItem.OriginalPath) &&
                !OrganizationEntrySafety.Exists(
                    organizedPath))
            {
                try
                {
                    OrganizationEntrySafety.MoveEntrySafely(
                        originalItem.OriginalPath,
                        organizedPath,
                        CancellationToken.None);

                    restoredOrganizedEntry =
                        false;
                }
                catch
                {
                    rollbackFailed =
                        true;
                }
            }

            undoItem.Status =
                OrganizationExecutionItemStatus.Error;

            undoItem.Message =
                rollbackFailed
                    ? $"{ex.Message} Además, no se pudo revertir automáticamente el movimiento parcial."
                    : $"{ex.Message} El elemento se dejó en el estado más seguro disponible.";
        }
    }

    private static string? GetSafeRecordedBackupPath(
        string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(
                backupPath))
        {
            return null;
        }

        try
        {
            var fullPath =
                Path.GetFullPath(
                    backupPath);

            var replacedRoot =
                Path.Combine(
                    PortablePaths.HistoryDirectory,
                    "replaced");

            return IsSameOrInside(
                    fullPath,
                    replacedRoot)
                ? fullPath
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task PersistRecordAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        string logPath,
        CancellationToken cancellationToken)
    {
        var json =
            JsonSerializer.Serialize(
                record,
                JsonOptions);

        await WriteTextAtomicAsync(
            historyPath,
            json,
            cancellationToken);

        await WriteTextAtomicAsync(
            logPath,
            OrganizationExecutionService.FormatHumanLog(
                record),
            cancellationToken);
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                path)!);

        var tempPath =
            path +
            $".tmp_{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false),
                cancellationToken);

            File.Move(
                tempPath,
                path,
                overwrite:
                    true);
        }
        finally
        {
            TryDelete(
                tempPath);
        }
    }

    private static string BuildUniqueFileName(
        string directory,
        string baseName,
        string extension)
    {
        var candidate =
            baseName +
            extension;

        if (!File.Exists(
                Path.Combine(
                    directory,
                    candidate)))
        {
            return candidate;
        }

        var index =
            2;

        do
        {
            candidate =
                $"{baseName}__{index}{extension}";

            index++;
        }
        while (File.Exists(
            Path.Combine(
                directory,
                candidate)));

        return candidate;
    }

    private static string GetProgressMessage(
        OrganizationExecutionItemRecord item) =>
        item.Status switch
        {
            OrganizationExecutionItemStatus.Moved =>
                "Restaurado",
            OrganizationExecutionItemStatus.ConflictNeedsDecision =>
                "Conflicto en origen",
            OrganizationExecutionItemStatus.Interrupted =>
                "Interrumpido",
            OrganizationExecutionItemStatus.SourceMissing =>
                "Ya no existe",
            OrganizationExecutionItemStatus.SourceChanged =>
                "Cambió después de organizarse",
            OrganizationExecutionItemStatus.Error =>
                "Error",
            _ =>
                item.Status.ToString()
        };

    private static bool IsSameOrInside(
        string candidate,
        string root)
    {
        var fullCandidate =
            NormalizeDirectoryPath(
                candidate);

        var fullRoot =
            NormalizeDirectoryPath(
                root);

        return fullCandidate.Equals(
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
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

    private static void TryDelete(
        string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(
                    path) &&
                File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }
}
