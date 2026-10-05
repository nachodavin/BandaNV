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
        if (!original.Type.Equals(
                "ORGANIZE",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Solo las ejecuciones de Organización pueden deshacerse.");
        }

        PortablePaths.EnsureDirectories();

        var originalMovedItems = original.Items
            .Where(item =>
                item.Status == OrganizationExecutionItemStatus.Moved)
            .ToList();

        if (originalMovedItems.Count == 0)
        {
            throw new InvalidOperationException(
                "La ejecución seleccionada no contiene movimientos para deshacer.");
        }

        var now = DateTime.Now;
        var undoRecord = new OrganizationExecutionRecord
        {
            Type = "UNDO",
            StartedAt = now,
            SourceFolder = original.DestinationFolder,
            DestinationFolder = original.SourceFolder,
            ConflictBehavior = "Undo seguro",
            RelatedExecutionId = original.ExecutionId,
            Items = originalMovedItems
                .Select(item => new OrganizationExecutionItemRecord
                {
                    FileName = item.FileName,
                    OriginalPath = item.FinalPath ?? string.Empty,
                    FinalPath = item.OriginalPath,
                    CategoryId = item.CategoryId,
                    CategoryName = item.CategoryName,
                    CategoryOrder = item.CategoryOrder,
                    SizeBytes = item.SizeBytes,
                    ModifiedUtcTicks = item.ModifiedUtcTicks,
                    Status = OrganizationExecutionItemStatus.Planned,
                    Message = $"Undo de {original.ExecutionId}"
                })
                .ToList()
        };

        var baseName =
            $"BandaNV_{now:dd-MM-yyyy____HH-mm-ss}";

        var historyPath = Path.Combine(
            PortablePaths.HistoryDirectory,
            BuildUniqueFileName(
                PortablePaths.HistoryDirectory,
                baseName,
                ".json"));

        var logPath = Path.Combine(
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

        var total = undoRecord.Items.Count;
        var processed = 0;

        try
        {
            for (var index = 0; index < undoRecord.Items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var undoItem = undoRecord.Items[index];
                var originalItem = originalMovedItems[index];

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

                progress?.Report(new OrganizationExecutionProgress(
                    processed,
                    total,
                    undoItem.FileName,
                    GetProgressMessage(undoItem)));
            }

            undoRecord.FinishedAt = DateTime.Now;
            undoRecord.Status = undoRecord.Items.Any(item =>
                    item.Status is not OrganizationExecutionItemStatus.Moved)
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
            undoRecord.FinishedAt = DateTime.Now;
            undoRecord.Status = OrganizationExecutionStatus.Cancelled;

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

        if (string.IsNullOrWhiteSpace(organizedPath))
        {
            undoItem.Status = OrganizationExecutionItemStatus.Error;
            undoItem.Message =
                "El historial original no contiene una ruta final válida.";
            return;
        }

        undoItem.OriginalPath = organizedPath;

        if (!IsSameOrInside(
                organizedPath,
                original.DestinationFolder) ||
            !IsSameOrInside(
                originalItem.OriginalPath,
                original.SourceFolder))
        {
            undoItem.Status = OrganizationExecutionItemStatus.Error;
            undoItem.Message =
                "Las rutas registradas quedaron fuera de los límites de la ejecución original.";
            return;
        }

        if (!File.Exists(organizedPath))
        {
            undoItem.Status = OrganizationExecutionItemStatus.SourceMissing;
            undoItem.Message =
                "El archivo organizado ya no existe en su ubicación final.";
            return;
        }

        if (File.Exists(originalItem.OriginalPath))
        {
            undoItem.Status =
                OrganizationExecutionItemStatus.ConflictNeedsDecision;
            undoItem.Message =
                "Ya existe un archivo en la ubicación original. Undo no sobrescribió nada.";
            return;
        }

        FileInfo organizedInfo;
        try
        {
            organizedInfo = new FileInfo(organizedPath);
        }
        catch (Exception ex)
        {
            undoItem.Status = OrganizationExecutionItemStatus.Error;
            undoItem.Message = ex.Message;
            return;
        }

        if (organizedInfo.Length != originalItem.SizeBytes ||
            Math.Abs(
                organizedInfo.LastWriteTimeUtc.Ticks -
                originalItem.ModifiedUtcTicks) > 20_000_000L)
        {
            undoItem.Status = OrganizationExecutionItemStatus.SourceChanged;
            undoItem.Message =
                "El archivo cambió después de organizarse. Undo no lo tocó.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                originalItem.ReplacedBackupPath) &&
            !File.Exists(originalItem.ReplacedBackupPath))
        {
            undoItem.Status = OrganizationExecutionItemStatus.Error;
            undoItem.Message =
                "Falta la copia protegida del archivo reemplazado. Undo se detuvo para no perder datos.";
            return;
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(originalItem.OriginalPath)!);

        undoItem.Status = OrganizationExecutionItemStatus.Moving;
        undoItem.Message = "Restauración iniciada.";

        await PersistRecordAsync(
            undoRecord,
            historyPath,
            logPath,
            cancellationToken);

        var restoredOrganizedFile = false;

        try
        {
            MoveFileSafely(
                organizedPath,
                originalItem.OriginalPath);

            restoredOrganizedFile = true;

            if (!string.IsNullOrWhiteSpace(
                    originalItem.ReplacedBackupPath))
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(organizedPath)!);

                MoveFileSafely(
                    originalItem.ReplacedBackupPath,
                    organizedPath);
            }

            undoItem.Status = OrganizationExecutionItemStatus.Moved;
            undoItem.Message =
                string.IsNullOrWhiteSpace(originalItem.ReplacedBackupPath)
                    ? "Restaurado al origen."
                    : "Restaurado al origen y repuesto el archivo que había sido reemplazado.";
        }
        catch (Exception ex)
        {
            var rollbackFailed = false;

            if (restoredOrganizedFile &&
                File.Exists(originalItem.OriginalPath) &&
                !File.Exists(organizedPath))
            {
                try
                {
                    MoveFileSafely(
                        originalItem.OriginalPath,
                        organizedPath);

                    restoredOrganizedFile = false;
                }
                catch
                {
                    rollbackFailed = true;
                }
            }

            undoItem.Status = OrganizationExecutionItemStatus.Error;
            undoItem.Message = rollbackFailed
                ? $"{ex.Message} Además, no se pudo revertir automáticamente el movimiento parcial."
                : $"{ex.Message} El archivo se dejó en el estado más seguro disponible.";
        }
    }

    private static async Task PersistRecordAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        string logPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            record,
            JsonOptions);

        await WriteTextAtomicAsync(
            historyPath,
            json,
            cancellationToken);

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

        var tempPath =
            path + $".tmp_{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            File.Move(
                tempPath,
                path,
                overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static string BuildUniqueFileName(
        string directory,
        string baseName,
        string extension)
    {
        var candidate = baseName + extension;

        if (!File.Exists(
                Path.Combine(directory, candidate)))
        {
            return candidate;
        }

        var index = 2;

        do
        {
            candidate =
                $"{baseName}__{index}{extension}";
            index++;
        }
        while (File.Exists(
            Path.Combine(directory, candidate)));

        return candidate;
    }

    private static string GetProgressMessage(
        OrganizationExecutionItemRecord item) =>
        item.Status switch
        {
            OrganizationExecutionItemStatus.Moved => "Restaurado",
            OrganizationExecutionItemStatus.ConflictNeedsDecision =>
                "Conflicto en origen",
            OrganizationExecutionItemStatus.Interrupted =>
                "Interrumpido",
            OrganizationExecutionItemStatus.SourceMissing =>
                "Ya no existe",
            OrganizationExecutionItemStatus.SourceChanged =>
                "Cambió después de organizarse",
            OrganizationExecutionItemStatus.Error => "Error",
            _ => item.Status.ToString()
        };

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
                TryDelete(destination);
                throw;
            }
        }
        finally
        {
            TryDelete(temporaryDestination);
        }
    }

    private static void TryDelete(
        string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
