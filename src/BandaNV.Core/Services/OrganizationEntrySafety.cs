using System.Security.Cryptography;
using System.Text;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed record OrganizationEntrySnapshot(
    OrganizationAnalysisItemKind Kind,
    int FileCount,
    long TotalSizeBytes,
    long ModifiedUtcTicks,
    string? ContentFingerprint);

public static class OrganizationEntrySafety
{
    private const long TimestampToleranceTicks =
        20_000_000L;

    public static bool Exists(
        string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (File.Exists(path) ||
         Directory.Exists(path));

    public static OrganizationEntrySnapshot GetSnapshot(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(path))
        {
            return GetDirectorySnapshot(
                path,
                cancellationToken);
        }

        var file =
            new FileInfo(
                path);

        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "El elemento ya no existe.",
                path);
        }

        return new OrganizationEntrySnapshot(
            OrganizationAnalysisItemKind.File,
            FileCount: 1,
            TotalSizeBytes:
                file.Length,
            ModifiedUtcTicks:
                file.LastWriteTimeUtc.Ticks,
            ContentFingerprint:
                null);
    }

    public static OrganizationEntrySnapshot GetDirectorySnapshot(
        string root,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot =
            Path.GetFullPath(
                root);

        if (!Directory.Exists(
                normalizedRoot))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta ya no existe: {normalizedRoot}");
        }

        var rootAttributes =
            File.GetAttributes(
                normalizedRoot);

        if ((rootAttributes &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "La carpeta es un vínculo o punto de reanálisis.");
        }

        var pending =
            new Stack<string>();

        pending.Push(
            normalizedRoot);

        var fingerprintParts =
            new List<string>();

        var fileCount =
            0;

        var totalSize =
            0L;

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
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"No se pudo leer completamente la carpeta: {current}",
                    ex);
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attributes =
                    File.GetAttributes(
                        filePath);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {filePath}");
                }

                var info =
                    new FileInfo(
                        filePath);

                var relativePath =
                    Path.GetRelativePath(
                        normalizedRoot,
                        info.FullName);

                fileCount++;
                totalSize +=
                    info.Length;

                fingerprintParts.Add(
                    $"{relativePath}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}");
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attributes =
                    File.GetAttributes(
                        directory);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {directory}");
                }

                pending.Push(
                    directory);
            }
        }

        var payload =
            string.Join(
                "\n",
                fingerprintParts
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase));

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    payload));

        return new OrganizationEntrySnapshot(
            OrganizationAnalysisItemKind.Folder,
            fileCount,
            totalSize,
            Directory.GetLastWriteTimeUtc(
                    normalizedRoot)
                .Ticks,
            Convert.ToHexString(
                    hash)
                .ToLowerInvariant());
    }

    public static bool MatchesExpected(
        string path,
        OrganizationExecutionItemRecord item)
    {
        try
        {
            if (item.IsDirectory)
            {
                if (!Directory.Exists(path) ||
                    string.IsNullOrWhiteSpace(
                        item.ContentFingerprint))
                {
                    return false;
                }

                var snapshot =
                    GetDirectorySnapshot(
                        path);

                return snapshot.FileCount ==
                           item.ContainedFileCount &&
                       snapshot.TotalSizeBytes ==
                           item.SizeBytes &&
                       snapshot.ContentFingerprint?.Equals(
                           item.ContentFingerprint,
                           StringComparison.OrdinalIgnoreCase) ==
                       true;
            }

            var info =
                new FileInfo(
                    path);

            return info.Exists &&
                   info.Length ==
                   item.SizeBytes &&
                   Math.Abs(
                       info.LastWriteTimeUtc.Ticks -
                       item.ModifiedUtcTicks) <=
                   TimestampToleranceTicks;
        }
        catch
        {
            return false;
        }
    }

    public static bool MatchesReplacement(
        string path,
        OrganizationExecutionItemRecord originalItem)
    {
        if (!originalItem.ReplacedSizeBytes.HasValue ||
            !originalItem.ReplacedModifiedUtcTicks.HasValue)
        {
            return false;
        }

        try
        {
            var replacementKind =
                originalItem.ReplacedKind ??
                OrganizationAnalysisItemKind.File;

            if (replacementKind ==
                OrganizationAnalysisItemKind.Folder)
            {
                if (!Directory.Exists(path) ||
                    !originalItem.ReplacedContainedFileCount.HasValue ||
                    string.IsNullOrWhiteSpace(
                        originalItem.ReplacedContentFingerprint))
                {
                    return false;
                }

                var snapshot =
                    GetDirectorySnapshot(
                        path);

                return snapshot.FileCount ==
                           originalItem.ReplacedContainedFileCount.Value &&
                       snapshot.TotalSizeBytes ==
                           originalItem.ReplacedSizeBytes.Value &&
                       snapshot.ContentFingerprint?.Equals(
                           originalItem.ReplacedContentFingerprint,
                           StringComparison.OrdinalIgnoreCase) ==
                       true;
            }

            var info =
                new FileInfo(
                    path);

            return info.Exists &&
                   info.Length ==
                   originalItem.ReplacedSizeBytes.Value &&
                   Math.Abs(
                       info.LastWriteTimeUtc.Ticks -
                       originalItem.ReplacedModifiedUtcTicks.Value) <=
                   TimestampToleranceTicks;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> AreIdenticalAsync(
        string leftPath,
        string rightPath,
        OrganizationExecutionItemRecord expectedItem,
        CancellationToken cancellationToken)
    {
        if (expectedItem.IsDirectory)
        {
            try
            {
                var left =
                    GetDirectorySnapshot(
                        leftPath,
                        cancellationToken);

                var right =
                    GetDirectorySnapshot(
                        rightPath,
                        cancellationToken);

                return left.FileCount ==
                           right.FileCount &&
                       left.TotalSizeBytes ==
                           right.TotalSizeBytes &&
                       left.ContentFingerprint?.Equals(
                           right.ContentFingerprint,
                           StringComparison.OrdinalIgnoreCase) ==
                       true &&
                       MatchesExpected(
                           leftPath,
                           expectedItem) &&
                       MatchesExpected(
                           rightPath,
                           expectedItem);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        return await FilesAreIdenticalAsync(
            leftPath,
            rightPath,
            cancellationToken);
    }

    public static void MoveEntrySafely(
        string source,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(source))
        {
            MoveDirectorySafely(
                source,
                destination,
                cancellationToken);

            return;
        }

        if (File.Exists(source))
        {
            MoveFileSafely(
                source,
                destination);

            return;
        }

        throw new FileNotFoundException(
            "El elemento que debía moverse ya no existe.",
            source);
    }

    public static bool TryDeleteEntry(
        string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);

                return !Directory.Exists(path);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return !File.Exists(path) &&
                   !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static int CleanupTemporaryEntries(
        string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(
                destinationPath))
        {
            return 0;
        }

        try
        {
            var fullPath =
                Path.GetFullPath(
                    destinationPath);

            var parent =
                Path.GetDirectoryName(
                    fullPath);

            if (string.IsNullOrWhiteSpace(parent) ||
                !Directory.Exists(parent))
            {
                return 0;
            }

            var name =
                Path.GetFileName(
                    fullPath);

            var pattern =
                name +
                ".bandanv_tmp_*";

            var deleted =
                0;

            foreach (var file in Directory.EnumerateFiles(
                         parent,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (TryDeleteEntry(file))
                {
                    deleted++;
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         parent,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (TryDeleteEntry(directory))
                {
                    deleted++;
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    private static void MoveFileSafely(
        string source,
        string destination)
    {
        if (Exists(destination))
        {
            throw new IOException(
                $"Ya existe un elemento en el destino: {destination}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                destination)!);

        try
        {
            File.Move(
                source,
                destination);

            return;
        }
        catch (IOException)
        {
            if (!File.Exists(source) ||
                Exists(destination))
            {
                throw;
            }
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

            var sourceLength =
                new FileInfo(source).Length;

            var copiedLength =
                new FileInfo(
                    temporaryDestination).Length;

            if (sourceLength !=
                copiedLength)
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
                File.Delete(
                    source);
            }
            catch
            {
                TryDeleteEntry(
                    destination);

                throw;
            }
        }
        finally
        {
            TryDeleteEntry(
                temporaryDestination);
        }
    }

    private static void MoveDirectorySafely(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"La carpeta de origen ya no existe: {source}");
        }

        if (Exists(destination))
        {
            throw new IOException(
                $"Ya existe un elemento en el destino: {destination}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                destination)!);

        try
        {
            Directory.Move(
                source,
                destination);

            return;
        }
        catch (IOException)
        {
            if (!Directory.Exists(source) ||
                Exists(destination))
            {
                throw;
            }
        }

        var temporaryDestination =
            destination +
            $".bandanv_tmp_{Guid.NewGuid():N}";

        try
        {
            CopyDirectoryTree(
                source,
                temporaryDestination,
                cancellationToken);

            var sourceSnapshot =
                GetDirectorySnapshot(
                    source,
                    cancellationToken);

            var copySnapshot =
                GetDirectorySnapshot(
                    temporaryDestination,
                    cancellationToken);

            if (sourceSnapshot.FileCount !=
                    copySnapshot.FileCount ||
                sourceSnapshot.TotalSizeBytes !=
                    copySnapshot.TotalSizeBytes ||
                !string.Equals(
                    sourceSnapshot.ContentFingerprint,
                    copySnapshot.ContentFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "La copia de la carpeta entre unidades no pudo validarse.");
            }

            Directory.Move(
                temporaryDestination,
                destination);

            try
            {
                Directory.Delete(
                    source,
                    recursive: true);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "La carpeta se copió y verificó correctamente en destino, pero no se pudo eliminar por completo el origen. Se conservaron las copias para evitar pérdida de datos.",
                    ex);
            }
        }
        finally
        {
            if (Directory.Exists(
                    temporaryDestination))
            {
                try
                {
                    Directory.Delete(
                        temporaryDestination,
                        recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    private static void CopyDirectoryTree(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var normalizedSource =
            Path.GetFullPath(
                source);

        var pending =
            new Stack<(string Source, string Destination)>();

        pending.Push(
            (normalizedSource, destination));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current =
                pending.Pop();

            var directoryAttributes =
                File.GetAttributes(
                    current.Source);

            if ((directoryAttributes &
                 FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"No se puede copiar un vínculo o punto de reanálisis: {current.Source}");
            }

            Directory.CreateDirectory(
                current.Destination);

            foreach (var filePath in Directory.GetFiles(
                         current.Source,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileAttributes =
                    File.GetAttributes(
                        filePath);

                if ((fileAttributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {filePath}");
                }

                var destinationFile =
                    Path.Combine(
                        current.Destination,
                        Path.GetFileName(
                            filePath));

                File.Copy(
                    filePath,
                    destinationFile,
                    overwrite: false);

                File.SetLastWriteTimeUtc(
                    destinationFile,
                    File.GetLastWriteTimeUtc(
                        filePath));
            }

            foreach (var directoryPath in Directory.GetDirectories(
                         current.Source,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attributes =
                    File.GetAttributes(
                        directoryPath);

                if ((attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"La carpeta contiene un vínculo o punto de reanálisis: {directoryPath}");
                }

                pending.Push(
                    (
                        directoryPath,
                        Path.Combine(
                            current.Destination,
                            Path.GetFileName(
                                directoryPath))));
            }
        }
    }

    private static async Task<bool> FilesAreIdenticalAsync(
        string leftPath,
        string rightPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var leftInfo =
                new FileInfo(
                    leftPath);

            var rightInfo =
                new FileInfo(
                    rightPath);

            if (!leftInfo.Exists ||
                !rightInfo.Exists ||
                leftInfo.Length !=
                rightInfo.Length)
            {
                return false;
            }

            await using var left =
                new FileStream(
                    leftPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize:
                        1024 * 128,
                    useAsync:
                        true);

            await using var right =
                new FileStream(
                    rightPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize:
                        1024 * 128,
                    useAsync:
                        true);

            using var leftHash =
                IncrementalHash.CreateHash(
                    HashAlgorithmName.SHA256);

            using var rightHash =
                IncrementalHash.CreateHash(
                    HashAlgorithmName.SHA256);

            var leftBuffer =
                new byte[1024 * 128];

            var rightBuffer =
                new byte[1024 * 128];

            while (true)
            {
                var leftRead =
                    await left.ReadAsync(
                        leftBuffer,
                        cancellationToken);

                var rightRead =
                    await right.ReadAsync(
                        rightBuffer,
                        cancellationToken);

                if (leftRead !=
                    rightRead)
                {
                    return false;
                }

                if (leftRead == 0)
                {
                    break;
                }

                leftHash.AppendData(
                    leftBuffer,
                    0,
                    leftRead);

                rightHash.AppendData(
                    rightBuffer,
                    0,
                    rightRead);
            }

            return CryptographicOperations.FixedTimeEquals(
                leftHash.GetHashAndReset(),
                rightHash.GetHashAndReset());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
