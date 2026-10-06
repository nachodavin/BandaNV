using System.Security.Cryptography;
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
                Kind = item.Kind,
                ContainedFileCount = item.ContainedFileCount,
                ContentFingerprint = item.ContentFingerprint,
                Status =
                    item.CategoryOrder.HasValue &&
                    !string.IsNullOrWhiteSpace(item.CategoryName)
                        ? OrganizationExecutionItemStatus.Planned
                        : OrganizationExecutionItemStatus.SkippedUnclassified,
                Message =
                    item.CategoryOrder.HasValue &&
                    !string.IsNullOrWhiteSpace(item.CategoryName)
                        ? null
                        : "El elemento no tiene una categoría asignada."
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

            if (settings.DeleteEmptyFolders)
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

        if (item.IsDirectory)
        {
            await ExecuteDirectoryItemAsync(
                settings,
                destinationRoot,
                executionId,
                record,
                item,
                historyPath,
                logPath,
                conflictResolver,
                conflictState,
                cancellationToken);

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
            settings.ConfirmDestructiveActions,
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
            if (EntryExists(target) &&
                targetResolution.Action == OrganizationConflictAction.Replace)
            {
                var replacedInfo =
                    GetEntryMetadata(
                        target,
                        cancellationToken);

                item.ReplacedSizeBytes =
                    replacedInfo.SizeBytes;
                item.ReplacedModifiedUtcTicks =
                    replacedInfo.ModifiedUtcTicks;
                item.ReplacedKind =
                    replacedInfo.IsDirectory
                        ? OrganizationAnalysisItemKind.Folder
                        : OrganizationAnalysisItemKind.File;
                item.ReplacedContainedFileCount =
                    replacedInfo.ItemCount;
                item.ReplacedContentFingerprint =
                    replacedInfo.ContentFingerprint;

                replacedBackupPath =
                    BackupReplacedEntry(
                        target,
                        executionId,
                        item.UndoId);

                item.ReplacedBackupPath =
                    replacedBackupPath;

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
                EntryExists(replacedBackupPath) &&
                !EntryExists(target))
            {
                try
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(target)!);

                    MoveEntrySafely(
                        replacedBackupPath,
                        target,
                        CancellationToken.None);

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

    private static async Task ExecuteDirectoryItemAsync(
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
        if (!Directory.Exists(item.OriginalPath))
        {
            item.Status =
                OrganizationExecutionItemStatus.SourceMissing;
            item.Message =
                "La carpeta ya no existe en el origen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(
                item.ContentFingerprint))
        {
            item.Status =
                OrganizationExecutionItemStatus.SourceChanged;
            item.Message =
                "La carpeta no pudo analizarse por completo. Se requiere un nuevo análisis antes de moverla.";
            return;
        }

        DirectorySnapshot sourceSnapshot;

        try
        {
            sourceSnapshot =
                BuildDirectorySnapshot(
                    item.OriginalPath,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            item.Status =
                OrganizationExecutionItemStatus.Error;
            item.Message =
                $"No se pudo validar la carpeta antes de moverla: {ex.Message}";
            return;
        }

        if (sourceSnapshot.FileCount !=
                item.ContainedFileCount ||
            sourceSnapshot.TotalSizeBytes !=
                item.SizeBytes ||
            !sourceSnapshot.ContentFingerprint.Equals(
                item.ContentFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            item.Status =
                OrganizationExecutionItemStatus.SourceChanged;
            item.Message =
                "El contenido de la carpeta cambió desde el análisis. No se movió.";
            return;
        }

        if (!item.CategoryOrder.HasValue ||
            string.IsNullOrWhiteSpace(
                item.CategoryName))
        {
            item.Status =
                OrganizationExecutionItemStatus.SkippedUnclassified;
            item.Message =
                "Sin categoría.";
            return;
        }

        var categoryFolder =
            CategoryService.GetFolderPath(
                destinationRoot,
                item.CategoryOrder.Value,
                item.CategoryName);

        if (settings.CreateFolders)
        {
            Directory.CreateDirectory(
                categoryFolder);
        }
        else if (!Directory.Exists(
                     categoryFolder))
        {
            item.Status =
                OrganizationExecutionItemStatus.Error;
            item.Message =
                $"La carpeta de categoría no existe: {categoryFolder}";
            return;
        }

        var desiredTarget =
            Path.Combine(
                categoryFolder,
                item.FileName);

        var targetResolution =
            await ResolveDirectoryTargetPathAsync(
                settings.ConflictBehavior,
                settings.ConfirmDestructiveActions,
                desiredTarget,
                sourceSnapshot,
                item,
                conflictResolver,
                conflictState,
                cancellationToken);

        var target =
            targetResolution.Target;

        if (target is null)
        {
            return;
        }

        EnsurePathInsideRoot(
            target,
            destinationRoot);

        item.FinalPath =
            target;
        item.Status =
            OrganizationExecutionItemStatus.Moving;
        item.Message =
            "Movimiento de carpeta iniciado.";

        await PersistRecordAsync(
            record,
            historyPath,
            logPath,
            cancellationToken);

        string? replacedBackupPath =
            null;

        try
        {
            if (EntryExists(target) &&
                targetResolution.Action ==
                OrganizationConflictAction.Replace)
            {
                var replacementInfo =
                    GetEntryMetadata(
                        target,
                        cancellationToken);

                item.ReplacedSizeBytes =
                    replacementInfo.SizeBytes;
                item.ReplacedModifiedUtcTicks =
                    replacementInfo.ModifiedUtcTicks;
                item.ReplacedKind =
                    replacementInfo.IsDirectory
                        ? OrganizationAnalysisItemKind.Folder
                        : OrganizationAnalysisItemKind.File;
                item.ReplacedContainedFileCount =
                    replacementInfo.ItemCount;
                item.ReplacedContentFingerprint =
                    replacementInfo.ContentFingerprint;

                replacedBackupPath =
                    BackupReplacedEntry(
                        target,
                        executionId,
                        item.UndoId);

                item.ReplacedBackupPath =
                    replacedBackupPath;

                await PersistRecordAsync(
                    record,
                    historyPath,
                    logPath,
                    cancellationToken);
            }

            MoveDirectorySafely(
                item.OriginalPath,
                target,
                cancellationToken);

            item.Status =
                OrganizationExecutionItemStatus.Moved;
            item.Message =
                null;
        }
        catch (Exception ex)
        {
            item.Status =
                OrganizationExecutionItemStatus.Error;
            item.Message =
                ex.Message;

            if (!string.IsNullOrWhiteSpace(
                    replacedBackupPath) &&
                EntryExists(
                    replacedBackupPath) &&
                !EntryExists(
                    target))
            {
                try
                {
                    MoveEntrySafely(
                        replacedBackupPath,
                        target,
                        CancellationToken.None);

                    item.ReplacedBackupPath =
                        null;
                }
                catch
                {
                    item.Message +=
                        " Además, no se pudo restaurar automáticamente la carpeta o archivo reemplazado.";
                }
            }
        }
    }

    private static async Task<TargetResolution> ResolveDirectoryTargetPathAsync(
        string behavior,
        bool confirmDestructiveActions,
        string desiredTarget,
        DirectorySnapshot sourceSnapshot,
        OrganizationExecutionItemRecord item,
        IOrganizationConflictResolver? conflictResolver,
        ConflictResolutionState conflictState,
        CancellationToken cancellationToken)
    {
        if (!EntryExists(
                desiredTarget))
        {
            return new TargetResolution(
                desiredTarget,
                null);
        }

        if (behavior.Equals(
                "Renombrar automáticamente",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution =
                "Renombrar automáticamente";

            return new TargetResolution(
                GetUniqueDirectoryDestination(
                    desiredTarget),
                OrganizationConflictAction.Rename);
        }

        if (behavior.Equals(
                "Omitir elemento",
                StringComparison.OrdinalIgnoreCase) ||
            behavior.Equals(
                "Omitir archivo",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution =
                "Omitir elemento";
            item.Status =
                OrganizationExecutionItemStatus.SkippedConflict;
            item.Message =
                "Ya existe un elemento con el mismo nombre en el destino.";

            return new TargetResolution(
                null,
                OrganizationConflictAction.Skip);
        }

        if (behavior.Equals(
                "Reemplazar",
                StringComparison.OrdinalIgnoreCase) &&
            !confirmDestructiveActions)
        {
            item.ConflictResolution =
                "Reemplazar";

            return new TargetResolution(
                desiredTarget,
                OrganizationConflictAction.Replace);
        }

        if (conflictResolver is null)
        {
            item.Status =
                OrganizationExecutionItemStatus.ConflictNeedsDecision;
            item.Message =
                "Ya existe un elemento con el mismo nombre y se requiere una decisión antes de reemplazarlo o moverlo.";

            return new TargetResolution(
                null,
                null);
        }

        OrganizationConflictResolution resolution;

        if (conflictState.ApplyToRemaining is
            { } rememberedAction)
        {
            resolution =
                new OrganizationConflictResolution(
                    rememberedAction,
                    ApplyToRemaining: true);
        }
        else
        {
            EntryMetadata destinationMetadata;

            try
            {
                destinationMetadata =
                    GetEntryMetadata(
                        desiredTarget,
                        cancellationToken);
            }
            catch (Exception ex)
            {
                item.Status =
                    OrganizationExecutionItemStatus.Error;
                item.Message =
                    $"No se pudo inspeccionar el elemento existente en destino: {ex.Message}";

                return new TargetResolution(
                    null,
                    null);
            }

            var conflict =
                new OrganizationConflictInfo(
                    item.FileName,
                    item.OriginalPath,
                    desiredTarget,
                    sourceSnapshot.TotalSizeBytes,
                    destinationMetadata.SizeBytes,
                    new DateTime(
                        sourceSnapshot.ModifiedUtcTicks,
                        DateTimeKind.Utc)
                        .ToLocalTime(),
                    new DateTime(
                        destinationMetadata.ModifiedUtcTicks,
                        DateTimeKind.Utc)
                        .ToLocalTime(),
                    IsDirectory: true,
                    SourceItemCount:
                        sourceSnapshot.FileCount,
                    DestinationItemCount:
                        destinationMetadata.ItemCount,
                    DestinationIsDirectory:
                        destinationMetadata.IsDirectory);

            resolution =
                await conflictResolver.ResolveAsync(
                    conflict,
                    cancellationToken);

            if (resolution.ApplyToRemaining)
            {
                conflictState.ApplyToRemaining =
                    resolution.Action;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (resolution.Action)
        {
            case OrganizationConflictAction.Rename:
                item.ConflictResolution =
                    "Renombrar automáticamente";

                return new TargetResolution(
                    GetUniqueDirectoryDestination(
                        desiredTarget),
                    OrganizationConflictAction.Rename);

            case OrganizationConflictAction.Replace:
                item.ConflictResolution =
                    "Reemplazar";

                return new TargetResolution(
                    desiredTarget,
                    OrganizationConflictAction.Replace);

            default:
                item.ConflictResolution =
                    "Omitir archivo";
                item.Status =
                    OrganizationExecutionItemStatus.SkippedConflict;
                item.Message =
                    "Omitido por decisión del usuario.";

                return new TargetResolution(
                    null,
                    OrganizationConflictAction.Skip);
        }
    }

    private static async Task<TargetResolution> ResolveTargetPathAsync(
        string behavior,
        bool confirmDestructiveActions,
        string desiredTarget,
        FileInfo sourceInfo,
        OrganizationExecutionItemRecord item,
        IOrganizationConflictResolver? conflictResolver,
        ConflictResolutionState conflictState,
        CancellationToken cancellationToken)
    {
        if (!EntryExists(desiredTarget))
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
                "Omitir elemento",
                StringComparison.OrdinalIgnoreCase) ||
            behavior.Equals(
                "Omitir archivo",
                StringComparison.OrdinalIgnoreCase))
        {
            item.ConflictResolution = "Omitir elemento";
            item.Status = OrganizationExecutionItemStatus.SkippedConflict;
            item.Message =
                "Ya existe un elemento con el mismo nombre en el destino.";
            return new TargetResolution(
                null,
                OrganizationConflictAction.Skip);
        }

        if (behavior.Equals(
                "Reemplazar",
                StringComparison.OrdinalIgnoreCase) &&
            !confirmDestructiveActions)
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
                "Ya existe un elemento con el mismo nombre y se requiere una decisión antes de reemplazarlo o moverlo.";
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
                var destinationInfo =
                    GetEntryMetadata(
                        desiredTarget,
                        cancellationToken);

                conflict = new OrganizationConflictInfo(
                    item.FileName,
                    item.OriginalPath,
                    desiredTarget,
                    sourceInfo.Length,
                    destinationInfo.SizeBytes,
                    sourceInfo.LastWriteTime,
                    new DateTime(
                        destinationInfo.ModifiedUtcTicks,
                        DateTimeKind.Utc)
                        .ToLocalTime(),
                    IsDirectory: false,
                    SourceItemCount: 1,
                    DestinationItemCount:
                        destinationInfo.ItemCount,
                    DestinationIsDirectory:
                        destinationInfo.IsDirectory);
            }
            catch (FileNotFoundException)
            {
                return new TargetResolution(desiredTarget, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new TargetResolution(desiredTarget, null);
            }
            catch (Exception ex)
            {
                item.Status =
                    OrganizationExecutionItemStatus.Error;
                item.Message =
                    $"No se pudo inspeccionar el elemento existente en destino: {ex.Message}";

                return new TargetResolution(
                    null,
                    null);
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
                item.ConflictResolution = "Omitir elemento";
                item.Status = OrganizationExecutionItemStatus.SkippedConflict;
                item.Message = "Omitido por decisión del usuario.";
                return new TargetResolution(
                    null,
                    OrganizationConflictAction.Skip);
        }
    }

    private sealed record DirectorySnapshot(
        int FileCount,
        long TotalSizeBytes,
        long ModifiedUtcTicks,
        string ContentFingerprint);

    private sealed record EntryMetadata(
        long SizeBytes,
        long ModifiedUtcTicks,
        int ItemCount,
        bool IsDirectory,
        string? ContentFingerprint);

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

                if (!EntryExists(backupPath) ||
                    TryDeleteAndConfirm(backupPath))
                {
                    // La copia protegida deja de existir cuando Undo está
                    // desactivado, pero la metadata del elemento reemplazado
                    // sigue formando parte del registro histórico/auditoría.
                    item.ReplacedBackupPath = null;
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
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);

                return !Directory.Exists(
                    path);
            }

            File.Delete(
                path);

            return !File.Exists(
                path);
        }
        catch
        {
            return false;
        }
    }

    private static bool EntryExists(
        string path) =>
        File.Exists(path) ||
        Directory.Exists(path);

    private static EntryMetadata GetEntryMetadata(
        string path,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            var snapshot =
                BuildDirectorySnapshot(
                    path,
                    cancellationToken);

            return new EntryMetadata(
                snapshot.TotalSizeBytes,
                snapshot.ModifiedUtcTicks,
                snapshot.FileCount,
                IsDirectory: true,
                ContentFingerprint:
                    snapshot.ContentFingerprint);
        }

        var file =
            new FileInfo(
                path);

        return new EntryMetadata(
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            ItemCount: 1,
            IsDirectory: false,
            ContentFingerprint: null);
    }

    private static DirectorySnapshot BuildDirectorySnapshot(
        string root,
        CancellationToken cancellationToken)
    {
        var normalizedRoot =
            Path.GetFullPath(
                root);

        if (!Directory.Exists(
                normalizedRoot))
        {
            throw new DirectoryNotFoundException(
                "La carpeta ya no existe.");
        }

        var rootAttributes =
            File.GetAttributes(
                normalizedRoot);

        if ((rootAttributes &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "La carpeta es un vínculo o punto de reanálisis y no puede moverse como unidad.");
        }

        var pending =
            new Stack<string>();

        pending.Push(
            normalizedRoot);

        var fingerprintParts =
            new List<string>();

        var fileCount =
            0;

        var totalSize =
            0L;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current =
                pending.Pop();

            string[] files;
            string[] directories;

            try
            {
                files =
                    Directory.GetFiles(
                        current,
                        "*",
                        SearchOption.TopDirectoryOnly);

                directories =
                    Directory.GetDirectories(
                        current,
                        "*",
                        SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"No se pudo leer completamente la carpeta: {current}",
                    ex);
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attributes =
                    File.GetAttributes(
                        filePath);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {filePath}");
                }

                var info =
                    new FileInfo(
                        filePath);

                var relativePath =
                    Path.GetRelativePath(
                        normalizedRoot,
                        info.FullName);

                fileCount++;
                totalSize +=
                    info.Length;

                fingerprintParts.Add(
                    $"{relativePath}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}");
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativeDirectoryPath =
                    Path.GetRelativePath(
                        normalizedRoot,
                        directory);

                fingerprintParts.Add(
                    $"DIR\0{relativeDirectoryPath}");

                var attributes =
                    File.GetAttributes(
                        directory);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {directory}");
                }

                pending.Push(
                    directory);
            }
        }

        var payload =
            string.Join(
                "\n",
                fingerprintParts
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase));

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    payload));

        return new DirectorySnapshot(
            fileCount,
            totalSize,
            Directory.GetLastWriteTimeUtc(
                    normalizedRoot)
                .Ticks,
            Convert.ToHexString(
                    hash)
                .ToLowerInvariant());
    }

    private static string GetUniqueDirectoryDestination(
        string desiredTarget)
    {
        if (!EntryExists(
                desiredTarget))
        {
            return desiredTarget;
        }

        var parent =
            Path.GetDirectoryName(
                desiredTarget)
            ?? throw new InvalidOperationException(
                "No se pudo determinar la carpeta de destino.");

        var name =
            Path.GetFileName(
                desiredTarget);

        var index =
            2;

        string candidate;

        do
        {
            candidate =
                Path.Combine(
                    parent,
                    $"{name} ({index})");

            index++;
        }
        while (EntryExists(
            candidate));

        return candidate;
    }

    private static string BackupReplacedEntry(
        string target,
        string executionId,
        string undoId)
    {
        var backupDirectory =
            Path.Combine(
                PortablePaths.HistoryDirectory,
                "replaced",
                executionId);

        Directory.CreateDirectory(
            backupDirectory);

        var backupPath =
            Path.Combine(
                backupDirectory,
                $"{undoId}_{Path.GetFileName(target)}");

        MoveEntrySafely(
            target,
            backupPath,
            CancellationToken.None);

        return backupPath;
    }

    private static void MoveEntrySafely(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(
                source))
        {
            MoveDirectorySafely(
                source,
                destination,
                cancellationToken);

            return;
        }

        if (File.Exists(
                source))
        {
            MoveFileSafely(
                source,
                destination);

            return;
        }

        throw new FileNotFoundException(
            "El elemento que debía moverse ya no existe.",
            source);
    }

    private static void MoveDirectorySafely(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(
                source))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta de origen ya no existe: {source}");
        }

        if (EntryExists(
                destination))
        {
            throw new IOException(
                $"Ya existe un elemento en el destino: {destination}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                destination)!);

        try
        {
            Directory.Move(
                source,
                destination);

            return;
        }
        catch (IOException)
        {
            if (!Directory.Exists(
                    source) ||
                EntryExists(
                    destination))
            {
                throw;
            }

            // Entre unidades distintas Directory.Move no puede completar
            // la operación. Se usa una copia temporal verificable.
        }

        var temporaryDestination =
            destination +
            $".bandanv_tmp_{Guid.NewGuid():N}";

        try
        {
            CopyDirectoryTree(
                source,
                temporaryDestination,
                cancellationToken);

            var sourceSnapshot =
                BuildDirectorySnapshot(
                    source,
                    cancellationToken);

            var copySnapshot =
                BuildDirectorySnapshot(
                    temporaryDestination,
                    cancellationToken);

            if (sourceSnapshot.FileCount !=
                    copySnapshot.FileCount ||
                sourceSnapshot.TotalSizeBytes !=
                    copySnapshot.TotalSizeBytes ||
                !sourceSnapshot.ContentFingerprint.Equals(
                    copySnapshot.ContentFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "La copia de la carpeta entre unidades no pudo validarse.");
            }

            Directory.Move(
                temporaryDestination,
                destination);

            try
            {
                Directory.Delete(
                    source,
                    recursive: true);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "La carpeta se copió y verificó correctamente en destino, pero no se pudo eliminar por completo el origen. Se conservó la copia de destino para evitar pérdida de datos.",
                    ex);
            }
        }
        finally
        {
            if (Directory.Exists(
                    temporaryDestination))
            {
                try
                {
                    Directory.Delete(
                        temporaryDestination,
                        recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    private static void CopyDirectoryTree(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var normalizedSource =
            Path.GetFullPath(
                source);

        var pending =
            new Stack<(string Source, string Destination)>();

        pending.Push(
            (normalizedSource, destination));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current =
                pending.Pop();

            var attributes =
                File.GetAttributes(
                    current.Source);

            if ((attributes &
                 FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"No se puede copiar un vínculo o punto de reanálisis: {current.Source}");
            }

            Directory.CreateDirectory(
                current.Destination);

            foreach (var filePath in Directory.GetFiles(
                         current.Source,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileAttributes =
                    File.GetAttributes(
                        filePath);

                if ((fileAttributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {filePath}");
                }

                var destinationFile =
                    Path.Combine(
                        current.Destination,
                        Path.GetFileName(
                            filePath));

                File.Copy(
                    filePath,
                    destinationFile,
                    overwrite: false);

                File.SetLastWriteTimeUtc(
                    destinationFile,
                    File.GetLastWriteTimeUtc(
                        filePath));
            }

            foreach (var directoryPath in Directory.GetDirectories(
                         current.Source,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directoryAttributes =
                    File.GetAttributes(
                        directoryPath);

                if ((directoryAttributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {directoryPath}");
                }

                pending.Push(
                    (
                        directoryPath,
                        Path.Combine(
                            current.Destination,
                            Path.GetFileName(
                                directoryPath))));
            }
        }
    }

    private static string GetUniqueDestination(string desiredTarget)
    {
        if (!EntryExists(desiredTarget))
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
        while (EntryExists(candidate));

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
            $"Tipo: {GetExecutionTypeDisplayName(record)}");

        if ((record.Type.Equals(
                 "SEARCH",
                 StringComparison.OrdinalIgnoreCase) ||
             record.Type.Equals(
                 "ORGANIZE_ACTION",
                 StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(record.Action))
        {
            builder.AppendLine(
                $"Acción: {GetSearchActionDisplayName(record.Action)}");
        }

        var formatVersion =
            record.Format.Replace(
                "BandaNV.Execution.",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        builder.AppendLine(
            $"Formato: {formatVersion}");
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

            builder.AppendLine(
                $"         Tipo: {(item.IsDirectory ? "CARPETA" : "ARCHIVO")}");

            if (item.IsDirectory)
            {
                builder.AppendLine(
                    $"         Contenido: {item.ContainedFileCount} archivo{(item.ContainedFileCount == 1 ? string.Empty : "s")}");
            }

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
        builder.AppendLine($"Archivos procesados: {record.SuccessfulCount}");
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

    private static string GetExecutionTypeDisplayName(
        OrganizationExecutionRecord record)
    {
        if (record.Type.Equals(
                "UNDO",
                StringComparison.OrdinalIgnoreCase))
        {
            return "DESHACER";
        }

        if (record.Type.Equals(
                "SEARCH",
                StringComparison.OrdinalIgnoreCase))
        {
            return "BUSCAR";
        }

        if (record.Type.Equals(
                "ORGANIZE_ACTION",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ORGANIZAR · ACCIÓN";
        }

        return "ORGANIZAR";
    }

    private static string GetSearchActionDisplayName(
        string action) =>
        action.ToUpperInvariant() switch
        {
            "CHANGE_CATEGORY" => "CAMBIAR CATEGORÍA",
            "RENAME" => "RENOMBRAR",
            "DELETE" => "ELIMINAR",
            _ => action
        };

    private static string GetLogStatus(
        OrganizationExecutionItemStatus status) =>
        status switch
        {
            OrganizationExecutionItemStatus.Moved => "MOVIDO",
            OrganizationExecutionItemStatus.Renamed => "RENOMBRADO",
            OrganizationExecutionItemStatus.Deleted => "ELIMINADO",
            OrganizationExecutionItemStatus.CompletedAction => "COMPLETADO",
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
            OrganizationExecutionItemStatus.Renamed => "Renombrado",
            OrganizationExecutionItemStatus.Deleted => "Eliminado",
            OrganizationExecutionItemStatus.CompletedAction => "Completado",
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
