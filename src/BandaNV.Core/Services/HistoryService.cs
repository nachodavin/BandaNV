using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed record HistoryMaintenanceResult(
    int DeletedExecutions,
    int DeletedLogs,
    int DeletedBackupDirectories,
    int ProtectedExecutions);

public sealed class HistoryService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<OrganizationExecutionRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PortablePaths.EnsureDirectories();

            var records =
                await LoadEntriesAsync(cancellationToken);

            foreach (var entry in records)
            {
                await EnsureHumanLogAsync(
                    entry.Record,
                    entry.HistoryPath,
                    cancellationToken);
            }

            return records
                .OrderByDescending(item => item.Record.StartedAt)
                .Select(item => item.Record)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryMaintenanceResult> ApplyRetentionAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            PortablePaths.EnsureDirectories();

            var entries =
                await LoadEntriesAsync(cancellationToken);
            var cutoff =
                GetRetentionCutoff(settings.HistoryRetention);

            var deletedExecutions = 0;
            var deletedLogs = 0;
            var deletedBackupDirectories = 0;
            var protectedExecutions = 0;

            var retainedExecutionIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            var activeExecutionIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            var knownExecutionIds =
                new HashSet<string>(
                    entries.Select(entry =>
                        entry.Record.ExecutionId),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var record = entry.Record;
                var isProtected = IsProtected(record);

                if (isProtected)
                {
                    protectedExecutions++;
                    retainedExecutionIds.Add(
                        record.ExecutionId);
                    activeExecutionIds.Add(
                        record.ExecutionId);
                    continue;
                }

                var expired =
                    cutoff.HasValue &&
                    record.StartedAt < cutoff.Value;

                if (!expired)
                {
                    retainedExecutionIds.Add(
                        record.ExecutionId);
                    continue;
                }

                if (TryDeleteFile(entry.HistoryPath))
                {
                    deletedExecutions++;
                }

                var logPath =
                    GetMatchingLogPath(entry.HistoryPath);

                if (TryDeleteFile(logPath))
                {
                    deletedLogs++;
                }

                var backupDirectory =
                    GetBackupDirectory(record.ExecutionId);

                if (TryDeleteDirectory(
                        backupDirectory))
                {
                    deletedBackupDirectories++;
                }
            }

            // Si Undo está desactivado, los backups de ejecuciones ya
            // finalizadas dejan de ser necesarios. Los de una ejecución
            // activa se conservan siempre por seguridad.
            var backupIdsToKeep =
                new HashSet<string>(
                    activeExecutionIds,
                    StringComparer.OrdinalIgnoreCase);

            if (settings.UndoEnabled)
            {
                foreach (var entry in entries)
                {
                    var record = entry.Record;

                    if (!retainedExecutionIds.Contains(
                            record.ExecutionId) ||
                        !record.Type.Equals(
                            "ORGANIZE",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (record.Items.Any(item =>
                            !string.IsNullOrWhiteSpace(
                                item.ReplacedBackupPath)))
                    {
                        backupIdsToKeep.Add(
                            record.ExecutionId);
                    }
                }
            }

            var replacedRoot =
                Path.Combine(
                    PortablePaths.HistoryDirectory,
                    "replaced");

            if (Directory.Exists(replacedRoot))
            {
                foreach (var directory in
                         Directory.EnumerateDirectories(
                             replacedRoot,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var executionId =
                        Path.GetFileName(directory);

                    if (backupIdsToKeep.Contains(
                            executionId))
                    {
                        continue;
                    }

                    // Para un directorio sin journal válido aplicamos una
                    // política conservadora: solo se elimina automáticamente
                    // si la retención tiene límite y el propio directorio ya
                    // quedó fuera de esa ventana. "Siempre" nunca borra un
                    // backup huérfano por su cuenta.
                    if (!knownExecutionIds.Contains(
                            executionId))
                    {
                        if (!cutoff.HasValue ||
                            GetSafeLastWriteTime(directory) >=
                            cutoff.Value)
                        {
                            continue;
                        }
                    }

                    if (TryDeleteDirectory(directory))
                    {
                        deletedBackupDirectories++;
                    }
                }
            }

            // Logs modernos que quedaron sin journal también obedecen la
            // retención cuando su fecha puede extraerse con certeza.
            if (cutoff.HasValue)
            {
                var retainedLogNames =
                    new HashSet<string>(
                        entries
                            .Where(entry =>
                                retainedExecutionIds.Contains(
                                    entry.Record.ExecutionId))
                            .Select(entry =>
                                Path.GetFileName(
                                    GetMatchingLogPath(
                                        entry.HistoryPath))),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var logPath in
                         Directory.EnumerateFiles(
                             PortablePaths.LogsDirectory,
                             "*.txt",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (retainedLogNames.Contains(
                            Path.GetFileName(logPath)))
                    {
                        continue;
                    }

                    if (!TryGetBandaLogDate(
                            logPath,
                            out var logDate) ||
                        logDate >= cutoff.Value)
                    {
                        continue;
                    }

                    if (TryDeleteFile(logPath))
                    {
                        deletedLogs++;
                    }
                }
            }

            return new HistoryMaintenanceResult(
                deletedExecutions,
                deletedLogs,
                deletedBackupDirectories,
                protectedExecutions);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryMaintenanceResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PortablePaths.EnsureDirectories();

            var entries =
                await LoadEntriesAsync(cancellationToken);

            var protectedHistoryPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            var protectedLogPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            var protectedExecutionIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries.Where(entry =>
                         IsProtected(entry.Record)))
            {
                protectedHistoryPaths.Add(
                    Path.GetFullPath(
                        entry.HistoryPath));
                protectedLogPaths.Add(
                    Path.GetFullPath(
                        GetMatchingLogPath(
                            entry.HistoryPath)));
                protectedExecutionIds.Add(
                    entry.Record.ExecutionId);
            }

            var deletedExecutions = 0;
            var deletedLogs = 0;
            var deletedBackupDirectories = 0;

            foreach (var historyPath in
                     Directory.EnumerateFiles(
                         PortablePaths.HistoryDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (protectedHistoryPaths.Contains(
                        Path.GetFullPath(historyPath)))
                {
                    continue;
                }

                if (TryDeleteFile(historyPath))
                {
                    deletedExecutions++;
                }
            }

            foreach (var logPath in
                     Directory.EnumerateFiles(
                         PortablePaths.LogsDirectory,
                         "*.txt",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (protectedLogPaths.Contains(
                        Path.GetFullPath(logPath)))
                {
                    continue;
                }

                if (TryDeleteFile(logPath))
                {
                    deletedLogs++;
                }
            }

            var replacedRoot =
                Path.Combine(
                    PortablePaths.HistoryDirectory,
                    "replaced");

            if (Directory.Exists(replacedRoot))
            {
                foreach (var directory in
                         Directory.EnumerateDirectories(
                             replacedRoot,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (protectedExecutionIds.Contains(
                            Path.GetFileName(directory)))
                    {
                        continue;
                    }

                    if (TryDeleteDirectory(directory))
                    {
                        deletedBackupDirectories++;
                    }
                }
            }

            return new HistoryMaintenanceResult(
                deletedExecutions,
                deletedLogs,
                deletedBackupDirectories,
                protectedExecutionIds.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<HistoryEntry>> LoadEntriesAsync(
        CancellationToken cancellationToken)
    {
        var records = new List<HistoryEntry>();

        foreach (var historyPath in
                 Directory.EnumerateFiles(
                     PortablePaths.HistoryDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    historyPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                var record =
                    await JsonSerializer.DeserializeAsync<OrganizationExecutionRecord>(
                        stream,
                        JsonOptions,
                        cancellationToken);

                if (record is null ||
                    !record.Format.StartsWith(
                        "BandaNV.Execution.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                records.Add(
                    new HistoryEntry(
                        record,
                        historyPath));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Un registro dañado no impide usar ni mantener el resto.
                // La limpieza manual sí podrá eliminarlo porque pertenece
                // al directorio administrado de Historial.
            }
        }

        return records;
    }

    private static async Task EnsureHumanLogAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        CancellationToken cancellationToken)
    {
        var logPath =
            GetMatchingLogPath(historyPath);

        if (File.Exists(logPath))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                logPath,
                OrganizationExecutionService.FormatHumanLog(
                    record),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
        catch
        {
            // El historial sigue siendo utilizable aunque falle el log humano.
        }
    }

    private static DateTime? GetRetentionCutoff(
        string? retention)
    {
        var now = DateTime.Now;

        return retention?.Trim() switch
        {
            "30 días" => now.AddDays(-30),
            "90 días" => now.AddDays(-90),
            "1 año" => now.AddYears(-1),
            _ => null
        };
    }

    private static bool IsProtected(
        OrganizationExecutionRecord record) =>
        record.Status ==
            OrganizationExecutionStatus.Running ||
        record.Items.Any(item =>
            item.Status ==
            OrganizationExecutionItemStatus.Moving);

    private static string GetMatchingLogPath(
        string historyPath) =>
        Path.Combine(
            PortablePaths.LogsDirectory,
            Path.GetFileNameWithoutExtension(
                historyPath) +
            ".txt");

    private static string GetBackupDirectory(
        string executionId) =>
        Path.Combine(
            PortablePaths.HistoryDirectory,
            "replaced",
            executionId);

    private static bool TryGetBandaLogDate(
        string logPath,
        out DateTime date)
    {
        date = default;

        var fileName =
            Path.GetFileNameWithoutExtension(
                logPath);

        if (!fileName.StartsWith(
                "BandaNV_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw =
            fileName["BandaNV_".Length..];

        // Los nombres únicos pueden terminar en __2, __3, etc.
        var duplicateSeparator =
            raw.LastIndexOf(
                "__",
                StringComparison.Ordinal);

        if (duplicateSeparator >
            "dd-MM-yyyy____HH-mm-ss".Length - 1)
        {
            var possibleSuffix =
                raw[(duplicateSeparator + 2)..];

            if (int.TryParse(
                    possibleSuffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                raw =
                    raw[..duplicateSeparator];
            }
        }

        return DateTime.TryParseExact(
            raw,
            "dd-MM-yyyy____HH-mm-ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static DateTime GetSafeLastWriteTime(
        string path)
    {
        try
        {
            return Directory.GetLastWriteTime(path);
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }

    private static bool TryDeleteFile(
        string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteDirectory(
        string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path))
            {
                return false;
            }

            File.SetAttributes(
                path,
                FileAttributes.Normal);

            Directory.Delete(
                path,
                recursive: true);

            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private sealed record HistoryEntry(
        OrganizationExecutionRecord Record,
        string HistoryPath);
}
