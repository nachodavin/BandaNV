using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

/// <summary>
/// Navegación de solo lectura por unidades y carpetas reales.
/// No depende del índice de Buscar ni modifica archivos.
/// </summary>
public sealed class ExplorerFileSystemService
{
    // Un límite explícito evita congelar la interfaz al entrar en carpetas
    // descomunales. La UI informa cuando el listado es parcial.
    public const int MaxItemsPerFolder = 30_000;

    public Task<ExplorerListing> GetDrivesAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetDrives(cancellationToken), cancellationToken);

    public Task<ExplorerListing> GetFolderAsync(
        string path,
        CancellationToken cancellationToken = default,
        bool includeHidden = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("La ruta debe ser absoluta.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        return Task.Run(
            () => GetFolder(fullPath, cancellationToken, includeHidden),
            cancellationToken);
    }

    private static ExplorerListing GetDrives(CancellationToken cancellationToken)
    {
        var items = new List<ExplorerItem>();

        foreach (var drive in DriveInfo.GetDrives().OrderBy(
                     drive => drive.Name,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = drive.Name;
            try
            {
                if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                {
                    name = drive.VolumeLabel + " (" + drive.Name.TrimEnd('\\') + ")";
                }
            }
            catch (IOException)
            {
                // Una unidad desconectada igual debe poder verse en Este equipo.
            }
            catch (UnauthorizedAccessException)
            {
                // No todas las unidades permiten consultar su etiqueta.
            }

            items.Add(new ExplorerItem(
                drive.Name,
                name,
                IsDirectory: true,
                SizeBytes: null,
                ModifiedAt: null,
                TypeName: "Unidad",
                IsDrive: true));
        }

        return new ExplorerListing(items);
    }

    private static ExplorerListing GetFolder(
        string path,
        CancellationToken cancellationToken,
        bool includeHidden)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No se hace un escaneo profundo: solo los hijos directos.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            // Los archivos de sistema siguen ocultos como medida de seguridad.
            AttributesToSkip = includeHidden
                ? FileAttributes.System
                : FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false
        };

        var items = new List<ExplorerItem>();
        var skipped = 0;
        var truncated = false;

        foreach (var entry in new DirectoryInfo(path)
                     .EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (items.Count >= MaxItemsPerFolder)
            {
                truncated = true;
                break;
            }

            try
            {
                entry.Refresh();
                var attributes = entry.Attributes;
                var directory = (attributes & FileAttributes.Directory) != 0;
                var link = (attributes & FileAttributes.ReparsePoint) != 0;

                long? size = null;
                if (!directory)
                {
                    try
                    {
                        size = ((FileInfo)entry).Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // La fila sigue visible aunque no se pueda leer el tamaño.
                    }
                }

                DateTime? modified = null;
                try
                {
                    modified = entry.LastWriteTime;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Un archivo bloqueado puede no exponer todos sus metadatos.
                }

                var extension = directory ? "" : Path.GetExtension(entry.Name);
                var type = directory
                    ? "Carpeta"
                    : string.IsNullOrEmpty(extension)
                        ? "Archivo"
                        : extension.TrimStart('.').ToUpperInvariant() + " · Archivo";

                items.Add(new ExplorerItem(
                    entry.FullName,
                    entry.Name,
                    directory,
                    size,
                    modified,
                    type,
                    IsReparsePoint: link));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Los elementos pueden desaparecer mientras se enumera.
                skipped++;
            }
        }

        var sorted = items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new ExplorerListing(sorted, skipped, truncated);
    }
}
