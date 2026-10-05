using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class SearchIndexService
{
    public Task<IReadOnlyList<IndexedSearchFile>> ScanAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run<IReadOnlyList<IndexedSearchFile>>(
            () => Scan(settings, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<IndexedSearchFile> Scan(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var destinationRoot = NormalizeDirectoryPath(
            settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(destinationRoot) ||
            !Directory.Exists(destinationRoot))
        {
            return [];
        }

        var files = new List<IndexedSearchFile>();

        foreach (var category in settings.Categories
                     .OrderBy(category => category.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryFolder = CategoryService.GetFolderPath(
                destinationRoot,
                category.Order,
                category.Name);

            if (!Directory.Exists(categoryFolder))
            {
                continue;
            }

            string[] paths;

            try
            {
                paths = Directory.GetFiles(
                    categoryFolder,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var file = new FileInfo(path);

                    files.Add(new IndexedSearchFile(
                        file.FullName,
                        file.Name,
                        category.Id,
                        category.Name,
                        category.Order,
                        file.Length,
                        file.LastWriteTime));
                }
                catch
                {
                    // Si un archivo cambia durante el escaneo, se omite.
                }
            }
        }

        return files
            .OrderByDescending(file => file.ModifiedAt)
            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
}
