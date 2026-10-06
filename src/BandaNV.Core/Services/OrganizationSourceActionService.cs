using BandaNV.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace BandaNV.Core.Services;

public sealed class OrganizationSourceActionService
{
    public Task<OrganizationSourceActionResult> RenameAsync(
        AppSettings settings,
        string entryPath,
        string proposedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(
            () => Rename(
                settings,
                entryPath,
                proposedName,
                cancellationToken),
            cancellationToken);
    }

    public Task<OrganizationSourceActionResult> DeleteAsync(
        AppSettings settings,
        IReadOnlyList<string> entryPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(entryPaths);

        return Task.Run(
            () => Delete(
                settings,
                entryPaths,
                cancellationToken),
            cancellationToken);
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
