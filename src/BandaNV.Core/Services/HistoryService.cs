using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<OrganizationExecutionRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        PortablePaths.EnsureDirectories();

        var records = new List<(OrganizationExecutionRecord Record, string HistoryPath)>();

        foreach (var historyPath in Directory.EnumerateFiles(
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

                var record = await JsonSerializer.DeserializeAsync<OrganizationExecutionRecord>(
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

                records.Add((record, historyPath));
                await EnsureHumanLogAsync(
                    record,
                    historyPath,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Un registro dañado no debe impedir mostrar el resto.
            }
        }

        return records
            .OrderByDescending(item => item.Record.StartedAt)
            .Select(item => item.Record)
            .ToList();
    }

    private static async Task EnsureHumanLogAsync(
        OrganizationExecutionRecord record,
        string historyPath,
        CancellationToken cancellationToken)
    {
        var logName =
            Path.GetFileNameWithoutExtension(historyPath) + ".txt";
        var logPath =
            Path.Combine(PortablePaths.LogsDirectory, logName);

        if (File.Exists(logPath))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                logPath,
                OrganizationExecutionService.FormatHumanLog(record),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
        catch
        {
            // El historial sigue siendo utilizable aunque falle el log humano.
        }
    }
}
