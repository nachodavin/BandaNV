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
        var source = NormalizeDirectoryPath(settings.SourceFolder);

        if (string.IsNullOrWhiteSpace(source) ||
            !Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                "La carpeta de origen configurada no existe o no está disponible.");
        }

        var destination = NormalizeDirectoryPath(settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new InvalidOperationException(
                "Configurá una carpeta de destino antes de analizar.");
        }

        var categories = settings.Categories
            .OrderBy(category => category.Order)
            .ToList();

        var extensionMap = BuildExtensionMap(categories);
        var excludedRoots = BuildExcludedRoots(
            source,
            destination,
            categories,
            settings.IncludeSubfolders);

        var files = new List<OrganizationAnalysisFile>();
        var skippedDirectories = 0;

        foreach (var path in EnumerateFilesSafely(
                     source,
                     settings.IncludeSubfolders,
                     excludedRoots,
                     () => skippedDirectories++,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var file = new FileInfo(path);
                var extension = NormalizeExtension(file.Extension);

                extensionMap.TryGetValue(extension, out var category);

                var destinationPath = category is null
                    ? null
                    : Path.Combine(
                        destination,
                        CategoryService.GetFolderName(
                            category.Order,
                            category.Name),
                        file.Name);

                files.Add(new OrganizationAnalysisFile(
                    file.FullName,
                    Path.GetRelativePath(source, file.FullName),
                    file.Name,
                    extension,
                    file.Length,
                    file.LastWriteTime,
                    category?.Id,
                    category?.Name,
                    category?.Order,
                    destinationPath));
            }
            catch (FileNotFoundException)
            {
                // El archivo desapareció mientras se analizaba. Se omite.
            }
            catch (DirectoryNotFoundException)
            {
                // La carpeta cambió durante el análisis. Se omite.
            }
            catch (UnauthorizedAccessException)
            {
                // El archivo no puede leerse. El análisis continúa.
            }
            catch (IOException)
            {
                // Un archivo bloqueado o transitorio no debe tumbar el análisis.
            }
        }

        return new OrganizationAnalysisResult(
            source,
            destination,
            files
                .OrderBy(file => file.IsClassified ? 0 : 1)
                .ThenBy(file => file.CategoryOrder ?? int.MaxValue)
                .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            skippedDirectories);
    }

    private static Dictionary<string, CategorySettings> BuildExtensionMap(
        IEnumerable<CategorySettings> categories)
    {
        var map = new Dictionary<string, CategorySettings>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            foreach (var rawExtension in category.Extensions)
            {
                var extension = NormalizeExtension(rawExtension);

                if (!map.ContainsKey(extension))
                {
                    map[extension] = category;
                }
            }
        }

        return map;
    }

    private static HashSet<string> BuildExcludedRoots(
        string source,
        string destination,
        IReadOnlyList<CategorySettings> categories,
        bool includeSubfolders)
    {
        var excluded = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (!includeSubfolders)
        {
            return excluded;
        }

        if (PathsEqual(source, destination))
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

        if (IsDescendantOf(destination, source))
        {
            excluded.Add(destination);
        }

        return excluded;
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
                files = Directory.GetFiles(
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

        var pending = new Stack<string>();
        pending.Push(source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();

            if (!PathsEqual(current, source) &&
                IsExcluded(current, excludedRoots))
            {
                continue;
            }

            string[] files;
            string[] directories;

            try
            {
                files = Directory.GetFiles(
                    current,
                    "*",
                    SearchOption.TopDirectoryOnly);

                directories = Directory.GetDirectories(
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

                var normalized = NormalizeDirectoryPath(directory);

                if (IsExcluded(normalized, excludedRoots))
                {
                    continue;
                }

                try
                {
                    var attributes = File.GetAttributes(normalized);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
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

                pending.Push(normalized);
            }
        }
    }

    private static bool IsExcluded(
        string path,
        IEnumerable<string> excludedRoots) =>
        excludedRoots.Any(root =>
            PathsEqual(path, root) ||
            IsDescendantOf(path, root));

    private static bool IsDescendantOf(
        string candidate,
        string parent)
    {
        var normalizedCandidate = NormalizeDirectoryPath(candidate);
        var normalizedParent = NormalizeDirectoryPath(parent);

        if (PathsEqual(normalizedCandidate, normalizedParent))
        {
            return false;
        }

        var prefix = normalizedParent + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        NormalizeDirectoryPath(left).Equals(
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase);

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

    private static string NormalizeExtension(string? extension)
    {
        var normalized = (extension ?? string.Empty)
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
