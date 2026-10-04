using System.Text.Json;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class SettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PortablePaths.EnsureDirectories();

            if (!File.Exists(PortablePaths.SettingsFile))
            {
                Current = AppSettings.CreateDefault();
                await SaveInternalAsync(Current, cancellationToken);
                return Current;
            }

            try
            {
                await using var stream = File.OpenRead(PortablePaths.SettingsFile);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                Current = Normalize(loaded ?? AppSettings.CreateDefault());
                return Current;
            }
            catch
            {
                BackupCorruptSettingsFile();

                Current = AppSettings.CreateDefault();
                await SaveInternalAsync(Current, cancellationToken);
                return Current;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            PortablePaths.EnsureDirectories();
            Current = Normalize(settings);
            await SaveInternalAsync(Current, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await SaveAsync(AppSettings.CreateDefault(), cancellationToken);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        settings.PrimaryColor = NormalizeHex(settings.PrimaryColor, "#123A34");
        settings.SecondaryColor = NormalizeHex(settings.SecondaryColor, "#4FE0C6");

        settings.Categories ??= [];

        foreach (var category in settings.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Id))
            {
                category.Id = Guid.NewGuid().ToString("D");
            }

            category.Name = category.Name.Trim();

            category.Extensions = category.Extensions
                .Select(NormalizeExtension)
                .Where(extension => extension.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var ordered = settings.Categories
            .OrderBy(category => category.Order)
            .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Order = index + 1;
        }

        settings.Categories = ordered;
        return settings;
    }

    private static string NormalizeExtension(string value)
    {
        var extension = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (extension.Length == 0)
        {
            return string.Empty;
        }

        return extension.StartsWith('.')
            ? extension
            : $".{extension}";
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var hex = value.Trim().TrimStart('#');

        return hex.Length == 6 &&
               hex.All(Uri.IsHexDigit)
            ? $"#{hex.ToUpperInvariant()}"
            : fallback;
    }

    private static async Task SaveInternalAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var tempPath = PortablePaths.SettingsFile + ".tmp";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                JsonOptions,
                cancellationToken);

            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, PortablePaths.SettingsFile, true);
    }

    private static void BackupCorruptSettingsFile()
    {
        try
        {
            if (!File.Exists(PortablePaths.SettingsFile))
            {
                return;
            }

            var backupName =
                $"bandanv_settings_corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            File.Copy(
                PortablePaths.SettingsFile,
                Path.Combine(PortablePaths.ConfigDirectory, backupName),
                overwrite: false);
        }
        catch
        {
            // Si la copia de seguridad falla, la aplicación igual debe poder
            // restaurar una configuración válida y arrancar.
        }
    }
}
