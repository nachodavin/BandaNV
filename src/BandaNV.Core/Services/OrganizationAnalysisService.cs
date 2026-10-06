using System.Security.Cryptography;
using System.Text;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class OrganizationAnalysisService
{
    public Task<OrganizationAnalysisResult> AnalyzeAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(
            () => Analyze(settings, cancellationToken),
            cancellationToken);
    }

    private static OrganizationAnalysisResult Analyze(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var source =
            NormalizeDirectoryPath(
                settings.SourceFolder);

        if (string.IsNullOrWhiteSpace(source) ||
            !Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                "La carpeta de origen configurada no existe o no está disponible.");
        }

        var destination =
            NormalizeDirectoryPath(
                settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new InvalidOperationException(
                "Configurá una carpeta de destino antes de analizar.");
        }

        var categories =
            settings.Categories
                .OrderBy(category => category.Order)
                .ToList();

        var extensionMap =
            BuildExtensionMap(
                categories);

        var excludedRoots =
            BuildExcludedRoots(
                source,
                destination,
                categories);

        var items =
            new List<OrganizationAnalysisFile>();

        var skippedDirectories =
            0;

        if (settings.OrganizeFoldersAsUnits)
        {
            foreach (var path in EnumerateFilesSafely(
                         source,
                         includeSubfolders: false,
                         excludedRoots,
                         () => skippedDirectories++,
                         cancellationToken))
            {
                var item =
                    TryAnalyzeFile(
                        path,
                        source,
                        destination,
                        extensionMap,
                        settings.ConflictBehavior);

                if (item is not null)
                {
                    items.Add(
                        item);
                }
            }

            foreach (var directory in EnumerateTopLevelDirectoriesSafely(
                         source,
                         excludedRoots,
                         () => skippedDirectories++,
                         cancellationToken))
            {
                var item =
                    TryAnalyzeFolder(
                        directory,
                        source,
                        destination,
                        extensionMap,
                        excludedRoots,
                        settings.ConflictBehavior,
                        () => skippedDirectories++,
                        cancellationToken);

                if (item is not null)
                {
                    items.Add(
                        item);
                }
            }
        }
        else
        {
            foreach (var path in EnumerateFilesSafely(
                         source,
                         includeSubfolders: false,
                         excludedRoots,
                         () => skippedDirectories++,
                         cancellationToken))
            {
                var item =
                    TryAnalyzeFile(
                        path,
                        source,
                        destination,
                        extensionMap,
                        settings.ConflictBehavior);

                if (item is not null)
                {
                    items.Add(
                        item);
                }
            }
        }

        return new OrganizationAnalysisResult(
            source,
            destination,
            items
                .OrderBy(item => item.IsClassified ? 0 : 1)
                .ThenBy(item => item.IsDirectory ? 0 : 1)
                .ThenBy(item => item.CategoryOrder ?? int.MaxValue)
                .ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            skippedDirectories);
    }

    private static OrganizationAnalysisFile? TryAnalyzeFile(
        string path,
        string source,
        string destination,
        IReadOnlyDictionary<string, CategorySettings> extensionMap,
        string conflictBehavior)
    {
        try
        {
            var file =
                new FileInfo(
                    path);

            var extension =
                NormalizeExtension(
                    file.Extension);

            extensionMap.TryGetValue(
                extension,
                out var category);

            var destinationPath =
                category is null
                    ? null
                    : Path.Combine(
                        destination,
                        CategoryService.GetFolderName(
                            category.Order,
                            category.Name),
                        file.Name);

            var hasDestinationConflict =
                HasDestinationConflict(
                    file.FullName,
                    destinationPath,
                    conflictBehavior);

            return new OrganizationAnalysisFile(
                file.FullName,
                Path.GetRelativePath(
                    source,
                    file.FullName),
                file.Name,
                extension,
                file.Length,
                file.LastWriteTime,
                file.LastWriteTimeUtc.Ticks,
                category?.Id,
                category?.Name,
                category?.Order,
                destinationPath,
                hasDestinationConflict,
                OrganizationAnalysisItemKind.File,
                ContainedFileCount: 1,
                RecognizedFileCount:
                    category is null ? 0 : 1,
                DistinctCategoryCount:
                    category is null ? 0 : 1,
                ScanIncomplete: false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static OrganizationAnalysisFile? TryAnalyzeFolder(
        string folderPath,
        string source,
        string destination,
        IReadOnlyDictionary<string, CategorySettings> extensionMap,
        HashSet<string> excludedRoots,
        string conflictBehavior,
        Action onSkippedDirectory,
        CancellationToken cancellationToken)
    {
        DirectoryInfo directory;

        try
        {
            directory =
                new DirectoryInfo(
                    folderPath);
        }
        catch
        {
            return null;
        }

        var totalFiles =
            0;

        var recognizedFiles =
            0;

        var totalSize =
            0L;

        var scanIncomplete =
            false;

        var detectedCategories =
            new Dictionary<string, CategorySettings>(
                StringComparer.OrdinalIgnoreCase);

        var folderFiles =
            new List<OrganizationAnalysisFolderFile>();

        var fingerprintParts =
            new List<string>();

        foreach (var path in EnumerateFilesSafely(
                     folderPath,
                     includeSubfolders: true,
                     excludedRoots,
                     () =>
                     {
                         scanIncomplete = true;
                         onSkippedDirectory();
                     },
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var file =
                    new FileInfo(
                        path);

                totalFiles++;
                totalSize +=
                    file.Length;

                var extension =
                    NormalizeExtension(
                        file.Extension);

                extensionMap.TryGetValue(
                    extension,
                    out var category);

                var relativePath =
                    Path.GetRelativePath(
                        directory.FullName,
                        file.FullName);

                folderFiles.Add(
                    new OrganizationAnalysisFolderFile(
                        relativePath,
                        file.Name,
                        extension,
                        file.Length,
                        file.LastWriteTime,
                        category?.Id,
                        category?.Name,
                        category?.Order));

                fingerprintParts.Add(
                    $"{relativePath}\0{file.Length}\0{file.LastWriteTimeUtc.Ticks}");

                if (category is null)
                {
                    continue;
                }

                recognizedFiles++;

                detectedCategories.TryAdd(
                    category.Id,
                    category);
            }
            catch (FileNotFoundException)
            {
                scanIncomplete = true;
            }
            catch (DirectoryNotFoundException)
            {
                scanIncomplete = true;
            }
            catch (UnauthorizedAccessException)
            {
                scanIncomplete = true;
            }
            catch (IOException)
            {
                scanIncomplete = true;
            }
        }

        foreach (var nestedDirectory in EnumerateDirectoriesSafely(
                     folderPath,
                     excludedRoots,
                     onSkippedDirectory,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var relativeDirectoryPath =
                    Path.GetRelativePath(
                        directory.FullName,
                        nestedDirectory);

                var prefix =
                    relativeDirectoryPath +
                    Path.DirectorySeparatorChar;

                var nestedFiles =
                    folderFiles
                        .Where(item =>
                            !item.IsDirectory &&
                            item.RelativePath.StartsWith(
                                prefix,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var nestedCategories =
                    nestedFiles
                        .Where(item => item.IsClassified)
                        .GroupBy(item => item.CategoryId, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                CategorySettings? nestedCategory =
                    null;

                if (nestedFiles.Count > 0 &&
                    nestedFiles.All(item => item.IsClassified) &&
                    nestedCategories.Count == 1)
                {
                    var categoryId =
                        nestedCategories[0].Key;

                    if (!string.IsNullOrWhiteSpace(categoryId))
                    {
                        nestedCategory =
                            extensionMap.Values.FirstOrDefault(category =>
                                category.Id.Equals(
                                    categoryId,
                                    StringComparison.OrdinalIgnoreCase));
                    }
                }

                var nestedInfo =
                    new DirectoryInfo(
                        nestedDirectory);

                folderFiles.Add(
                    new OrganizationAnalysisFolderFile(
                        relativeDirectoryPath,
                        nestedInfo.Name,
                        string.Empty,
                        nestedFiles.Sum(item => item.SizeBytes),
                        nestedInfo.LastWriteTime,
                        nestedCategory?.Id,
                        nestedCategory?.Name,
                        nestedCategory?.Order,
                        IsDirectory: true,
                        ContainedFileCount: nestedFiles.Count,
                        DistinctCategoryCount: nestedCategories.Count));
            }
            catch (UnauthorizedAccessException)
            {
                scanIncomplete = true;
                onSkippedDirectory();
            }
            catch (IOException)
            {
                scanIncomplete = true;
                onSkippedDirectory();
            }
        }

        if (totalFiles == 0)
        {
            return null;
        }

        CategorySettings? inferredCategory =
            null;

        if (!scanIncomplete &&
            recognizedFiles == totalFiles &&
            detectedCategories.Count == 1)
        {
            inferredCategory =
                detectedCategories.Values.First();
        }

        var destinationPath =
            inferredCategory is null
                ? null
                : Path.Combine(
                    destination,
                    CategoryService.GetFolderName(
                        inferredCategory.Order,
                        inferredCategory.Name),
                    directory.Name);

        var hasDestinationConflict =
            HasDestinationConflict(
                directory.FullName,
                destinationPath,
                conflictBehavior);

        DateTime modifiedAt;
        long modifiedUtcTicks;

        try
        {
            modifiedAt =
                directory.LastWriteTime;

            modifiedUtcTicks =
                directory.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            modifiedAt =
                DateTime.MinValue;

            modifiedUtcTicks =
                0;
        }

        return new OrganizationAnalysisFile(
            directory.FullName,
            Path.GetRelativePath(
                source,
                directory.FullName),
            directory.Name,
            string.Empty,
            totalSize,
            modifiedAt,
            modifiedUtcTicks,
            inferredCategory?.Id,
            inferredCategory?.Name,
            inferredCategory?.Order,
            destinationPath,
            hasDestinationConflict,
            OrganizationAnalysisItemKind.Folder,
            ContainedFileCount:
                totalFiles,
            RecognizedFileCount:
                recognizedFiles,
            DistinctCategoryCount:
                detectedCategories.Count,
            ScanIncomplete:
                scanIncomplete,
            FolderFiles:
                folderFiles
                    .OrderBy(item => item.IsDirectory ? 0 : 1)
                    .ThenBy(
                        item => item.RelativePath,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            ContentFingerprint:
                scanIncomplete
                    ? null
                    : BuildContentFingerprint(
                        fingerprintParts));
    }

    private static string BuildContentFingerprint(
        IEnumerable<string> parts)
    {
        var ordered =
            parts
                .OrderBy(
                    value => value,
                    StringComparer.OrdinalIgnoreCase);

        var payload =
            string.Join(
                "\n",
                ordered);

        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    payload));

        return Convert.ToHexString(
                bytes)
            .ToLowerInvariant();
    }

    private static bool HasDestinationConflict(
        string sourcePath,
        string? destinationPath,
        string conflictBehavior)
    {
        if (string.IsNullOrWhiteSpace(
                destinationPath) ||
            !conflictBehavior.Equals(
                "Preguntar",
                StringComparison.OrdinalIgnoreCase) ||
            PathsEqual(
                sourcePath,
                destinationPath))
        {
            return false;
        }

        return File.Exists(
                   destinationPath) ||
               Directory.Exists(
                   destinationPath);
    }

    private static Dictionary<string, CategorySettings> BuildExtensionMap(
        IEnumerable<CategorySettings> categories)
    {
        var map =
            new Dictionary<string, CategorySettings>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            foreach (var rawExtension in category.Extensions)
            {
                var extension =
                    NormalizeExtension(
                        rawExtension);

                if (!map.ContainsKey(
                        extension))
                {
                    map[extension] =
                        category;
                }
            }
        }

        return map;
    }

    private static HashSet<string> BuildExcludedRoots(
        string source,
        string destination,
        IReadOnlyList<CategorySettings> categories)
    {
        var excluded =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (PathsEqual(
                source,
                destination))
        {
            foreach (var category in categories)
            {
                excluded.Add(
                    NormalizeDirectoryPath(
                        CategoryService.GetFolderPath(
                            destination,
                            category.Order,
                            category.Name)));
            }

            return excluded;
        }

        if (IsDescendantOf(
                destination,
                source))
        {
            excluded.Add(
                destination);
        }

        return excluded;
    }

    private static IEnumerable<string> EnumerateTopLevelDirectoriesSafely(
        string source,
        HashSet<string> excludedRoots,
        Action onSkippedDirectory,
        CancellationToken cancellationToken)
    {
        string[] directories;

        try
        {
            directories =
                Directory.GetDirectories(
                    source,
                    "*",
                    SearchOption.TopDirectoryOnly);
        }
        catch (UnauthorizedAccessException)
        {
            onSkippedDirectory();
            yield break;
        }
        catch (IOException)
        {
            onSkippedDirectory();
            yield break;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized =
                NormalizeDirectoryPath(
                    directory);

            if (IsFolderCandidateExcluded(
                    normalized,
                    excludedRoots))
            {
                continue;
            }

            try
            {
                var attributes =
                    File.GetAttributes(
                        normalized);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    onSkippedDirectory();
                    continue;
                }
            }
            catch
            {
                onSkippedDirectory();
                continue;
            }

            yield return normalized;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(
        string source,
        HashSet<string> excludedRoots,
        Action onSkippedDirectory,
        CancellationToken cancellationToken)
    {
        var pending =
            new Stack<string>();

        pending.Push(
            source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current =
                pending.Pop();

            string[] directories;

            try
            {
                directories =
                    Directory.GetDirectories(
                        current,
                        "*",
                        SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                onSkippedDirectory();
                continue;
            }
            catch (IOException)
            {
                onSkippedDirectory();
                continue;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized =
                    NormalizeDirectoryPath(
                        directory);

                if (IsExcluded(
                        normalized,
                        excludedRoots))
                {
                    continue;
                }

                try
                {
                    var attributes =
                        File.GetAttributes(
                            normalized);

                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        onSkippedDirectory();
                        continue;
                    }
                }
                catch
                {
                    onSkippedDirectory();
                    continue;
                }

                yield return normalized;

                pending.Push(
                    normalized);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(
        string source,
        bool includeSubfolders,
        HashSet<string> excludedRoots,
        Action onSkippedDirectory,
        CancellationToken cancellationToken)
    {
        if (!includeSubfolders)
        {
            string[] files;

            try
            {
                files =
                    Directory.GetFiles(
                        source,
                        "*",
                        SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                onSkippedDirectory();
                yield break;
            }
            catch (IOException)
            {
                onSkippedDirectory();
                yield break;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            yield break;
        }

        var pending =
            new Stack<string>();

        pending.Push(
            source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current =
                pending.Pop();

            if (!PathsEqual(
                    current,
                    source) &&
                IsExcluded(
                    current,
                    excludedRoots))
            {
                continue;
            }

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
            catch (UnauthorizedAccessException)
            {
                onSkippedDirectory();
                continue;
            }
            catch (IOException)
            {
                onSkippedDirectory();
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized =
                    NormalizeDirectoryPath(
                        directory);

                if (IsExcluded(
                        normalized,
                        excludedRoots))
                {
                    continue;
                }

                try
                {
                    var attributes =
                        File.GetAttributes(
                            normalized);

                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        onSkippedDirectory();
                        continue;
                    }
                }
                catch
                {
                    onSkippedDirectory();
                    continue;
                }

                pending.Push(
                    normalized);
            }
        }
    }

    private static bool IsFolderCandidateExcluded(
        string path,
        IEnumerable<string> excludedRoots) =>
        excludedRoots.Any(root =>
            PathsEqual(
                path,
                root) ||
            IsDescendantOf(
                path,
                root) ||
            IsDescendantOf(
                root,
                path));

    private static bool IsExcluded(
        string path,
        IEnumerable<string> excludedRoots) =>
        excludedRoots.Any(root =>
            PathsEqual(
                path,
                root) ||
            IsDescendantOf(
                path,
                root));

    private static bool IsDescendantOf(
        string candidate,
        string parent)
    {
        var normalizedCandidate =
            NormalizeDirectoryPath(
                candidate);

        var normalizedParent =
            NormalizeDirectoryPath(
                parent);

        if (PathsEqual(
                normalizedCandidate,
                normalizedParent))
        {
            return false;
        }

        var prefix =
            normalizedParent +
            Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string left,
        string right) =>
        NormalizeDirectoryPath(
                left)
            .Equals(
                NormalizeDirectoryPath(
                    right),
                StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectoryPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
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

    private static string NormalizeExtension(
        string? extension)
    {
        var normalized =
            (extension ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return ".sin-extension";
        }

        return normalized.StartsWith('.')
            ? normalized
            : $".{normalized}";
    }
}
