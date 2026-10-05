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

            Directory.CreateDirectory(destinationRoot);
        }

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

        try
        {
            ValidateTargets(
                destinationRoot,
                moves,
                nextCategories);

            var createdFolders = new List<string>();
            var phaseOneCompleted = new List<FolderMove>();
            var phaseTwoCompleted = new List<FolderMove>();

            try
            {
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

                        Directory.CreateDirectory(folder);
                        createdFolders.Add(folder);
                        createdCount++;
                    }
                }

                var deletedEmpty = 0;
                var preservedDeleted = 0;

                foreach (var previous in previousCategories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (nextById.ContainsKey(previous.Id))
                    {
                        continue;
                    }

                    var deletedCategoryFolder =
                        CategoryService.GetFolderPath(
                            destinationRoot,
                            previous.Order,
                            previous.Name);

                    if (!Directory.Exists(deletedCategoryFolder))
                    {
                        continue;
                    }

                    if (Directory
                        .EnumerateFileSystemEntries(
                            deletedCategoryFolder)
                        .Any())
                    {
                        preservedDeleted++;
                        continue;
                    }

                    Directory.Delete(
                        deletedCategoryFolder,
                        recursive: false);

                    deletedEmpty++;
                }

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
                RollBackMoves(
                    phaseOneCompleted,
                    phaseTwoCompleted);

                foreach (var created in createdFolders
                             .OrderByDescending(path => path.Length))
                {
                    TryDeleteEmptyDirectory(created);
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
            return CategoryFolderSyncResult.Failed(
                $"No se pudieron sincronizar las carpetas físicas: {ex.Message}");
        }
    }

    private static void ValidateTargets(
        string destinationRoot,
        IReadOnlyList<FolderMove> moves,
        IReadOnlyList<CategorySettings> nextCategories)
    {
        var sources = moves
            .Select(move => NormalizeDirectoryPath(move.Source))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            if (File.Exists(move.Target))
            {
                throw new IOException(
                    $"Existe un archivo ocupando el destino {move.Target}.");
            }

            if (Directory.Exists(move.Target) &&
                !sources.Contains(
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
}
