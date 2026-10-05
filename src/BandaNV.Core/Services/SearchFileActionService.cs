using BandaNV.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace BandaNV.Core.Services;

public sealed class SearchFileActionService
{
    public Task<SearchFileActionResult> MoveToCategoryAsync(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CategorySettings targetCategory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(targetCategory);

        return Task.Run(
            () => MoveToCategory(
                settings,
                filePaths,
                targetCategory,
                cancellationToken),
            cancellationToken);
    }

    public Task<SearchFileActionResult> RenameAsync(
        AppSettings settings,
        string filePath,
        string proposedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(
            () => Rename(
                settings,
                filePath,
                proposedName,
                cancellationToken),
            cancellationToken);
    }

    public Task<SearchFileActionResult> DeleteAsync(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filePaths);

        return Task.Run(
            () => Delete(
                settings,
                filePaths,
                cancellationToken),
            cancellationToken);
    }

    private static SearchFileActionResult MoveToCategory(
        AppSettings settings,
        IReadOnlyList<string> filePaths,
        CategorySettings targetCategory,
        CancellationToken cancellationToken)
    {
        var destinationRoot =
            NormalizeDirectoryPath(settings.DestinationFolder);

        EnsureDirectoryExists(destinationRoot);

        var targetFolder = CategoryService.GetFolderPath(
            destinationRoot,
            targetCategory.Order,
            targetCategory.Name);

        EnsurePathInsideRoot(targetFolder, destinationRoot);

        if (settings.CreateFolders)
        {
            Directory.CreateDirectory(targetFolder);
        }
        else if (!Directory.Exists(targetFolder))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta de categoría no existe: {targetFolder}");
        }

        var results = new List<SearchFileActionItemResult>();

        foreach (var rawPath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = Path.GetFullPath(rawPath);
            EnsurePathInsideRoot(source, destinationRoot);

            if (!File.Exists(source))
            {
                results.Add(new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.Missing,
                    "El archivo ya no existe."));
                continue;
            }

            var desiredTarget = Path.Combine(
                targetFolder,
                Path.GetFileName(source));

            if (PathsEqual(source, desiredTarget))
            {
                results.Add(new SearchFileActionItemResult(
                    source,
                    source,
                    SearchFileActionStatus.Completed,
                    "El archivo ya pertenece a esa categoría."));
                continue;
            }

            var target = ResolveTargetPath(
                settings.ConflictBehavior,
                desiredTarget);

            if (target is null)
            {
                results.Add(new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.SkippedConflict,
                    "Ya existe un archivo con el mismo nombre en la categoría elegida."));
                continue;
            }

            EnsurePathInsideRoot(target, destinationRoot);

            try
            {
                if (File.Exists(target) &&
                    settings.ConflictBehavior.Equals(
                        "Reemplazar",
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                }

                MoveFileSafely(source, target);

                results.Add(new SearchFileActionItemResult(
                    source,
                    target,
                    SearchFileActionStatus.Completed,
                    null));
            }
            catch (Exception ex)
            {
                results.Add(new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.Error,
                    ex.Message));
            }
        }

        return new SearchFileActionResult(results);
    }

    private static SearchFileActionResult Rename(
        AppSettings settings,
        string filePath,
        string proposedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destinationRoot =
            NormalizeDirectoryPath(settings.DestinationFolder);

        EnsureDirectoryExists(destinationRoot);

        var source = Path.GetFullPath(filePath);
        EnsurePathInsideRoot(source, destinationRoot);

        if (!File.Exists(source))
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.Missing,
                    "El archivo ya no existe.")
            ]);
        }

        var fileName = proposedName.Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                "El nuevo nombre no puede estar vacío.",
                nameof(proposedName));
        }

        if (fileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "El nombre contiene caracteres no permitidos.",
                nameof(proposedName));
        }

        if (string.IsNullOrWhiteSpace(
                Path.GetExtension(fileName)))
        {
            fileName += Path.GetExtension(source);
        }

        var target = Path.Combine(
            Path.GetDirectoryName(source)!,
            fileName);

        EnsurePathInsideRoot(target, destinationRoot);

        if (PathsEqual(source, target))
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

        if (File.Exists(target))
        {
            return new SearchFileActionResult(
            [
                new SearchFileActionItemResult(
                    source,
                    null,
                    SearchFileActionStatus.SkippedConflict,
                    "Ya existe un archivo con ese nombre en la misma carpeta.")
            ]);
        }

        try
        {
            MoveFileSafely(source, target);

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
            NormalizeDirectoryPath(settings.DestinationFolder);

        EnsureDirectoryExists(destinationRoot);

        var results = new List<SearchFileActionItemResult>();

        foreach (var rawPath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.GetFullPath(rawPath);
            EnsurePathInsideRoot(path, destinationRoot);

            if (!File.Exists(path))
            {
                results.Add(new SearchFileActionItemResult(
                    path,
                    null,
                    SearchFileActionStatus.Missing,
                    "El archivo ya no existe."));
                continue;
            }

            try
            {
                if (settings.UseRecycleBin)
                {
                    FileSystem.DeleteFile(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                }
                else
                {
                    File.Delete(path);
                }

                results.Add(new SearchFileActionItemResult(
                    path,
                    null,
                    SearchFileActionStatus.Completed,
                    null));
            }
            catch (Exception ex)
            {
                results.Add(new SearchFileActionItemResult(
                    path,
                    null,
                    SearchFileActionStatus.Error,
                    ex.Message));
            }
        }

        return new SearchFileActionResult(results);
    }

    private static string? ResolveTargetPath(
        string behavior,
        string desiredTarget)
    {
        if (!File.Exists(desiredTarget))
        {
            return desiredTarget;
        }

        if (behavior.Equals(
                "Renombrar automáticamente",
                StringComparison.OrdinalIgnoreCase))
        {
            return GetUniqueDestination(desiredTarget);
        }

        if (behavior.Equals(
                "Reemplazar",
                StringComparison.OrdinalIgnoreCase))
        {
            return desiredTarget;
        }

        // "Preguntar" y "Omitir archivo" nunca pisan nada desde Buscar.
        return null;
    }

    private static string GetUniqueDestination(
        string desiredTarget)
    {
        var directory =
            Path.GetDirectoryName(desiredTarget)!;
        var name =
            Path.GetFileNameWithoutExtension(desiredTarget);
        var extension =
            Path.GetExtension(desiredTarget);

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

            if (new FileInfo(source).Length !=
                new FileInfo(temporaryDestination).Length)
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
}
