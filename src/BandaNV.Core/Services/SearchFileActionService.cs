using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace BandaNV.Core.Services;

public sealed class SearchFileActionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<SearchFileActionResult> MoveToCategoryAsync(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CategorySettings targetCategory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(targetCategory);

        var startedAt = DateTime.Now;
        var auditSources =
            CaptureAuditSources(
                settings,
                filePaths);

        var result = await Task.Run(
            () => MoveToCategory(
                settings,
                filePaths,
                targetCategory,
                cancellationToken),
            cancellationToken);

        await TryPersistAuditAsync(
            settings,
            "CHANGE_CATEGORY",
            startedAt,
            result,
            auditSources,
            targetCategory);

        return result;
    }

    public async Task<SearchFileActionResult> RenameAsync(
        AppSettings settings,
        string filePath,
        string proposedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt = DateTime.Now;
        var auditSources =
            CaptureAuditSources(
                settings,
                [filePath]);

        var result = await Task.Run(
            () => Rename(
                settings,
                filePath,
                proposedName,
                cancellationToken),
            cancellationToken);

        await TryPersistAuditAsync(
            settings,
            "RENAME",
            startedAt,
            result,
            auditSources,
            targetCategory: null);

        return result;
    }

    public async Task<SearchFileActionResult> DeleteAsync(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filePaths);

        var startedAt = DateTime.Now;
        var auditSources =
            CaptureAuditSources(
                settings,
                filePaths);

        var result = await Task.Run(
            () => Delete(
                settings,
                filePaths,
                cancellationToken),
            cancellationToken);

        await TryPersistAuditAsync(
            settings,
            "DELETE",
            startedAt,
            result,
            auditSources,
            targetCategory: null);

        return result;
    }

    private static SearchFileActionResult MoveToCategory(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CategorySettings targetCategory,
        CancellationToken cancellationToken)
    {
        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        EnsureDirectoryExists(
            destinationRoot);

        var targetFolder =
            CategoryService.GetFolderPath(
                destinationRoot,
                targetCategory.Order,
                targetCategory.Name);

        EnsurePathInsideRoot(
            targetFolder,
            destinationRoot);

        if (settings.CreateFolders)
        {
            Directory.CreateDirectory(
                targetFolder);
        }
        else if (!Directory.Exists(
                     targetFolder))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta de categoría no existe: {targetFolder}");
        }

        var results =
            new List<SearchFileActionItemResult>();

        foreach (var rawPath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source =
                Path.GetFullPath(
                    rawPath);

            EnsurePathInsideRoot(
                source,
                destinationRoot);

            var sourceIsDirectory =
                Directory.Exists(
                    source);

            if (!sourceIsDirectory &&
                !File.Exists(
                    source))
            {
                results.Add(
                    new SearchFileActionItemResult(
                        source,
                        null,
                        SearchFileActionStatus.Missing,
                        "El elemento ya no existe."));

                continue;
            }

            var desiredTarget =
                Path.Combine(
                    targetFolder,
                    Path.GetFileName(
                        source));

            if (PathsEqual(
                    source,
                    desiredTarget))
            {
                results.Add(
                    new SearchFileActionItemResult(
                        source,
                        source,
                        SearchFileActionStatus.Completed,
                        "El elemento ya pertenece a esa categoría."));

                continue;
            }

            var target =
                ResolveTargetPath(
                    settings.ConflictBehavior,
                    desiredTarget,
                    sourceIsDirectory);

            if (target is null)
            {
                results.Add(
                    new SearchFileActionItemResult(
                        source,
                        null,
                        SearchFileActionStatus.SkippedConflict,
                        "Ya existe un elemento con el mismo nombre en la categoría elegida."));

                continue;
            }

            EnsurePathInsideRoot(
                target,
                destinationRoot);

            try
            {
                OrganizationEntrySafety.MoveEntrySafely(
                    source,
                    target,
                    cancellationToken);

                results.Add(
                    new SearchFileActionItemResult(
                        source,
                        target,
                        SearchFileActionStatus.Completed,
                        null));
            }
            catch (Exception ex)
            {
                results.Add(
                    new SearchFileActionItemResult(
                        source,
                        null,
                        SearchFileActionStatus.Error,
                        ex.Message));
            }
        }

        return new SearchFileActionResult(
            results);
    }

    private static SearchFileActionResult Rename(
        AppSettings settings,
        string filePath,
        string proposedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        EnsureDirectoryExists(
            destinationRoot);

        var source =
            Path.GetFullPath(
                filePath);

        EnsurePathInsideRoot(
            source,
            destinationRoot);

        var isDirectory =
            Directory.Exists(
                source);

        if (!isDirectory &&
            !File.Exists(
                source))
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.Missing,
                    "El elemento ya no existe.")
            ]);
        }

        var name =
            proposedName.Trim();

        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "El nuevo nombre no puede estar vacío.",
                nameof(proposedName));
        }

        if (name.IndexOfAny(
                Path.GetInvalidFileNameChars()) >=
            0)
        {
            throw new ArgumentException(
                "El nombre contiene caracteres no permitidos.",
                nameof(proposedName));
        }

        if (!isDirectory &&
            string.IsNullOrWhiteSpace(
                Path.GetExtension(
                    name)))
        {
            name +=
                Path.GetExtension(
                    source);
        }

        var target =
            Path.Combine(
                Path.GetDirectoryName(
                    source)!,
                name);

        EnsurePathInsideRoot(
            target,
            destinationRoot);

        if (PathsEqual(
                source,
                target))
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    source,
                    SearchFileActionStatus.Completed,
                    "El nombre ya era el mismo.")
            ]);
        }

        if (OrganizationEntrySafety.Exists(
                target))
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.SkippedConflict,
                    "Ya existe un elemento con ese nombre en la misma carpeta.")
            ]);
        }

        try
        {
            OrganizationEntrySafety.MoveEntrySafely(
                source,
                target,
                cancellationToken);

            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    target,
                    SearchFileActionStatus.Completed,
                    null)
            ]);
        }
        catch (Exception ex)
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.Error,
                    ex.Message)
            ]);
        }
    }

    private static SearchFileActionResult Delete(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        EnsureDirectoryExists(
            destinationRoot);

        var results =
            new List<SearchFileActionItemResult>();

        foreach (var rawPath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path =
                Path.GetFullPath(
                    rawPath);

            EnsurePathInsideRoot(
                path,
                destinationRoot);

            var isDirectory =
                Directory.Exists(
                    path);

            if (!isDirectory &&
                !File.Exists(
                    path))
            {
                results.Add(
                    new SearchFileActionItemResult(
                        path,
                        null,
                        SearchFileActionStatus.Missing,
                        "El elemento ya no existe."));

                continue;
            }

            try
            {
                if (settings.UseRecycleBin)
                {
                    if (isDirectory)
                    {
                        FileSystem.DeleteDirectory(
                            path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        FileSystem.DeleteFile(
                            path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                    }
                }
                else if (isDirectory)
                {
                    Directory.Delete(
                        path,
                        recursive: true);
                }
                else
                {
                    File.Delete(
                        path);
                }

                results.Add(
                    new SearchFileActionItemResult(
                        path,
                        null,
                        SearchFileActionStatus.Completed,
                        null));
            }
            catch (Exception ex)
            {
                results.Add(
                    new SearchFileActionItemResult(
                        path,
                        null,
                        SearchFileActionStatus.Error,
                        ex.Message));
            }
        }

        return new SearchFileActionResult(
            results);
    }

    private static async Task TryPersistAuditAsync(
        AppSettings settings,
        string action,
        DateTime startedAt,
        SearchFileActionResult result,
        IReadOnlyList<SearchAuditSourceInfo> auditSources,
        CategorySettings? targetCategory)
    {
        try
        {
            await PersistAuditAsync(
                settings,
                action,
                startedAt,
                result,
                auditSources,
                targetCategory,
                CancellationToken.None);
        }
        catch
        {
            // La auditoría es secundaria al resultado físico. Si el elemento
            // ya fue modificado correctamente, un fallo de log no debe hacer
            // que Buscar informe una operación fallida.
        }
    }

    private static async Task PersistAuditAsync(
        AppSettings settings,
        string action,
        DateTime startedAt,
        SearchFileActionResult result,
        IReadOnlyList<SearchAuditSourceInfo> auditSources,
        CategorySettings? targetCategory,
        CancellationToken cancellationToken)
    {
        PortablePaths.EnsureDirectories();

        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        var sourcesByPath = auditSources
            .GroupBy(
                source => source.FullPath,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var record = new OrganizationExecutionRecord
        {
            Type = "SEARCH",
            Action = action,
            StartedAt = startedAt,
            FinishedAt = DateTime.Now,
            SourceFolder = destinationRoot,
            DestinationFolder =
                action.Equals(
                    "DELETE",
                    StringComparison.OrdinalIgnoreCase)
                    ? settings.UseRecycleBin
                        ? "Papelera de reciclaje"
                        : "Eliminación permanente"
                    : destinationRoot,
            ConflictBehavior =
                GetAuditBehavior(
                    settings,
                    action),
            Status = result.IssueCount == 0
                ? OrganizationExecutionStatus.Completed
                : OrganizationExecutionStatus.CompletedWithIssues
        };

        foreach (var resultItem in result.Items)
        {
            sourcesByPath.TryGetValue(
                Path.GetFullPath(
                    resultItem.SourcePath),
                out var sourceInfo);

            var category =
                action.Equals(
                    "CHANGE_CATEGORY",
                    StringComparison.OrdinalIgnoreCase) &&
                resultItem.Status ==
                    SearchFileActionStatus.Completed &&
                targetCategory is not null
                    ? new SearchAuditCategory(
                        targetCategory.Id,
                        targetCategory.Name,
                        targetCategory.Order)
                    : sourceInfo?.Category;

            var status =
                MapAuditStatus(
                    action,
                    resultItem);

            var message =
                resultItem.Message ??
                GetSuccessfulAuditMessage(
                    settings,
                    action,
                    resultItem,
                    sourceInfo);

            record.Items.Add(
                new OrganizationExecutionItemRecord
                {
                    FileName =
                        sourceInfo?.FileName ??
                        Path.GetFileName(
                            resultItem.SourcePath),
                    OriginalPath =
                        Path.GetFullPath(
                            resultItem.SourcePath),
                    FinalPath =
                        string.IsNullOrWhiteSpace(
                            resultItem.FinalPath)
                            ? null
                            : Path.GetFullPath(
                                resultItem.FinalPath),
                    CategoryId = category?.Id,
                    CategoryName = category?.Name,
                    CategoryOrder = category?.Order,
                    SizeBytes =
                        sourceInfo?.SizeBytes ?? 0,
                    ModifiedUtcTicks =
                        sourceInfo?.ModifiedUtcTicks ?? 0,
                    Kind =
                        sourceInfo?.Kind ??
                        OrganizationAnalysisItemKind.File,
                    ContainedFileCount =
                        sourceInfo?.ContainedFileCount ?? 1,
                    ContentFingerprint =
                        sourceInfo?.ContentFingerprint,
                    Status = status,
                    Message = message
                });
        }

        var baseName =
            BuildUniqueAuditBaseName(
                startedAt);

        var logPath = Path.Combine(
            PortablePaths.LogsDirectory,
            baseName + ".txt");

        await WriteTextAtomicAsync(
            logPath,
            OrganizationExecutionService.FormatHumanLog(
                record),
            cancellationToken);

        if (!settings.SaveSearchHistory)
        {
            return;
        }

        var historyPath = Path.Combine(
            PortablePaths.HistoryDirectory,
            baseName + ".json");

        await WriteTextAtomicAsync(
            historyPath,
            JsonSerializer.Serialize(
                record,
                JsonOptions),
            cancellationToken);
    }

    private static IReadOnlyList<SearchAuditSourceInfo> CaptureAuditSources(
        AppSettings settings,
        IEnumerable<string> paths)
    {
        var results =
            new List<SearchAuditSourceInfo>();

        foreach (var rawPath in paths)
        {
            try
            {
                var fullPath =
                    Path.GetFullPath(
                        rawPath);

                var category =
                    ResolveCategoryForPath(
                        settings,
                        fullPath);

                if (Directory.Exists(
                        fullPath))
                {
                    var snapshot =
                        OrganizationEntrySafety.GetDirectorySnapshot(
                            fullPath);

                    results.Add(
                        new SearchAuditSourceInfo(
                            fullPath,
                            Path.GetFileName(
                                fullPath),
                            snapshot.TotalSizeBytes,
                            snapshot.ModifiedUtcTicks,
                            OrganizationAnalysisItemKind.Folder,
                            snapshot.FileCount,
                            snapshot.ContentFingerprint,
                            category));
                }
                else if (File.Exists(
                             fullPath))
                {
                    var info =
                        new FileInfo(
                            fullPath);

                    results.Add(
                        new SearchAuditSourceInfo(
                            fullPath,
                            info.Name,
                            info.Length,
                            info.LastWriteTimeUtc.Ticks,
                            OrganizationAnalysisItemKind.File,
                            1,
                            null,
                            category));
                }
                else
                {
                    results.Add(
                        new SearchAuditSourceInfo(
                            fullPath,
                            Path.GetFileName(
                                fullPath),
                            0,
                            0,
                            OrganizationAnalysisItemKind.File,
                            1,
                            null,
                            category));
                }
            }
            catch
            {
                // La acción real volverá a validar la ruta. La captura de
                // auditoría no debe bloquearla.
            }
        }

        return results;
    }

    private static SearchAuditCategory? ResolveCategoryForPath(
        AppSettings settings,
        string filePath)
    {
        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);
        var parent =
            Path.GetDirectoryName(
                Path.GetFullPath(filePath));

        if (string.IsNullOrWhiteSpace(parent))
        {
            return null;
        }

        foreach (var category in settings.Categories)
        {
            var categoryFolder =
                CategoryService.GetFolderPath(
                    destinationRoot,
                    category.Order,
                    category.Name);

            if (PathsEqual(
                    parent,
                    categoryFolder))
            {
                return new SearchAuditCategory(
                    category.Id,
                    category.Name,
                    category.Order);
            }
        }

        return null;
    }

    private static OrganizationExecutionItemStatus MapAuditStatus(
        string action,
        SearchFileActionItemResult item)
    {
        if (item.Status ==
            SearchFileActionStatus.SkippedConflict)
        {
            return OrganizationExecutionItemStatus.SkippedConflict;
        }

        if (item.Status ==
            SearchFileActionStatus.Missing)
        {
            return OrganizationExecutionItemStatus.SourceMissing;
        }

        if (item.Status ==
            SearchFileActionStatus.Error)
        {
            return OrganizationExecutionItemStatus.Error;
        }

        if (action.Equals(
                "DELETE",
                StringComparison.OrdinalIgnoreCase))
        {
            return OrganizationExecutionItemStatus.Deleted;
        }

        if (action.Equals(
                "RENAME",
                StringComparison.OrdinalIgnoreCase))
        {
            return PathsEqual(
                    item.SourcePath,
                    item.FinalPath ??
                    item.SourcePath)
                ? OrganizationExecutionItemStatus.CompletedAction
                : OrganizationExecutionItemStatus.Renamed;
        }

        if (action.Equals(
                "CHANGE_CATEGORY",
                StringComparison.OrdinalIgnoreCase))
        {
            return PathsEqual(
                    item.SourcePath,
                    item.FinalPath ??
                    item.SourcePath)
                ? OrganizationExecutionItemStatus.CompletedAction
                : OrganizationExecutionItemStatus.Moved;
        }

        return OrganizationExecutionItemStatus.CompletedAction;
    }

    private static string? GetSuccessfulAuditMessage(
        AppSettings settings,
        string action,
        SearchFileActionItemResult item,
        SearchAuditSourceInfo? sourceInfo)
    {
        if (action.Equals(
                "DELETE",
                StringComparison.OrdinalIgnoreCase))
        {
            return settings.UseRecycleBin
                ? "Enviado a la Papelera."
                : "Eliminado permanentemente.";
        }

        if (action.Equals(
                "RENAME",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                item.FinalPath))
        {
            var originalName =
                sourceInfo?.FileName ??
                Path.GetFileName(
                    item.SourcePath);
            var finalName =
                Path.GetFileName(
                    item.FinalPath);

            return $"Renombrado de \"{originalName}\" a \"{finalName}\".";
        }

        return null;
    }

    private static string GetAuditBehavior(
        AppSettings settings,
        string action) =>
        action.ToUpperInvariant() switch
        {
            "CHANGE_CATEGORY" =>
                settings.ConflictBehavior,
            "RENAME" =>
                "No sobrescribir",
            "DELETE" =>
                settings.UseRecycleBin
                    ? "Papelera de reciclaje"
                    : "Eliminación permanente",
            _ => string.Empty
        };

    private static string? ResolveTargetPath(
        string behavior,
        string desiredTarget,
        bool isDirectory)
    {
        if (!OrganizationEntrySafety.Exists(
                desiredTarget))
        {
            return desiredTarget;
        }

        if (behavior.Equals(
                "Renombrar automáticamente",
                StringComparison.OrdinalIgnoreCase))
        {
            return GetUniqueDestination(
                desiredTarget,
                isDirectory);
        }

        // Buscar nunca reemplaza silenciosamente un elemento existente.
        // Preguntar, Omitir y Reemplazar se resuelven como conflicto seguro.
        return null;
    }

    private static string GetUniqueDestination(
        string desiredTarget,
        bool isDirectory)
    {
        var directory =
            Path.GetDirectoryName(
                desiredTarget)!;

        var name =
            isDirectory
                ? Path.GetFileName(
                    desiredTarget)
                : Path.GetFileNameWithoutExtension(
                    desiredTarget);

        var extension =
            isDirectory
                ? string.Empty
                : Path.GetExtension(
                    desiredTarget);

        var index =
            2;

        string candidate;

        do
        {
            candidate =
                Path.Combine(
                    directory,
                    $"{name} ({index}){extension}");

            index++;
        }
        while (OrganizationEntrySafety.Exists(
            candidate));

        return candidate;
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        var temporaryPath =
            path + $".tmp_{Guid.NewGuid():N}";

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
            TryDelete(temporaryPath);
        }
    }

    private static string BuildUniqueAuditBaseName(
        DateTime startedAt)
    {
        var baseName =
            $"BandaNV_{startedAt:dd-MM-yyyy____HH-mm-ss}";
        var candidate = baseName;
        var index = 2;

        while (File.Exists(
                   Path.Combine(
                       PortablePaths.LogsDirectory,
                       candidate + ".txt")) ||
               File.Exists(
                   Path.Combine(
                       PortablePaths.HistoryDirectory,
                       candidate + ".json")))
        {
            candidate =
                $"{baseName}__{index}";
            index++;
        }

        return candidate;
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                "La carpeta destino configurada no existe.");
        }
    }

    private static void EnsurePathInsideRoot(
        string path,
        string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = NormalizeDirectoryPath(root);

        if (PathsEqual(fullPath, fullRoot))
        {
            return;
        }

        var prefix =
            fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La operación quedó fuera de la carpeta destino configurada.");
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record SearchAuditCategory(
        string Id,
        string Name,
        int Order);

    private sealed record SearchAuditSourceInfo(
        string FullPath,
        string FileName,
        long SizeBytes,
        long ModifiedUtcTicks,
        OrganizationAnalysisItemKind Kind,
        int ContainedFileCount,
        string? ContentFingerprint,
        SearchAuditCategory? Category);
}
