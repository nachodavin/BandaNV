using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class UpdateService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            ClearFailedUpdateStateIfObsolete();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{AppVersionInfo.GitHubApiBase}/releases/latest");

            using var response =
                await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            var release =
                await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                    stream,
                    JsonOptions,
                    cancellationToken);

            if (release is null ||
                release.Draft ||
                release.Prerelease ||
                string.IsNullOrWhiteSpace(release.TagName))
            {
                return Error(
                    "GitHub no devolvió una Release estable compatible de BandaNV.");
            }

            if (!TryParseStableVersion(
                    AppVersionInfo.Tag,
                    out var installedVersion))
            {
                return Error(
                    "La versión instalada de BandaNV no tiene un formato reconocido.");
            }

            if (!TryParseStableVersion(
                    release.TagName,
                    out var availableVersion))
            {
                return Error(
                    "La última Release de GitHub tiene un tag que BandaNV no reconoce.");
            }

            var comparison =
                availableVersion.CompareTo(
                    installedVersion);

            var status =
                comparison > 0
                    ? UpdateCheckStatus.Available
                    : comparison == 0
                        ? UpdateCheckStatus.Current
                        : UpdateCheckStatus.LocalNewer;

            var failedTag =
                GetFailedUpdateTag();

            if (status == UpdateCheckStatus.Available &&
                !string.IsNullOrWhiteSpace(failedTag) &&
                release.TagName.Equals(
                    failedTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                status =
                    UpdateCheckStatus.FailedSuppressed;
            }

            var expectedAssetName =
                $"BandaNV_{release.TagName}.zip";

            var asset =
                release.Assets.FirstOrDefault(candidate =>
                    candidate.Name.Equals(
                        expectedAssetName,
                        StringComparison.OrdinalIgnoreCase));

            var message = string.Empty;

            if (status == UpdateCheckStatus.Available &&
                asset is null)
            {
                message =
                    $"La Release {release.TagName} existe, pero no incluye el paquete oficial {expectedAssetName}.";
            }
            else if (status == UpdateCheckStatus.Available &&
                     string.IsNullOrWhiteSpace(
                         asset?.Digest))
            {
                message =
                    "GitHub no publicó el SHA-256 del paquete oficial. Por seguridad, BandaNV no lo instalará.";
            }

            return new UpdateCheckResult(
                status,
                AppVersionInfo.Tag,
                release.TagName,
                release.TagName,
                release.Name ?? release.TagName,
                release.Body ?? string.Empty,
                release.HtmlUrl ?? string.Empty,
                asset?.Name ?? string.Empty,
                asset?.BrowserDownloadUrl ?? string.Empty,
                asset?.Digest ?? string.Empty,
                message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error(
                $"No se pudo consultar GitHub Releases. {ex.Message}");
        }
    }

    public async Task<PreparedUpdate> PrepareAsync(
        UpdateCheckResult release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Status != UpdateCheckStatus.Available)
        {
            throw new InvalidOperationException(
                "La Release indicada no está disponible para actualizar.");
        }

        if (!release.CanInstall)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(release.Message)
                    ? "La Release no contiene un paquete verificable."
                    : release.Message);
        }

        PortablePaths.EnsureDirectories();

        var token =
            Guid.NewGuid().ToString("N");

        var tempBase =
            Path.Combine(
                Path.GetTempPath(),
                "BandaNV");

        var workspaceRoot =
            Path.Combine(
                tempBase,
                $"update-{token}");

        var packagePath =
            Path.Combine(
                workspaceRoot,
                "BandaNV.zip");

        var extractDirectory =
            Path.Combine(
                workspaceRoot,
                "extracted");

        var backupDirectory =
            Path.Combine(
                workspaceRoot,
                "backup");

        var confirmationPath =
            Path.Combine(
                workspaceRoot,
                "confirmed.json");

        try
        {
            Directory.CreateDirectory(
                workspaceRoot);
            Directory.CreateDirectory(
                extractDirectory);
            Directory.CreateDirectory(
                backupDirectory);

            await DownloadAsync(
                release.AssetUrl,
                packagePath,
                progress,
                cancellationToken);

            await VerifyDigestAsync(
                packagePath,
                release.Digest,
                cancellationToken);

            ExtractPackageSafely(
                packagePath,
                extractDirectory);

            var stagedApplicationDirectory =
                Path.Combine(
                    extractDirectory,
                    "BandaNV");

            ValidateStagedPackage(
                stagedApplicationDirectory,
                release.Tag);

            var stagedUpdaterPath =
                Path.Combine(
                    stagedApplicationDirectory,
                    "NVupdate.exe");

            var updaterPath =
                Path.Combine(
                    workspaceRoot,
                    "NVupdate.exe");

            File.Copy(
                stagedUpdaterPath,
                updaterPath,
                overwrite: true);

            return new PreparedUpdate(
                release,
                workspaceRoot,
                stagedApplicationDirectory,
                updaterPath,
                backupDirectory,
                confirmationPath,
                token);
        }
        catch
        {
            TryDeleteWorkspace(
                workspaceRoot);
            throw;
        }
    }

    public void LaunchPreparedUpdate(
        PreparedUpdate prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        var appDirectory =
            PortablePaths.RootDirectory;

        var currentExecutable =
            Path.Combine(
                appDirectory,
                "BandaNV.exe");

        if (!File.Exists(currentExecutable))
        {
            throw new FileNotFoundException(
                "No se encontró BandaNV.exe en la carpeta actual.",
                currentExecutable);
        }

        var process =
            new ProcessStartInfo
            {
                FileName =
                    prepared.UpdaterPath,
                WorkingDirectory =
                    prepared.WorkspaceRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };

        process.ArgumentList.Add(
            "--parent-pid");
        process.ArgumentList.Add(
            Environment.ProcessId.ToString());

        process.ArgumentList.Add(
            "--app-dir");
        process.ArgumentList.Add(
            appDirectory);

        process.ArgumentList.Add(
            "--staged-dir");
        process.ArgumentList.Add(
            prepared.StagedApplicationDirectory);

        process.ArgumentList.Add(
            "--backup-dir");
        process.ArgumentList.Add(
            prepared.BackupDirectory);

        process.ArgumentList.Add(
            "--confirm-path");
        process.ArgumentList.Add(
            prepared.ConfirmationPath);

        process.ArgumentList.Add(
            "--token");
        process.ArgumentList.Add(
            prepared.Token);

        process.ArgumentList.Add(
            "--expected-version");
        process.ArgumentList.Add(
            prepared.Release.Tag);

        process.ArgumentList.Add(
            "--release-tag");
        process.ArgumentList.Add(
            prepared.Release.Tag);

        if (Process.Start(process) is null)
        {
            throw new InvalidOperationException(
                "No se pudo iniciar NVupdate.");
        }
    }

    public async Task ConfirmPendingUpdateAsync(
        IReadOnlyList<string> commandLineArguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            commandLineArguments);

        var confirmPath =
            GetArgumentValue(
                commandLineArguments,
                "--update-confirm-path");

        var token =
            GetArgumentValue(
                commandLineArguments,
                "--update-token");

        if (string.IsNullOrWhiteSpace(confirmPath) ||
            string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (!IsSafeUpdateWorkspacePath(
                confirmPath))
        {
            return;
        }

        try
        {
            var directory =
                Path.GetDirectoryName(
                    Path.GetFullPath(
                        confirmPath));

            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }

            var payload =
                new UpdateConfirmation
                {
                    Token = token,
                    Version =
                        AppVersionInfo.Tag,
                    ConfirmedAt =
                        DateTime.Now,
                    ProcessId =
                        Environment.ProcessId
                };

            var temporaryPath =
                confirmPath +
                $".tmp_{Guid.NewGuid():N}";

            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(
                    payload,
                    JsonOptions),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            File.Move(
                temporaryPath,
                confirmPath,
                overwrite: true);
        }
        catch
        {
            // Si el handshake falla, NVupdate detectará que no hubo
            // confirmación y hará rollback de forma automática.
        }
    }

    public void TryDeleteWorkspace(
        PreparedUpdate? prepared)
    {
        if (prepared is null)
        {
            return;
        }

        TryDeleteWorkspace(
            prepared.WorkspaceRoot);
    }

    private static HttpClient CreateHttpClient()
    {
        var client =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(15)
            };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "BandaNV-Updater/2.0");

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));

        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2022-11-28");

        return client;
    }

    private static async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/octet-stream"));

        using var response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes =
            response.Content.Headers.ContentLength;

        await using var input =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var output =
            new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 128,
                useAsync: true);

        var buffer =
            new byte[1024 * 128];

        long received = 0;

        while (true)
        {
            var read =
                await input.ReadAsync(
                    buffer,
                    cancellationToken);

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);

            received += read;

            var percentage =
                totalBytes.HasValue &&
                totalBytes.Value > 0
                    ? (int)Math.Clamp(
                        received * 100L /
                        totalBytes.Value,
                        0,
                        100)
                    : 0;

            progress?.Report(
                new UpdateDownloadProgress(
                    percentage,
                    received,
                    totalBytes));
        }

        await output.FlushAsync(
            cancellationToken);

        progress?.Report(
            new UpdateDownloadProgress(
                100,
                received,
                totalBytes));
    }

    private static async Task VerifyDigestAsync(
        string path,
        string digest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith(
                "sha256:",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "GitHub no publicó un SHA-256 válido para el paquete.");
        }

        var expected =
            digest["sha256:".Length..]
                .Trim();

        if (expected.Length != 64 ||
            expected.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "El SHA-256 publicado por GitHub no tiene un formato válido.");
        }

        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                useAsync: true);

        using var sha =
            SHA256.Create();

        var actualBytes =
            await sha.ComputeHashAsync(
                stream,
                cancellationToken);

        var actual =
            Convert.ToHexString(
                    actualBytes)
                .ToLowerInvariant();

        if (!actual.Equals(
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La verificación SHA-256 falló. El paquete descargado no coincide con el publicado por GitHub.");
        }
    }

    private static void ExtractPackageSafely(
        string packagePath,
        string extractDirectory)
    {
        var extractRoot =
            Path.GetFullPath(
                extractDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive =
            ZipFile.OpenRead(
                packagePath);

        foreach (var entry in archive.Entries)
        {
            var destinationPath =
                Path.GetFullPath(
                    Path.Combine(
                        extractDirectory,
                        entry.FullName));

            if (!destinationPath.StartsWith(
                    extractRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "El paquete contiene una ruta no permitida.");
            }

            if (string.IsNullOrEmpty(
                    entry.Name))
            {
                Directory.CreateDirectory(
                    destinationPath);
                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destinationPath)!);

            entry.ExtractToFile(
                destinationPath,
                overwrite: false);
        }
    }

    private static void ValidateStagedPackage(
        string stagedApplicationDirectory,
        string releaseTag)
    {
        if (!Directory.Exists(
                stagedApplicationDirectory))
        {
            throw new InvalidDataException(
                "El paquete no contiene la carpeta BandaNV esperada.");
        }

        var requiredFiles =
            new[]
            {
                "BandaNV.exe",
                "NVupdate.exe",
                "bandanv_update_manifest.json"
            };

        foreach (var required in requiredFiles)
        {
            var path =
                Path.Combine(
                    stagedApplicationDirectory,
                    required);

            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    $"El paquete no contiene {required}.");
            }
        }

        var manifestPath =
            Path.Combine(
                stagedApplicationDirectory,
                "bandanv_update_manifest.json");

        UpdatePackageManifest? manifest;

        try
        {
            manifest =
                JsonSerializer.Deserialize<UpdatePackageManifest>(
                    File.ReadAllText(
                        manifestPath,
                        Encoding.UTF8),
                    JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "El manifest del paquete no se pudo leer.",
                ex);
        }

        if (manifest is null ||
            !manifest.Format.Equals(
                UpdatePackageManifest.CurrentFormat,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "El manifest del paquete tiene un formato no compatible.");
        }

        if (!manifest.Version.Equals(
                releaseTag,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La versión declarada por el paquete no coincide con la Release descargada.");
        }

        var manifestFiles =
            new HashSet<string>(
                manifest.Files.Select(
                    NormalizeManifestPath),
                StringComparer.OrdinalIgnoreCase);

        foreach (var required in requiredFiles)
        {
            if (!manifestFiles.Contains(
                    NormalizeManifestPath(
                        required)))
            {
                throw new InvalidDataException(
                    $"El manifest no declara {required}.");
            }
        }

        foreach (var relativePath in manifestFiles)
        {
            EnsureSafeManifestPath(
                relativePath);

            var fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        stagedApplicationDirectory,
                        relativePath));

            if (!File.Exists(fullPath))
            {
                throw new InvalidDataException(
                    $"El manifest declara un archivo inexistente: {relativePath}");
            }
        }

        var actualFiles =
            Directory
                .EnumerateFiles(
                    stagedApplicationDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path =>
                    NormalizeManifestPath(
                        Path.GetRelativePath(
                            stagedApplicationDirectory,
                            path)))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        if (!actualFiles.SetEquals(
                manifestFiles))
        {
            throw new InvalidDataException(
                "El contenido del paquete no coincide con su manifest.");
        }
    }

    private static void EnsureSafeManifestPath(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "El manifest contiene una ruta no válida.");
        }

        var normalized =
            NormalizeManifestPath(
                relativePath);

        if (normalized.Equals(
                "..",
                StringComparison.Ordinal) ||
            normalized.StartsWith(
                "../",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "El manifest contiene una ruta fuera de la aplicación.");
        }

        var firstSegment =
            normalized.Split(
                '/',
                2,
                StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ??
            string.Empty;

        if (firstSegment.Equals(
                "config",
                StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals(
                "logs",
                StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals(
                "history",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "El paquete intenta administrar datos portables del usuario.");
        }
    }

    private static string NormalizeManifestPath(
        string path) =>
        path.Replace(
                Path.DirectorySeparatorChar,
                '/')
            .Replace(
                Path.AltDirectorySeparatorChar,
                '/')
            .TrimStart('/');

    private static bool TryParseStableVersion(
        string value,
        out Version version)
    {
        version =
            new Version(
                0,
                0,
                0,
                0);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw =
            value.Trim();

        if (raw.StartsWith(
                'v') ||
            raw.StartsWith(
                'V'))
        {
            raw =
                raw[1..];
        }

        if (raw.Contains(
                '-',
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!Version.TryParse(
                raw,
                out var parsed) ||
            parsed.Major < 0 ||
            parsed.Minor < 0)
        {
            return false;
        }

        version =
            new Version(
                parsed.Major,
                parsed.Minor,
                parsed.Build < 0
                    ? 0
                    : parsed.Build,
                parsed.Revision < 0
                    ? 0
                    : parsed.Revision);

        return true;
    }

    private static string GetFailedUpdateTag()
    {
        var statePath =
            PortablePaths.UpdateStateFile;

        if (!File.Exists(statePath))
        {
            return string.Empty;
        }

        try
        {
            var state =
                JsonSerializer.Deserialize<FailedUpdateState>(
                    File.ReadAllText(
                        statePath,
                        Encoding.UTF8),
                    JsonOptions);

            return state?.FailedUpdateTag ??
                   string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ClearFailedUpdateStateIfObsolete()
    {
        var failedTag =
            GetFailedUpdateTag();

        if (string.IsNullOrWhiteSpace(
                failedTag) ||
            !TryParseStableVersion(
                failedTag,
                out var failedVersion) ||
            !TryParseStableVersion(
                AppVersionInfo.Tag,
                out var currentVersion))
        {
            return;
        }

        if (currentVersion.CompareTo(
                failedVersion) < 0)
        {
            return;
        }

        try
        {
            if (File.Exists(
                    PortablePaths.UpdateStateFile))
            {
                File.Delete(
                    PortablePaths.UpdateStateFile);
            }
        }
        catch
        {
        }
    }

    private static string? GetArgumentValue(
        IReadOnlyList<string> arguments,
        string name)
    {
        for (var index = 0;
             index < arguments.Count - 1;
             index++)
        {
            if (arguments[index].Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static bool IsSafeUpdateWorkspacePath(
        string path)
    {
        try
        {
            var fullPath =
                Path.GetFullPath(path);

            var tempBase =
                Path.GetFullPath(
                    Path.Combine(
                        Path.GetTempPath(),
                        "BandaNV"))
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    tempBase,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative =
                Path.GetRelativePath(
                    tempBase,
                    fullPath);

            var firstSegment =
                relative.Split(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            return !string.IsNullOrWhiteSpace(
                       firstSegment) &&
                   firstSegment.StartsWith(
                       "update-",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteWorkspace(
        string workspaceRoot)
    {
        try
        {
            if (!IsSafeUpdateWorkspacePath(
                    workspaceRoot))
            {
                return;
            }

            if (Directory.Exists(
                    workspaceRoot))
            {
                Directory.Delete(
                    workspaceRoot,
                    recursive: true);
            }
        }
        catch
        {
        }
    }

    private static UpdateCheckResult Error(
        string message) =>
        new(
            UpdateCheckStatus.Error,
            AppVersionInfo.Tag,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            AppVersionInfo.GitHubReleasesUrl,
            string.Empty,
            string.Empty,
            string.Empty,
            message);

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }

    private sealed class FailedUpdateState
    {
        public string FailedUpdateTag { get; set; } = string.Empty;
    }

    private sealed class UpdateConfirmation
    {
        public string Token { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime ConfirmedAt { get; set; }
        public int ProcessId { get; set; }
    }
}
