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
        var destinationRoot =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(destinationRoot) ||
            !Directory.Exists(destinationRoot))
        {
            return [];
        }

        var items =
            new List<IndexedSearchFile>();

        foreach (var category in settings.Categories
                     .OrderBy(category => category.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryFolder =
                CategoryService.GetFolderPath(
                    destinationRoot,
                    category.Order,
                    category.Name);

            if (!Directory.Exists(categoryFolder))
            {
                continue;
            }

            foreach (var path in EnumerateTopLevelFiles(
                         categoryFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var file =
                        new FileInfo(
                            path);

                    items.Add(
                        new IndexedSearchFile(
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
                    // Si cambia durante el escaneo, se omite esta entrada.
                }
            }

            foreach (var path in EnumerateTopLevelDirectories(
                         categoryFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folder =
                    TryIndexDirectory(
                        path,
                        category,
                        cancellationToken);

                if (folder is not null)
                {
                    items.Add(
                        folder);
                }
            }
        }

        return items
            .OrderByDescending(item =>
                item.ModifiedAt)
            .ThenBy(
                item => item.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IndexedSearchFile? TryIndexDirectory(
        string path,
        CategorySettings category,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory =
                new DirectoryInfo(
                    path);

            if (!directory.Exists ||
                (directory.Attributes &
                 FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            var contents =
                ScanDirectoryContents(
                    directory.FullName,
                    cancellationToken);

            var totalSize =
                contents.Sum(item =>
                    item.SizeBytes);

            var modifiedAt =
                contents.Count > 0
                    ? contents.Max(item =>
                        item.ModifiedAt)
                    : directory.LastWriteTime;

            return new IndexedSearchFile(
                directory.FullName,
                directory.Name,
                category.Id,
                category.Name,
                category.Order,
                totalSize,
                modifiedAt,
                OrganizationAnalysisItemKind.Folder,
                contents.Count,
                contents);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static List<IndexedSearchChild> ScanDirectoryContents(
        string root,
        CancellationToken cancellationToken)
    {
        var results =
            new List<IndexedSearchChild>();

        var pending =
            new Stack<string>();

        pending.Push(
            root);

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
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var attributes =
                        File.GetAttributes(
                            filePath);

                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var file =
                        new FileInfo(
                            filePath);

                    results.Add(
                        new IndexedSearchChild(
                            Path.GetRelativePath(
                                root,
                                file.FullName),
                            file.Name,
                            NormalizeExtension(
                                file.Extension),
                            file.Length,
                            file.LastWriteTime));
                }
                catch
                {
                }
            }

            foreach (var directoryPath in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var attributes =
                        File.GetAttributes(
                            directoryPath);

                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    pending.Push(
                        directoryPath);
                }
                catch
                {
                }
            }
        }

        return results
            .OrderBy(
                item => item.RelativePath,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateTopLevelFiles(
        string folder)
    {
        try
        {
            return Directory
                .GetFiles(
                    folder,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateTopLevelDirectories(
        string folder)
    {
        try
        {
            return Directory
                .GetDirectories(
                    folder,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeExtension(
        string? extension)
    {
        var value =
            (extension ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (value.Length == 0)
        {
            return ".sin-extension";
        }

        return value.StartsWith('.')
            ? value
            : $".{value}";
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
}
