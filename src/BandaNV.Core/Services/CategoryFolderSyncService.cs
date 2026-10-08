using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class CategoryFolderSyncService
{
    public Task<CategoryFolderSyncResult> SynchronizeAsync(
        AppSettings settings,
        IReadOnlyList<CategorySettings> previousCategories,
        IReadOnlyList<CategorySettings> nextCategories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(previousCategories);
        ArgumentNullException.ThrowIfNull(nextCategories);

        return Task.Run(
            () => Synchronize(
                settings,
                previousCategories,
                nextCategories,
                cancellationToken),
            cancellationToken);
    }

    public Task<int> CleanupUnusedCategoryFoldersAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(
            () =>
            {
                var destinationRoot =
                    NormalizeDirectoryPath(
                        settings.DestinationFolder);

                if (string.IsNullOrWhiteSpace(destinationRoot) ||
                    !Directory.Exists(destinationRoot))
                {
                    return 0;
                }

                return CleanupTrackedOrphanFolders(
                    settings,
                    destinationRoot,
                    settings.DeleteUnusedCategoryFolders,
                    cancellationToken);
            },
            cancellationToken);
    }

    private static CategoryFolderSyncResult Synchronize(
        AppSettings settings,
        IReadOnlyList<CategorySettings> previousCategories,
        IReadOnlyList<CategorySettings> nextCategories,
        CancellationToken cancellationToken)
    {
        var destinationRoot =
            NormalizeDirectoryPath(settings.DestinationFolder);

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return CategoryFolderSyncResult.DeferredSync();
        }

        if (!Directory.Exists(destinationRoot))
        {
            if (!settings.CreateFolders)
            {
                return CategoryFolderSyncResult.DeferredSync();
            }

            ProtectedFolderService.EnsureAllowed(settings, destinationRoot);
            Directory.CreateDirectory(destinationRoot);
        }

        settings.OrphanedCategoryFolders ??= [];

        var previousById = previousCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.Id))
            .ToDictionary(
                category => category.Id,
                StringComparer.OrdinalIgnoreCase);

        var nextById = nextCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.Id))
            .ToDictionary(
                category => category.Id,
                StringComparer.OrdinalIgnoreCase);

        var moves = new List<FolderMove>();

        foreach (var next in nextCategories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!previousById.TryGetValue(next.Id, out var previous))
            {
                continue;
            }

            var source = CategoryService.GetFolderPath(
                destinationRoot,
                previous.Order,
                previous.Name);

            var target = CategoryService.GetFolderPath(
                destinationRoot,
                next.Order,
                next.Name);

            if (PathsEqual(source, target) ||
                !Directory.Exists(source))
            {
                continue;
            }

            moves.Add(new FolderMove(
                source,
                target,
                BuildTemporaryPath(
                    destinationRoot,
                    next.Id)));
        }

        var deletedMoves =
            new List<DeletedFolderMove>();

        foreach (var previous in previousCategories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (nextById.ContainsKey(previous.Id))
            {
                continue;
            }

            var source = CategoryService.GetFolderPath(
                destinationRoot,
                previous.Order,
                previous.Name);

            if (!Directory.Exists(source))
            {
                continue;
            }

            deletedMoves.Add(
                new DeletedFolderMove(
                    previous.Id,
                    previous.Name,
                    source,
                    BuildTemporaryPath(
                        destinationRoot,
                        previous.Id)));
        }

        var originalOrphans =
            settings.OrphanedCategoryFolders.ToList();

        try
        {
            ValidateTargets(
                settings,
                destinationRoot,
                moves,
                deletedMoves,
                nextCategories);

            var createdFolders = new List<string>();
            var phaseOneCompleted = new List<FolderMove>();
            var phaseTwoCompleted = new List<FolderMove>();
            var deletedStaged = new List<DeletedFolderMove>();
            var preservedDeletedMoves =
                new List<PreservedDeletedFolderMove>();

            try
            {
                // Primero apartamos las categorías eliminadas. Esto libera
                // prefijos numéricos que pueden pasar a otra categoría activa.
                foreach (var deleted in deletedMoves)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Directory.Move(
                        deleted.Source,
                        deleted.Temporary);

                    deletedStaged.Add(
                        deleted);
                }

                foreach (var move in moves)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Directory.Move(
                        move.Source,
                        move.Temporary);

                    phaseOneCompleted.Add(move);
                }

                foreach (var move in moves)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Directory.Move(
                        move.Temporary,
                        move.Target);

                    phaseTwoCompleted.Add(move);
                }

                var createdCount = 0;

                if (settings.CreateFolders)
                {
                    foreach (var category in nextCategories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var folder = CategoryService.GetFolderPath(
                            destinationRoot,
                            category.Order,
                            category.Name);

                        EnsurePathInsideRoot(
                            folder,
                            destinationRoot);

                        if (Directory.Exists(folder))
                        {
                            continue;
                        }

                        ProtectedFolderService.EnsureAllowed(settings, folder);
                        Directory.CreateDirectory(folder);
                        createdFolders.Add(folder);
                        createdCount++;
                    }
                }

                var deletedEmpty = 0;
                var preservedDeleted = 0;

                foreach (var deleted in deletedMoves)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var isEmpty =
                        !Directory
                            .EnumerateFileSystemEntries(
                                deleted.Temporary)
                            .Any();

                    if (isEmpty &&
                        settings.DeleteUnusedCategoryFolders)
                    {
                        Directory.Delete(
                            deleted.Temporary,
                            recursive: false);

                        deletedEmpty++;
                        continue;
                    }

                    var orphanTarget =
                        BuildAvailableOrphanPath(
                            destinationRoot,
                            deleted.CategoryName);

                    EnsurePathInsideRoot(
                        orphanTarget,
                        destinationRoot);

                    ProtectedFolderService.EnsureAllowed(
                        settings,
                        orphanTarget);

                    Directory.Move(
                        deleted.Temporary,
                        orphanTarget);

                    preservedDeletedMoves.Add(
                        new PreservedDeletedFolderMove(
                            deleted,
                            orphanTarget));

                    TrackOrphanFolder(
                        settings,
                        orphanTarget);

                    preservedDeleted++;
                }

                deletedEmpty +=
                    CleanupTrackedOrphanFolders(
                        settings,
                        destinationRoot,
                        settings.DeleteUnusedCategoryFolders,
                        cancellationToken);

                return new CategoryFolderSyncResult(
                    true,
                    moves.Count,
                    createdCount,
                    deletedEmpty,
                    preservedDeleted,
                    false,
                    null);
            }
            catch
            {
                settings.OrphanedCategoryFolders =
                    originalOrphans;

                foreach (var preserved in
                         preservedDeletedMoves.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (Directory.Exists(preserved.Target) &&
                            !Directory.Exists(
                                preserved.Deleted.Temporary))
                        {
                            Directory.Move(
                                preserved.Target,
                                preserved.Deleted.Temporary);
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (var created in createdFolders
                             .OrderByDescending(path => path.Length))
                {
                    TryDeleteEmptyDirectory(created);
                }

                RollBackMoves(
                    phaseOneCompleted,
                    phaseTwoCompleted);

                foreach (var deleted in
                         deletedStaged.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!Directory.Exists(deleted.Temporary) &&
                            !Directory.Exists(deleted.Source))
                        {
                            Directory.CreateDirectory(
                                deleted.Temporary);
                        }

                        if (Directory.Exists(deleted.Temporary) &&
                            !Directory.Exists(deleted.Source))
                        {
                            Directory.Move(
                                deleted.Temporary,
                                deleted.Source);
                        }
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            settings.OrphanedCategoryFolders =
                originalOrphans;

            return CategoryFolderSyncResult.Failed(
                $"No se pudieron sincronizar las carpetas físicas: {ex.Message}");
        }
    }

    private static void ValidateTargets(
        AppSettings settings,
        string destinationRoot,
        IReadOnlyList<FolderMove> moves,
        IReadOnlyList<DeletedFolderMove> deletedMoves,
        IReadOnlyList<CategorySettings> nextCategories)
    {
        var releasedPaths = moves
            .Select(move => NormalizeDirectoryPath(move.Source))
            .Concat(
                deletedMoves.Select(move =>
                    NormalizeDirectoryPath(move.Source)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var deleted in deletedMoves)
        {
            EnsurePathInsideRoot(
                deleted.Source,
                destinationRoot);
            EnsurePathInsideRoot(
                deleted.Temporary,
                destinationRoot);

            ProtectedFolderService.EnsureAllowed(
                settings,
                deleted.Source,
                deleted.Temporary);
        }

        foreach (var move in moves)
        {
            EnsurePathInsideRoot(
                move.Source,
                destinationRoot);
            EnsurePathInsideRoot(
                move.Target,
                destinationRoot);
            EnsurePathInsideRoot(
                move.Temporary,
                destinationRoot);

            ProtectedFolderService.EnsureAllowed(
                settings,
                move.Source,
                move.Target,
                move.Temporary);

            if (File.Exists(move.Target))
            {
                throw new IOException(
                    $"Existe un archivo ocupando el destino {move.Target}.");
            }

            if (Directory.Exists(move.Target) &&
                !releasedPaths.Contains(
                    NormalizeDirectoryPath(move.Target)))
            {
                throw new IOException(
                    $"Ya existe una carpeta ajena a BandaNV en {move.Target}.");
            }
        }

        var duplicateTargets = nextCategories
            .Select(category =>
                NormalizeDirectoryPath(
                    CategoryService.GetFolderPath(
                        destinationRoot,
                        category.Order,
                        category.Name)))
            .GroupBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateTargets is not null)
        {
            throw new IOException(
                "Dos categorías intentan usar la misma carpeta física.");
        }
    }

    private static int CleanupTrackedOrphanFolders(
        AppSettings settings,
        string destinationRoot,
        bool deleteEmpty,
        CancellationToken cancellationToken)
    {
        settings.OrphanedCategoryFolders ??= [];

        var deletedCount = 0;
        var retained =
            new List<string>();

        foreach (var trackedPath in
                 settings.OrphanedCategoryFolders
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalized;

            try
            {
                normalized =
                    NormalizeDirectoryPath(
                        trackedPath);
            }
            catch
            {
                continue;
            }

            // Una carpeta registrada solo se administra mientras siga siendo
            // hija directa del destino con el que se está trabajando.
            if (!IsDirectChildOfRoot(
                    normalized,
                    destinationRoot))
            {
                retained.Add(trackedPath);
                continue;
            }

            if (!Directory.Exists(normalized))
            {
                continue;
            }

            if (!deleteEmpty ||
                ProtectedFolderService.IsProtected(settings, normalized))
            {
                retained.Add(normalized);
                continue;
            }

            try
            {
                if (Directory
                    .EnumerateFileSystemEntries(
                        normalized)
                    .Any())
                {
                    retained.Add(normalized);
                    continue;
                }

                Directory.Delete(
                    normalized,
                    recursive: false);

                deletedCount++;
            }
            catch
            {
                retained.Add(normalized);
            }
        }

        settings.OrphanedCategoryFolders =
            retained
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        return deletedCount;
    }

    private static void TrackOrphanFolder(
        AppSettings settings,
        string path)
    {
        var normalized =
            NormalizeDirectoryPath(path);

        if (!settings.OrphanedCategoryFolders.Contains(
                normalized,
                StringComparer.OrdinalIgnoreCase))
        {
            settings.OrphanedCategoryFolders.Add(
                normalized);
        }
    }

    private static string BuildAvailableOrphanPath(
        string destinationRoot,
        string categoryName)
    {
        var baseName =
            string.IsNullOrWhiteSpace(categoryName)
                ? "Categoría eliminada"
                : categoryName.Trim();

        var candidate =
            Path.Combine(
                destinationRoot,
                baseName);

        if (!Directory.Exists(candidate) &&
            !File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 1; ; index++)
        {
            candidate =
                Path.Combine(
                    destinationRoot,
                    $"{baseName} ({index})");

            if (!Directory.Exists(candidate) &&
                !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsDirectChildOfRoot(
        string path,
        string root)
    {
        var normalizedPath =
            NormalizeDirectoryPath(path);
        var normalizedRoot =
            NormalizeDirectoryPath(root);

        var parent =
            Path.GetDirectoryName(
                normalizedPath);

        return !string.IsNullOrWhiteSpace(parent) &&
               NormalizeDirectoryPath(parent).Equals(
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void RollBackMoves(
        IReadOnlyList<FolderMove> phaseOneCompleted,
        IReadOnlyList<FolderMove> phaseTwoCompleted)
    {
        foreach (var move in phaseTwoCompleted.Reverse())
        {
            try
            {
                if (Directory.Exists(move.Target) &&
                    !Directory.Exists(move.Temporary))
                {
                    Directory.Move(
                        move.Target,
                        move.Temporary);
                }
            }
            catch
            {
            }
        }

        foreach (var move in phaseOneCompleted.Reverse())
        {
            try
            {
                if (Directory.Exists(move.Temporary) &&
                    !Directory.Exists(move.Source))
                {
                    Directory.Move(
                        move.Temporary,
                        move.Source);
                }
            }
            catch
            {
            }
        }
    }

    private static string BuildTemporaryPath(
        string destinationRoot,
        string categoryId)
    {
        string candidate;

        do
        {
            candidate = Path.Combine(
                destinationRoot,
                $".bandanv_category_{categoryId}_{Guid.NewGuid():N}");
        }
        while (Directory.Exists(candidate) ||
               File.Exists(candidate));

        return candidate;
    }

    private static void EnsurePathInsideRoot(
        string path,
        string root)
    {
        var fullPath =
            NormalizeDirectoryPath(path);
        var fullRoot =
            NormalizeDirectoryPath(root);

        var prefix =
            fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Una carpeta de categoría quedó fuera del destino configurado.");
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

    private static void TryDeleteEmptyDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(
                    path,
                    recursive: false);
            }
        }
        catch
        {
        }
    }

    private sealed record FolderMove(
        string Source,
        string Target,
        string Temporary);

    private sealed record DeletedFolderMove(
        string CategoryId,
        string CategoryName,
        string Source,
        string Temporary);

    private sealed record PreservedDeletedFolderMove(
        DeletedFolderMove Deleted,
        string Target);
}
