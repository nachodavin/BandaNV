using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class OrganizationExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<OrganizationExecutionResult> ExecuteAsync(
        AppSettings settings,
        IReadOnlyList<OrganizationExecutionRequestItem> requestedItems,
        IProgress<OrganizationExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IOrganizationConflictResolver? conflictResolver = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(requestedItems);

        return Task.Run(
            () => ExecuteCoreAsync(
                settings,
                requestedItems,
                progress,
                cancellationToken,
                conflictResolver),
            cancellationToken);
    }

    private static async Task<OrganizationExecutionResult> ExecuteCoreAsync(
        AppSettings settings,
        IReadOnlyList<OrganizationExecutionRequestItem> requestedItems,
        IProgress<OrganizationExecutionProgress>? progress,
        CancellationToken cancellationToken,
        IOrganizationConflictResolver? conflictResolver)
    {
        PortablePaths.EnsureDirectories();

        var sourceRoot = NormalizeDirectoryPath(settings.SourceFolder);
        var destinationRoot = NormalizeDirectoryPath(settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(sourceRoot) ||
            !Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(
                "La carpeta de origen ya no existe o no está disponible.");
        }

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new InvalidOperationException(
                "La carpeta de destino no está configurada.");
        }

        if (settings.CreateFolders)
        {
            Directory.CreateDirectory(destinationRoot);

            foreach (var category in settings.Categories
                         .OrderBy(category => category.Order))
            {
                var categoryFolder = CategoryService.GetFolderPath(
                    destinationRoot,
                    category.Order,
                    category.Name);

                EnsurePathInsideRoot(categoryFolder, destinationRoot);
                Directory.CreateDirectory(categoryFolder);
            }
        }
        else if (!Directory.Exists(destinationRoot))
        {
            throw new DirectoryNotFoundException(
                "La carpeta de destino no existe y Crear carpetas faltantes está desactivado.");
        }

        var now = DateTime.Now;
        var record = new OrganizationExecutionRecord
        {
            StartedAt = now,
            SourceFolder = sourceRoot,
            DestinationFolder = destinationRoot,
            ConflictBehavior = settings.ConflictBehavior,
            Items = requestedItems.Select(item => new OrganizationExecutionItemRecord
            {
                FileName = item.FileName,
                OriginalPath = Path.GetFullPath(item.FullPath),
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryName,
                CategoryOrder = item.CategoryOrder,
                SizeBytes = item.SizeBytes,
                ModifiedUtcTicks = item.ModifiedUtcTicks,
                Status =
                    item.CategoryOrder.HasValue &&
                    !string.IsNullOrWhiteSpace(item.CategoryName)
                        ? OrganizationExecutionItemStatus.Planned
                        : OrganizationExecutionItemStatus.SkippedUnclassified,
                Message =
                    item.CategoryOrder.HasValue &&
                    !string.IsNullOrWhiteSpace(item.CategoryName)
                        ? null
                        : "El archivo no tiene una categoría asignada."
            }).ToList()
        };

        var keepStructuredHistory = settings.SaveHistory || settings.UndoEnabled;
        var historyPath = Path.Combine(
            PortablePaths.HistoryDirectory,
            BuildUniqueFileName(
                PortablePaths.HistoryDirectory,
                $"BandaNV_{now:dd-MM-yyyy____HH-mm-ss}",
                ".json"));

        var logPath = Path.Combine(
            PortablePaths.LogsDirectory,
            BuildUniqueFileName(
                PortablePaths.LogsDirectory,
                $"BandaNV_{now:dd-MM-yyyy____HH-mm-ss}",
                ".txt"));

        await PersistRecordAsync(
            record,
            historyPath,
            logPath,
            cancellationToken);

        var total = record.Items.Count;
        var processed = 0;
        var conflictState = new ConflictResolutionState();

        try
        {
            foreach (var item in record.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.Status == OrganizationExecutionItemStatus.SkippedUnclassified)
                {
                    processed++;
                    progress?.Report(new OrganizationExecutionProgress(
                        processed,
                        total,
                        item.FileName,
                        "Sin categoría: se deja en origen."));
                    continue;
                }

                await ExecuteItemAsync(
                    settings,
                    destinationRoot,
                    record.ExecutionId,
                    record,
                    item,
                    historyPath,
                    logPath,
                    conflictResolver,
                    conflictState,
                    cancellationToken);

                await PersistRecordAsync(
                    record,
                    historyPath,
                    logPath,
                    cancellationToken);

                processed++;

                progress?.Report(new OrganizationExecutionProgress(
                    processed,
                    total,
                    item.FileName,
                    GetProgressMessage(item)));
            }

            if (settings.DeleteEmptyFolders &&
                settings.IncludeSubfolders)
            {
                record.EmptyDirectoriesDeleted =
                    DeleteEmptySourceDirectories(
                        sourceRoot,
                        destinationRoot);
            }

            if (!settings.UndoEnabled)
            {
                ReleaseReplacementBackups(
                    record);
            }

            record.FinishedAt = DateTime.Now;
            record.Status = record.Items.Any(item =>
                    item.Status is OrganizationExecutionItemStatus.Error or
                    OrganizationExecutionItemStatus.SourceMissing or
                    OrganizationExecutionItemStatus.SourceChanged or
                    OrganizationExecutionItemStatus.SkippedConflict or
                    OrganizationExecutionItemStatus.ConflictNeedsDecision)
                ? OrganizationExecutionStatus.CompletedWithIssues
                : OrganizationExecutionStatus.Completed;

            await PersistRecordAsync(
                record,
                historyPath,
                logPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            record.FinishedAt = DateTime.Now;
            record.Status = OrganizationExecutionStatus.Cancelled;

            await PersistRecordAsync(
                record,
                historyPath,
                logPath,
                CancellationToken.None);

            throw;
        }

        if (!keepStructuredHistory)
        {
            TryDelete(historyPath);
            historyPath = string.Empty;
        }

        return new OrganizationExecutionResult(
            record,
            string.IsNullOrWhiteSpace(historyPath) ? null : historyPath,
            logPath);
    }

    private static async Task ExecuteItemAsync(
        AppSettings settings,
        string destinationRoot,
        string executionId,
        OrganizationExecutionRecord record,
        OrganizationExecutionItemRecord item,
        string historyPath,
        string? logPath,
        IOrganizationConflictResolver? conflictResolver,
        ConflictResolutionState conflictState,
        CancellationToken cancellationToken)
    {
        if (!IsSameOrInside(item.OriginalPath, settings.SourceFolder))
        {
            item.Status = OrganizationExecutionItemStatus.Error;
            item.Message =
                "La ruta del archivo quedó fuera de la carpeta de origen configurada.";
            return;
        }

        if (!File.Exists(item.OriginalPath))
        {
            item.Status = OrganizationExecutionItemStatus.SourceMissing;
            item.Message = "El archivo ya no existe en el origen.";
            return;
        }

        FileInfo sourceInfo;
        try
        {
            sourceInfo = new FileInfo(item.OriginalPath);
        }
        catch (Exception ex)
        {
            item.Status = OrganizationExecutionItemStatus.Error;
            item.Message = ex.Message;
            return;
        }

        if (sourceInfo.Length != item.SizeBytes ||
            Math.Abs(sourceInfo.LastWriteTimeUtc.Ticks - item.ModifiedUtcTicks) > 20_000_000L)
        {
            item.Status = OrganizationExecutionItemStatus.SourceChanged;
            item.Message =
                "El archivo cambió desde el análisis. No se movió.";
            return;
        }

        if (!item.CategoryOrder.HasValue ||
            string.IsNullOrWhiteSpace(item.CategoryName))
        {
            item.Status = OrganizationExecutionItemStatus.SkippedUnclassified;
            item.Message = "Sin categoría.";
            return;
        }

        var categoryFolder = CategoryService.GetFolderPath(
            destinationRoot,
            item.CategoryOrder.Value,
            item.CategoryName);

        if (settings.CreateFolders)
        {
            Directory.CreateDirectory(categoryFolder);
        }
        else if (!Directory.Exists(categoryFolder))
        {
            item.Status = OrganizationExecutionItemStatus.Error;
            item.Message =
                $"La carpeta de categoría no existe: {categoryFolder}";
            return;
        }

        var desiredTarget = Path.Combine(
            categoryFolder,
            item.FileName);

        var targetResolution = await ResolveTargetPathAsync(
            settings.ConflictBehavior,
            desiredTarget,
            sourceInfo,
            item,
            conflictResolver,
            conflictState,
            cancellationToken);

        var target = targetResolution.Target;

        if (target is null)
        {
            return;
        }

        EnsurePathInsideRoot(target, destinationRoot);

        item.FinalPath = target;
        item.Status = OrganizationExecutionItemStatus.Moving;
        item.Message = "Movimiento iniciado.";

        // El journal se escribe antes de tocar el archivo. Si la app se
        // interrumpe en este punto, el estado Moving permite reconciliarlo.
        await PersistRecordAsync(
            record,
            historyPath,
            logPath,
            cancellationToken);

        string? replacedBackupPath = null;

        try
        {
            if (File.Exists(target) &&
                targetResolution.Action == OrganizationConflictAction.Replace)
            {
                var replacedInfo = new FileInfo(target);

                item.ReplacedSizeBytes = replacedInfo.Length;
                item.ReplacedModifiedUtcTicks =
                    replacedInfo.LastWriteTimeUtc.Ticks;

                replacedBackupPath = BackupReplacedFile(
                    target,
                    executionId,
                    item.UndoId);

                item.ReplacedBackupPath = replacedBackupPath;

                // La copia protegida ya existe físicamente. Persistimos esa
                // evidencia antes de mover el archivo nuevo para que un cierre
                // abrupto pueda reconciliar el estado sin inferencias.
                await PersistRecordAsync(
                    record,
                    historyPath,
                    logPath,
                    cancellationToken);
            }

            MoveFileSafely(
                item.OriginalPath,
                target);

            item.Status = OrganizationExecutionItemStatus.Moved;
            item.Message = null;
        }
        catch (Exception ex)
        {
            item.Status = OrganizationExecutionItemStatus.Error;
            item.Message = ex.Message;

            if (replacedBackupPath is not null &&
                File.Exists(replacedBackupPath) &&
                !File.Exists(target))
            {
                try
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(target)!);

                    MoveFileSafely(
                        replacedBackupPath,
                        target);

                    item.ReplacedBackupPath = null;
                }
                catch
                {
                    item.Message +=
                        " Además, no se pudo restaurar automáticamente el archivo reemplazado.";
                }
            }
        }
    }

    private static async Task<TargetResolution> ResolveTargetPathAsync(
        string behavior,
        string desiredTarget,
        FileInfo sourceInfo,
        OrganizationExecutionItemRecord item,
        IOrganizationConflictResolver? conflictResolver,
        ConflictResolutionState conflictState,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(desiredTarget))
        {
            return new TargetResolution(desiredTarget, null);
        }

        if (behavior.Equals(
                "Renombrar automáticamente",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution = "Renombrar automáticamente";
            return new TargetResolution(
                GetUniqueDestination(desiredTarget),
                OrganizationConflictAction.Rename);
        }

        if (behavior.Equals(
                "Omitir archivo",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution = "Omitir archivo";
            item.Status = OrganizationExecutionItemStatus.SkippedConflict;
            item.Message =
                "Ya existe un archivo con el mismo nombre en el destino.";
            return new TargetResolution(
                null,
                OrganizationConflictAction.Skip);
        }

        if (behavior.Equals(
                "Reemplazar",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution = "Reemplazar";
            return new TargetResolution(
                desiredTarget,
                OrganizationConflictAction.Replace);
        }

        if (conflictResolver is null)
        {
            item.Status = OrganizationExecutionItemStatus.ConflictNeedsDecision;
            item.Message =
                "Ya existe un archivo con el mismo nombre. La opción Preguntar requiere resolver este conflicto antes de moverlo.";
            return new TargetResolution(null, null);
        }

        OrganizationConflictResolution resolution;

        if (conflictState.ApplyToRemaining is { } rememberedAction)
        {
            resolution = new OrganizationConflictResolution(
                rememberedAction,
                ApplyToRemaining: true);
        }
        else
        {
            OrganizationConflictInfo conflict;

            try
            {
                var destinationInfo = new FileInfo(desiredTarget);

                conflict = new OrganizationConflictInfo(
                    item.FileName,
                    item.OriginalPath,
                    desiredTarget,
                    sourceInfo.Length,
                    destinationInfo.Length,
                    sourceInfo.LastWriteTime,
                    destinationInfo.LastWriteTime);
            }
            catch (FileNotFoundException)
            {
                return new TargetResolution(desiredTarget, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new TargetResolution(desiredTarget, null);
            }

            resolution = await conflictResolver.ResolveAsync(
                conflict,
                cancellationToken);

            if (resolution.ApplyToRemaining)
            {
                conflictState.ApplyToRemaining = resolution.Action;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (resolution.Action)
        {
            case OrganizationConflictAction.Rename:
                item.ConflictResolution = "Renombrar automáticamente";
                return new TargetResolution(
                    GetUniqueDestination(desiredTarget),
                    OrganizationConflictAction.Rename);

            case OrganizationConflictAction.Replace:
                item.ConflictResolution = "Reemplazar";
                return new TargetResolution(
                    desiredTarget,
                    OrganizationConflictAction.Replace);

            default:
                item.ConflictResolution = "Omitir archivo";
                item.Status = OrganizationExecutionItemStatus.SkippedConflict;
                item.Message = "Omitido por decisión del usuario.";
                return new TargetResolution(
                    null,
                    OrganizationConflictAction.Skip);
        }
    }

    private sealed class ConflictResolutionState
    {
        public OrganizationConflictAction? ApplyToRemaining { get; set; }
    }

    private sealed record TargetResolution(
        string? Target,
        OrganizationConflictAction? Action);

    private static void ReleaseReplacementBackups(
        OrganizationExecutionRecord record)
    {
        var backupRoot = Path.Combine(
            PortablePaths.HistoryDirectory,
            "replaced",
            record.ExecutionId);

        foreach (var item in record.Items)
        {
            if (string.IsNullOrWhiteSpace(
                    item.ReplacedBackupPath))
            {
                continue;
            }

            try
            {
                var backupPath =
                    Path.GetFullPath(
                        item.ReplacedBackupPath);
                var normalizedRoot =
                    Path.GetFullPath(
                        backupRoot)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                var isInsideRoot =
                    backupPath.StartsWith(
                        normalizedRoot +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);

                if (!isInsideRoot)
                {
                    continue;
                }

                if (!File.Exists(backupPath) ||
                    TryDeleteAndConfirm(backupPath))
                {
                    item.ReplacedBackupPath = null;
                    item.ReplacedSizeBytes = null;
                    item.ReplacedModifiedUtcTicks = null;
                }
            }
            catch
            {
                // Si la limpieza falla, se conserva la referencia para que el
                // mantenimiento posterior pueda volver a intentarlo.
            }
        }

        try
        {
            if (Directory.Exists(backupRoot) &&
                !Directory.EnumerateFileSystemEntries(
                    backupRoot).Any())
            {
                Directory.Delete(backupRoot);
            }
        }
        catch
        {
        }
    }

    private static bool TryDeleteAndConfirm(
        string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniqueDestination(string desiredTarget)
    {
        if (!File.Exists(desiredTarget))
        {
            return desiredTarget;
        }

        var directory = Path.GetDirectoryName(desiredTarget)!;
        var name = Path.GetFileNameWithoutExtension(desiredTarget);
        var extension = Path.GetExtension(desiredTarget);

        var index = 2;
        string candidate;

        do
        {
            candidate = Path.Combine(
                directory,
                $"{name} ({index}){extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private static string BackupReplacedFile(
        string target,
        string executionId,
        string undoId)
    {
        var backupDirectory = Path.Combine(
            PortablePaths.HistoryDirectory,
            "replaced",
            executionId);

        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(
            backupDirectory,
            $"{undoId}_{Path.GetFileName(target)}");

        MoveFileSafely(target, backupPath);
        return backupPath;
    }

    private static void MoveFileSafely(
        string source,
        string destination)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        try
        {
            File.Move(source, destination);
            return;
        }
        catch (IOException)
        {
            // Puede ocurrir al mover entre unidades distintas.
        }

        var tempDestination =
            destination + $".bandanv_tmp_{Guid.NewGuid():N}";

        try
        {
            File.Copy(
                source,
                tempDestination,
                overwrite: false);

            var sourceLength = new FileInfo(source).Length;
            var copyLength = new FileInfo(tempDestination).Length;

            if (sourceLength != copyLength)
            {
                throw new IOException(
                    "La copia entre unidades no pudo validarse.");
            }

            File.Move(
                tempDestination,
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
            TryDelete(tempDestination);
        }
    }

    private static int DeleteEmptySourceDirectories(
        string sourceRoot,
        string destinationRoot)
    {
        var deleted = 0;

        IEnumerable<string> directories;
        try
        {
            directories = Directory
                .EnumerateDirectories(
                    sourceRoot,
                    "*",
                    SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ToList();
        }
        catch
        {
            return 0;
        }

        foreach (var directory in directories)
        {
            if (IsSameOrInside(directory, destinationRoot))
            {
                continue;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                    deleted++;
                }
            }
            catch
            {
            }
        }

        return deleted;
    }

    private static async Task PersistRecordAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        string? logPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await WriteTextAtomicAsync(
            historyPath,
            json,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await WriteTextAtomicAsync(
                logPath,
                FormatHumanLog(record),
                cancellationToken);
        }

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
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
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

    public static string FormatHumanLog(
        OrganizationExecutionRecord record)
    {
        var builder = new StringBuilder();

        builder.AppendLine("==================================================");
        builder.AppendLine("BandaNV - Registro de ejecución");
        builder.AppendLine("==================================================");
        builder.AppendLine();
        builder.AppendLine(
            $"Tipo: {(record.Type.Equals("UNDO", StringComparison.OrdinalIgnoreCase) ? "DESHACER" : "ORGANIZAR")}");
        builder.AppendLine("Formato: v2");
        builder.AppendLine($"ExecutionID: {record.ExecutionId}");
        builder.AppendLine($"Inicio: {record.StartedAt:dd/MM/yyyy HH:mm:ss}");
        builder.AppendLine($"Origen: {record.SourceFolder}");
        builder.AppendLine($"Destino: {record.DestinationFolder}");
        builder.AppendLine($"Conflictos: {record.ConflictBehavior}");

        if (!string.IsNullOrWhiteSpace(record.RelatedExecutionId))
        {
            builder.AppendLine(
                $"Ejecución relacionada: {record.RelatedExecutionId}");
        }

        if (record.RecoveredAt is { } recoveredAt)
        {
            builder.AppendLine(
                $"Recuperado: {recoveredAt:dd/MM/yyyy HH:mm:ss}");
        }

        if (!string.IsNullOrWhiteSpace(record.RecoveryMessage))
        {
            builder.AppendLine(
                $"Recuperación: {record.RecoveryMessage}");
        }

        builder.AppendLine();
        builder.AppendLine("--------------------------------------------------");
        builder.AppendLine();

        foreach (var item in record.Items)
        {
            var logStatus =
                record.Type.Equals("UNDO", StringComparison.OrdinalIgnoreCase) &&
                item.Status == OrganizationExecutionItemStatus.Moved
                    ? "RESTAURADO"
                    : GetLogStatus(item.Status);

            builder.AppendLine(
                $"[{logStatus}] {item.FileName}");
            builder.AppendLine($"         UndoID: {item.UndoId}");
            builder.AppendLine($"         Original: {item.OriginalPath}");

            if (!string.IsNullOrWhiteSpace(item.FinalPath))
            {
                builder.AppendLine($"         Final: {item.FinalPath}");
            }

            builder.AppendLine($"         Tamaño: {item.SizeBytes}");
            builder.AppendLine($"         ModificadoUTC: {item.ModifiedUtcTicks}");

            if (!string.IsNullOrWhiteSpace(item.CategoryName))
            {
                builder.AppendLine(
                    $"         -> {item.CategoryOrder} - {item.CategoryName}");
            }

            if (!string.IsNullOrWhiteSpace(item.ConflictResolution))
            {
                builder.AppendLine(
                    $"         Resolución de conflicto: {item.ConflictResolution}");
            }

            if (!string.IsNullOrWhiteSpace(item.Message))
            {
                builder.AppendLine($"         Motivo: {item.Message}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("--------------------------------------------------");
        builder.AppendLine();
        builder.AppendLine($"Archivos procesados: {record.MovedCount}");
        builder.AppendLine($"Incidencias: {record.IssueCount}");
        builder.AppendLine(
            $"Estado: {record.Status}");

        if (record.FinishedAt is { } finished)
        {
            builder.AppendLine(
                $"Finalización: {finished:dd/MM/yyyy HH:mm:ss}");

            builder.AppendLine(
                $"Duración: {(finished - record.StartedAt).TotalSeconds:0.##} segundos");
        }

        builder.AppendLine();
        builder.AppendLine("==================================================");

        return builder.ToString();
    }

    private static string GetLogStatus(
        OrganizationExecutionItemStatus status) =>
        status switch
        {
            OrganizationExecutionItemStatus.Moved => "MOVIDO",
            OrganizationExecutionItemStatus.SkippedUnclassified => "SIN_CATEGORIA",
            OrganizationExecutionItemStatus.SkippedConflict => "OMITIDO",
            OrganizationExecutionItemStatus.ConflictNeedsDecision => "CONFLICTO",
            OrganizationExecutionItemStatus.Interrupted => "INTERRUMPIDO",
            OrganizationExecutionItemStatus.SourceMissing => "NO_ENCONTRADO",
            OrganizationExecutionItemStatus.SourceChanged => "MODIFICADO",
            OrganizationExecutionItemStatus.Error => "ERROR",
            OrganizationExecutionItemStatus.Moving => "MOVIENDO",
            _ => "PENDIENTE"
        };

    private static string GetProgressMessage(
        OrganizationExecutionItemRecord item) =>
        item.Status switch
        {
            OrganizationExecutionItemStatus.Moved => "Movido",
            OrganizationExecutionItemStatus.SkippedConflict => "Omitido por conflicto",
            OrganizationExecutionItemStatus.ConflictNeedsDecision => "Conflicto pendiente",
            OrganizationExecutionItemStatus.Interrupted => "Interrumpido",
            OrganizationExecutionItemStatus.SourceMissing => "Ya no existe",
            OrganizationExecutionItemStatus.SourceChanged => "Cambió desde el análisis",
            OrganizationExecutionItemStatus.Error => "Error",
            _ => item.Status.ToString()
        };

    private static string BuildUniqueFileName(
        string directory,
        string baseName,
        string extension)
    {
        var candidate = baseName + extension;

        if (!File.Exists(Path.Combine(directory, candidate)))
        {
            return candidate;
        }

        var index = 2;

        do
        {
            candidate = $"{baseName}__{index}{extension}";
            index++;
        }
        while (File.Exists(Path.Combine(directory, candidate)));

        return candidate;
    }

    private static void EnsurePathInsideRoot(
        string path,
        string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = NormalizeDirectoryPath(root);
        var prefix = fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El destino calculado quedó fuera de la carpeta configurada.");
        }
    }

    private static bool IsSameOrInside(
        string candidate,
        string root)
    {
        var fullCandidate = NormalizeDirectoryPath(candidate);
        var fullRoot = NormalizeDirectoryPath(root);

        return fullCandidate.Equals(
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(path.Trim()))
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    private static void TryDelete(string? path)
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
