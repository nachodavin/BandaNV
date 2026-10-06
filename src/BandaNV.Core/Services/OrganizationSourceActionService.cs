using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace BandaNV.Core.Services;

public sealed class OrganizationSourceActionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<OrganizationSourceActionResult> RenameAsync(
        AppSettings settings,
        string entryPath,
        string proposedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt =
            DateTime.Now;

        var auditSources =
            CaptureAuditSources(
                [entryPath]);

        var result =
            await Task.Run(
                () => Rename(
                    settings,
                    entryPath,
                    proposedName,
                    cancellationToken),
                cancellationToken);

        await TryPersistAuditAsync(
            settings,
            "RENAME",
            startedAt,
            result,
            auditSources);

        return result;
    }

    public async Task<OrganizationSourceActionResult> DeleteAsync(
        AppSettings settings,
        IReadOnlyList<string> entryPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(entryPaths);

        var startedAt =
            DateTime.Now;

        var auditSources =
            CaptureAuditSources(
                entryPaths);

        var result =
            await Task.Run(
                () => Delete(
                    settings,
                    entryPaths,
                    cancellationToken),
                cancellationToken);

        await TryPersistAuditAsync(
            settings,
            "DELETE",
            startedAt,
            result,
            auditSources);

        return result;
    }

    private static OrganizationSourceActionResult Rename(
        AppSettings settings,
        string entryPath,
        string proposedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceRoot =
            NormalizeDirectoryPath(
                settings.SourceFolder);

        EnsureSourceRootExists(
            sourceRoot);

        var source =
            Path.GetFullPath(
                entryPath);

        EnsureEditablePathInsideSource(
            source,
            sourceRoot);

        var isDirectory =
            Directory.Exists(
                source);

        if (!isDirectory &&
            !File.Exists(
                source))
        {
            return new OrganizationSourceActionResult(
            [
                new OrganizationSourceActionItemResult(
                    source,
                    null,
                    OrganizationSourceActionStatus.Missing,
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
                Path.GetInvalidFileNameChars()) >= 0)
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

        var parent =
            Path.GetDirectoryName(
                source) ??
            throw new InvalidOperationException(
                "No se pudo determinar la carpeta contenedora.");

        var target =
            Path.Combine(
                parent,
                name);

        EnsureEditablePathInsideSource(
            target,
            sourceRoot);

        if (PathsEqual(
                source,
                target))
        {
            return new OrganizationSourceActionResult(
            [
                new OrganizationSourceActionItemResult(
                    source,
                    source,
                    OrganizationSourceActionStatus.Completed,
                    "El nombre ya era el mismo.")
            ]);
        }

        if (OrganizationEntrySafety.Exists(
                target))
        {
            return new OrganizationSourceActionResult(
            [
                new OrganizationSourceActionItemResult(
                    source,
                    null,
                    OrganizationSourceActionStatus.Conflict,
                    "Ya existe un elemento con ese nombre en la misma carpeta.")
            ]);
        }

        try
        {
            OrganizationEntrySafety.MoveEntrySafely(
                source,
                target,
                cancellationToken);

            return new OrganizationSourceActionResult(
            [
                new OrganizationSourceActionItemResult(
                    source,
                    target,
                    OrganizationSourceActionStatus.Completed,
                    null)
            ]);
        }
        catch (Exception ex)
        {
            return new OrganizationSourceActionResult(
            [
                new OrganizationSourceActionItemResult(
                    source,
                    null,
                    OrganizationSourceActionStatus.Error,
                    ex.Message)
            ]);
        }
    }

    private static OrganizationSourceActionResult Delete(
        AppSettings settings,
        IReadOnlyList<string> entryPaths,
        CancellationToken cancellationToken)
    {
        var sourceRoot =
            NormalizeDirectoryPath(
                settings.SourceFolder);

        EnsureSourceRootExists(
            sourceRoot);

        var results =
            new List<OrganizationSourceActionItemResult>();

        foreach (var rawPath in entryPaths
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path =
                Path.GetFullPath(
                    rawPath);

            EnsureEditablePathInsideSource(
                path,
                sourceRoot);

            var isDirectory =
                Directory.Exists(
                    path);

            if (!isDirectory &&
                !File.Exists(
                    path))
            {
                results.Add(
                    new OrganizationSourceActionItemResult(
                        path,
                        null,
                        OrganizationSourceActionStatus.Missing,
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
                    new OrganizationSourceActionItemResult(
                        path,
                        null,
                        OrganizationSourceActionStatus.Completed,
                        null));
            }
            catch (Exception ex)
            {
                results.Add(
                    new OrganizationSourceActionItemResult(
                        path,
                        null,
                        OrganizationSourceActionStatus.Error,
                        ex.Message));
            }
        }

        return new OrganizationSourceActionResult(
            results);
    }

    private static async Task TryPersistAuditAsync(
        AppSettings settings,
        string action,
        DateTime startedAt,
        OrganizationSourceActionResult result,
        IReadOnlyList<OrganizationSourceAuditInfo> auditSources)
    {
        try
        {
            await PersistAuditAsync(
                settings,
                action,
                startedAt,
                result,
                auditSources,
                CancellationToken.None);
        }
        catch
        {
            // La auditoría es secundaria al cambio físico. Si la acción
            // terminó correctamente, un fallo al escribir el registro no
            // debe convertirla en una operación fallida.
        }
    }

    private static async Task PersistAuditAsync(
        AppSettings settings,
        string action,
        DateTime startedAt,
        OrganizationSourceActionResult result,
        IReadOnlyList<OrganizationSourceAuditInfo> auditSources,
        CancellationToken cancellationToken)
    {
        PortablePaths.EnsureDirectories();

        var sourceRoot =
            NormalizeDirectoryPath(
                settings.SourceFolder);

        var sourcesByPath =
            auditSources
                .GroupBy(
                    source => source.FullPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        var record =
            new OrganizationExecutionRecord
            {
                Type =
                    "ORGANIZE_ACTION",
                ShowInHistory =
                    settings.SaveOrganizeHistory,
                Action =
                    action,
                StartedAt =
                    startedAt,
                FinishedAt =
                    DateTime.Now,
                SourceFolder =
                    sourceRoot,
                DestinationFolder =
                    action.Equals(
                        "DELETE",
                        StringComparison.OrdinalIgnoreCase)
                        ? settings.UseRecycleBin
                            ? "Papelera de reciclaje"
                            : "Eliminación permanente"
                        : sourceRoot,
                ConflictBehavior =
                    action.Equals(
                        "DELETE",
                        StringComparison.OrdinalIgnoreCase)
                        ? settings.UseRecycleBin
                            ? "Papelera de reciclaje"
                            : "Eliminación permanente"
                        : "No sobrescribir",
                Status =
                    result.HasErrors
                        ? OrganizationExecutionStatus.CompletedWithIssues
                        : OrganizationExecutionStatus.Completed
            };

        foreach (var resultItem in result.Items)
        {
            sourcesByPath.TryGetValue(
                Path.GetFullPath(
                    resultItem.SourcePath),
                out var sourceInfo);

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
                            resultItem.ResultPath)
                            ? null
                            : Path.GetFullPath(
                                resultItem.ResultPath),
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
                    Status =
                        MapAuditStatus(
                            action,
                            resultItem),
                    Message =
                        resultItem.Message ??
                        GetSuccessfulAuditMessage(
                            settings,
                            action,
                            resultItem,
                            sourceInfo)
                });
        }

        var baseName =
            BuildUniqueAuditBaseName(
                startedAt);

        var logPath =
            Path.Combine(
                PortablePaths.LogsDirectory,
                baseName +
                ".txt");

        await WriteTextAtomicAsync(
            logPath,
            OrganizationExecutionService.FormatHumanLog(
                record),
            cancellationToken);

        var keepTechnicalUndo =
            UndoService.SupportsUndo(
                record) &&
            record.Items.Any(item =>
                UndoService.IsUndoCandidate(
                    record,
                    item));

        if (!settings.SaveOrganizeHistory &&
            !keepTechnicalUndo)
        {
            return;
        }

        var historyPath =
            Path.Combine(
                PortablePaths.HistoryDirectory,
                baseName +
                ".json");

        await WriteTextAtomicAsync(
            historyPath,
            JsonSerializer.Serialize(
                record,
                JsonOptions),
            cancellationToken);
    }

    private static IReadOnlyList<OrganizationSourceAuditInfo> CaptureAuditSources(
        IEnumerable<string> paths)
    {
        var results =
            new List<OrganizationSourceAuditInfo>();

        foreach (var rawPath in paths)
        {
            try
            {
                var fullPath =
                    Path.GetFullPath(
                        rawPath);

                if (Directory.Exists(
                        fullPath))
                {
                    var snapshot =
                        OrganizationEntrySafety.GetDirectorySnapshot(
                            fullPath);

                    results.Add(
                        new OrganizationSourceAuditInfo(
                            fullPath,
                            Path.GetFileName(
                                fullPath),
                            snapshot.TotalSizeBytes,
                            snapshot.ModifiedUtcTicks,
                            OrganizationAnalysisItemKind.Folder,
                            snapshot.FileCount,
                            snapshot.ContentFingerprint));
                }
                else if (File.Exists(
                             fullPath))
                {
                    var info =
                        new FileInfo(
                            fullPath);

                    results.Add(
                        new OrganizationSourceAuditInfo(
                            fullPath,
                            info.Name,
                            info.Length,
                            info.LastWriteTimeUtc.Ticks,
                            OrganizationAnalysisItemKind.File,
                            1,
                            null));
                }
                else
                {
                    results.Add(
                        new OrganizationSourceAuditInfo(
                            fullPath,
                            Path.GetFileName(
                                fullPath),
                            0,
                            0,
                            OrganizationAnalysisItemKind.File,
                            1,
                            null));
                }
            }
            catch
            {
                // La acción real vuelve a validar la ruta. La captura de
                // auditoría nunca debe bloquear la operación principal.
            }
        }

        return results;
    }

    private static OrganizationExecutionItemStatus MapAuditStatus(
        string action,
        OrganizationSourceActionItemResult item)
    {
        if (item.Status ==
            OrganizationSourceActionStatus.Conflict)
        {
            return OrganizationExecutionItemStatus.SkippedConflict;
        }

        if (item.Status ==
            OrganizationSourceActionStatus.Missing)
        {
            return OrganizationExecutionItemStatus.SourceMissing;
        }

        if (item.Status ==
            OrganizationSourceActionStatus.Error)
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
                    item.ResultPath ??
                    item.SourcePath)
                ? OrganizationExecutionItemStatus.CompletedAction
                : OrganizationExecutionItemStatus.Renamed;
        }

        return OrganizationExecutionItemStatus.CompletedAction;
    }

    private static string? GetSuccessfulAuditMessage(
        AppSettings settings,
        string action,
        OrganizationSourceActionItemResult item,
        OrganizationSourceAuditInfo? sourceInfo)
    {
        if (action.Equals(
                "DELETE",
                StringComparison.OrdinalIgnoreCase))
        {
            return settings.UseRecycleBin
                ? "Enviado a la Papelera desde Organizar."
                : "Eliminado permanentemente desde Organizar.";
        }

        if (action.Equals(
                "RENAME",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                item.ResultPath))
        {
            var originalName =
                sourceInfo?.FileName ??
                Path.GetFileName(
                    item.SourcePath);

            var finalName =
                Path.GetFileName(
                    item.ResultPath);

            return
                $"Renombrado de \"{originalName}\" a \"{finalName}\" desde Organizar.";
        }

        return null;
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                path)!);

        var temporaryPath =
            path +
            $".tmp_{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false),
                cancellationToken);

            File.Move(
                temporaryPath,
                path,
                overwrite:
                    true);
        }
        finally
        {
            TryDeleteAuditTemporaryFile(
                temporaryPath);
        }
    }

    private static string BuildUniqueAuditBaseName(
        DateTime startedAt)
    {
        var baseName =
            $"BandaNV_{startedAt:dd-MM-yyyy____HH-mm-ss}";
        var candidate =
            baseName;
        var index =
            2;

        while (File.Exists(
                   Path.Combine(
                       PortablePaths.LogsDirectory,
                       candidate +
                       ".txt")) ||
               File.Exists(
                   Path.Combine(
                       PortablePaths.HistoryDirectory,
                       candidate +
                       ".json")))
        {
            candidate =
                $"{baseName}__{index}";
            index++;
        }

        return candidate;
    }

    private static void TryDeleteAuditTemporaryFile(
        string path)
    {
        try
        {
            if (File.Exists(
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

    private sealed record OrganizationSourceAuditInfo(
        string FullPath,
        string FileName,
        long SizeBytes,
        long ModifiedUtcTicks,
        OrganizationAnalysisItemKind Kind,
        int ContainedFileCount,
        string? ContentFingerprint);

    private static void EnsureSourceRootExists(
        string sourceRoot)
    {
        if (!Directory.Exists(
                sourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta de origen no existe: {sourceRoot}");
        }
    }

    private static void EnsureEditablePathInsideSource(
        string path,
        string sourceRoot)
    {
        var normalizedPath =
            Path.GetFullPath(
                path);

        if (PathsEqual(
                normalizedPath,
                sourceRoot))
        {
            throw new InvalidOperationException(
                "La carpeta de origen no puede modificarse desde Organizar.");
        }

        var prefix =
            sourceRoot.EndsWith(
                Path.DirectorySeparatorChar)
                ? sourceRoot
                : sourceRoot +
                  Path.DirectorySeparatorChar;

        if (!normalizedPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La operación fue bloqueada porque el elemento está fuera de la carpeta de origen.");
        }
    }

    private static string NormalizeDirectoryPath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new InvalidOperationException(
                "La carpeta de origen no está configurada.");
        }

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(
                path));
    }

    private static bool PathsEqual(
        string left,
        string right) =>
        Path.GetFullPath(
                left)
            .Equals(
                Path.GetFullPath(
                    right),
                StringComparison.OrdinalIgnoreCase);
}

public enum OrganizationSourceActionStatus
{
    Completed,
    Missing,
    Conflict,
    Error
}

public sealed record OrganizationSourceActionItemResult(
    string SourcePath,
    string? ResultPath,
    OrganizationSourceActionStatus Status,
    string? Message);

public sealed record OrganizationSourceActionResult(
    IReadOnlyList<OrganizationSourceActionItemResult> Items)
{
    public int CompletedCount =>
        Items.Count(item =>
            item.Status == OrganizationSourceActionStatus.Completed);

    public bool HasErrors =>
        Items.Any(item =>
            item.Status != OrganizationSourceActionStatus.Completed);
}
