using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BandaNV.Updater;

internal static class Program
{
    private const string ManifestFormat =
        "BandaNV.UpdateManifest.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] ProtectedDirectoryNames =
    [
        "config",
        "logs",
        "history"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryReadArguments(
                args,
                out var options))
        {
            ShowInfo(
                "NVupdate.exe es el actualizador auxiliar de BandaNV y se ejecuta automáticamente cuando instalás una actualización desde la aplicación.");
            return 0;
        }

        var backupCreated = false;

        try
        {
            ValidateOptions(options);

            if (!WaitForProcessExit(
                    options.ParentProcessId,
                    TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException(
                    "BandaNV no terminó de cerrarse a tiempo. No se modificó la instalación.");
            }

            var manifest =
                ReadManifest(
                    options.StagedDirectory);

            if (!manifest.Version.Equals(
                    options.ExpectedVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "La versión del manifest no coincide con la actualización esperada.");
            }

            BackupCurrentInstallation(
                options.AppDirectory,
                options.BackupDirectory);

            backupCreated = true;

            InstallStagedApplication(
                options.AppDirectory,
                options.StagedDirectory,
                manifest);

            var process =
                StartUpdatedApplication(
                    options);

            if (!WaitForConfirmation(
                    process,
                    options,
                    TimeSpan.FromSeconds(25)))
            {
                TryStopProcess(
                    process);

                throw new InvalidOperationException(
                    "La nueva versión no confirmó un inicio correcto dentro del tiempo esperado.");
            }

            TryDeleteWorkspace(
                options.WorkspaceRoot);

            return 0;
        }
        catch (Exception ex)
        {
            return RollbackAfterFailure(
                options,
                ex.Message,
                backupCreated);
        }
    }

    private static bool TryReadArguments(
        IReadOnlyList<string> args,
        out UpdateOptions options)
    {
        options =
            new UpdateOptions();

        var values =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        for (var index = 0;
             index < args.Count - 1;
             index += 2)
        {
            if (!args[index].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                return false;
            }

            values[args[index]] =
                args[index + 1];
        }

        if (!values.TryGetValue(
                "--parent-pid",
                out var rawPid) ||
            !int.TryParse(
                rawPid,
                out var parentPid) ||
            !values.TryGetValue(
                "--app-dir",
                out var appDirectory) ||
            !values.TryGetValue(
                "--staged-dir",
                out var stagedDirectory) ||
            !values.TryGetValue(
                "--backup-dir",
                out var backupDirectory) ||
            !values.TryGetValue(
                "--confirm-path",
                out var confirmationPath) ||
            !values.TryGetValue(
                "--token",
                out var token) ||
            !values.TryGetValue(
                "--expected-version",
                out var expectedVersion) ||
            !values.TryGetValue(
                "--release-tag",
                out var releaseTag))
        {
            return false;
        }

        options =
            new UpdateOptions
            {
                ParentProcessId =
                    parentPid,
                AppDirectory =
                    appDirectory,
                StagedDirectory =
                    stagedDirectory,
                BackupDirectory =
                    backupDirectory,
                ConfirmationPath =
                    confirmationPath,
                Token =
                    token,
                ExpectedVersion =
                    expectedVersion,
                ReleaseTag =
                    releaseTag,
                WorkspaceRoot =
                    Path.GetDirectoryName(
                        Path.GetFullPath(
                            stagedDirectory)) is { } extractedDirectory
                        ? Path.GetDirectoryName(
                              extractedDirectory) ??
                          string.Empty
                        : string.Empty
            };

        return true;
    }

    private static void ValidateOptions(
        UpdateOptions options)
    {
        if (options.ParentProcessId <= 0)
        {
            throw new InvalidOperationException(
                "El proceso principal indicado no es válido.");
        }

        options.AppDirectory =
            Path.GetFullPath(
                options.AppDirectory);

        options.StagedDirectory =
            Path.GetFullPath(
                options.StagedDirectory);

        options.BackupDirectory =
            Path.GetFullPath(
                options.BackupDirectory);

        options.ConfirmationPath =
            Path.GetFullPath(
                options.ConfirmationPath);

        options.WorkspaceRoot =
            Path.GetFullPath(
                options.WorkspaceRoot);

        var tempBase =
            Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "BandaNV"))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        var workspaceWithSeparator =
            options.WorkspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!workspaceWithSeparator.StartsWith(
                tempBase,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La carpeta temporal de actualización no es válida.");
        }

        var workspaceLeaf =
            Path.GetFileName(
                options.WorkspaceRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

        if (!workspaceLeaf.StartsWith(
                "update-",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El workspace de actualización no tiene un nombre válido.");
        }

        EnsurePathInside(
            options.StagedDirectory,
            options.WorkspaceRoot);

        EnsurePathInside(
            options.BackupDirectory,
            options.WorkspaceRoot);

        EnsurePathInside(
            options.ConfirmationPath,
            options.WorkspaceRoot);

        if (!Directory.Exists(
                options.AppDirectory))
        {
            throw new DirectoryNotFoundException(
                "No se encontró la instalación actual de BandaNV.");
        }

        if (!File.Exists(
                Path.Combine(
                    options.AppDirectory,
                    "BandaNV.exe")))
        {
            throw new FileNotFoundException(
                "La carpeta indicada no contiene BandaNV.exe.");
        }

        if (!Directory.Exists(
                options.StagedDirectory))
        {
            throw new DirectoryNotFoundException(
                "No se encontró la nueva versión preparada.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Token))
        {
            throw new InvalidOperationException(
                "Falta el token de confirmación de la actualización.");
        }
    }

    private static UpdateManifest ReadManifest(
        string directory)
    {
        var path =
            Path.Combine(
                directory,
                "bandanv_update_manifest.json");

        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                "No se encontró el manifest del paquete.");
        }

        UpdateManifest? manifest;

        try
        {
            manifest =
                JsonSerializer.Deserialize<UpdateManifest>(
                    File.ReadAllText(
                        path,
                        Encoding.UTF8),
                    JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "No se pudo leer el manifest de actualización.",
                ex);
        }

        if (manifest is null ||
            !manifest.Format.Equals(
                ManifestFormat,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "El manifest de actualización no es compatible.");
        }

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException(
                "El manifest de actualización está vacío.");
        }

        return manifest;
    }

    private static void BackupCurrentInstallation(
        string appDirectory,
        string backupDirectory)
    {
        if (Directory.Exists(
                backupDirectory))
        {
            Directory.Delete(
                backupDirectory,
                recursive: true);
        }

        Directory.CreateDirectory(
            backupDirectory);

        foreach (var file in
                 Directory.EnumerateFiles(
                     appDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative =
                Path.GetRelativePath(
                    appDirectory,
                    file);

            if (IsProtectedUserPath(
                    relative))
            {
                continue;
            }

            var destination =
                Path.Combine(
                    backupDirectory,
                    relative);

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destination)!);

            File.Copy(
                file,
                destination,
                overwrite: true);
        }
    }

    private static void InstallStagedApplication(
        string appDirectory,
        string stagedDirectory,
        UpdateManifest newManifest)
    {
        var currentManifest =
            TryReadManifest(
                appDirectory);

        if (currentManifest is not null)
        {
            var newFiles =
                new HashSet<string>(
                    newManifest.Files.Select(
                        NormalizeRelativePath),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var oldFile in
                     currentManifest.Files)
            {
                var normalized =
                    NormalizeRelativePath(
                        oldFile);

                if (newFiles.Contains(
                        normalized) ||
                    IsProtectedUserPath(
                        normalized))
                {
                    continue;
                }

                var currentPath =
                    Path.GetFullPath(
                        Path.Combine(
                            appDirectory,
                            normalized));

                EnsurePathInside(
                    currentPath,
                    appDirectory);

                if (File.Exists(
                        currentPath))
                {
                    File.SetAttributes(
                        currentPath,
                        FileAttributes.Normal);

                    File.Delete(
                        currentPath);
                }
            }
        }

        foreach (var relative in
                 newManifest.Files)
        {
            var normalized =
                NormalizeRelativePath(
                    relative);

            if (IsProtectedUserPath(
                    normalized))
            {
                throw new InvalidDataException(
                    "El paquete intenta modificar datos portables del usuario.");
            }

            var source =
                Path.GetFullPath(
                    Path.Combine(
                        stagedDirectory,
                        normalized));

            var destination =
                Path.GetFullPath(
                    Path.Combine(
                        appDirectory,
                        normalized));

            EnsurePathInside(
                source,
                stagedDirectory);

            EnsurePathInside(
                destination,
                appDirectory);

            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"Falta un archivo declarado por el paquete: {normalized}",
                    source);
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destination)!);

            if (File.Exists(
                    destination))
            {
                File.SetAttributes(
                    destination,
                    FileAttributes.Normal);
            }

            File.Copy(
                source,
                destination,
                overwrite: true);
        }

        RemoveEmptyApplicationDirectories(
            appDirectory);
    }

    private static Process StartUpdatedApplication(
        UpdateOptions options)
    {
        var executable =
            Path.Combine(
                options.AppDirectory,
                "BandaNV.exe");

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "La actualización no dejó BandaNV.exe disponible.",
                executable);
        }

        try
        {
            if (File.Exists(
                    options.ConfirmationPath))
            {
                File.Delete(
                    options.ConfirmationPath);
            }
        }
        catch
        {
        }

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    executable,
                WorkingDirectory =
                    options.AppDirectory,
                UseShellExecute = false
            };

        startInfo.ArgumentList.Add(
            "--update-confirm-path");
        startInfo.ArgumentList.Add(
            options.ConfirmationPath);

        startInfo.ArgumentList.Add(
            "--update-token");
        startInfo.ArgumentList.Add(
            options.Token);

        return Process.Start(
                   startInfo) ??
               throw new InvalidOperationException(
                   "No se pudo iniciar la nueva versión de BandaNV.");
    }

    private static bool WaitForConfirmation(
        Process process,
        UpdateOptions options,
        TimeSpan timeout)
    {
        var deadline =
            DateTime.UtcNow +
            timeout;

        while (DateTime.UtcNow <
               deadline)
        {
            if (IsValidConfirmation(
                    options.ConfirmationPath,
                    options.Token,
                    options.ExpectedVersion))
            {
                return true;
            }

            try
            {
                process.Refresh();

                if (process.HasExited)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            Thread.Sleep(
                250);
        }

        return false;
    }

    private static bool IsValidConfirmation(
        string path,
        string token,
        string expectedVersion)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var confirmation =
                JsonSerializer.Deserialize<UpdateConfirmation>(
                    File.ReadAllText(
                        path,
                        Encoding.UTF8),
                    JsonOptions);

            if (confirmation is null ||
                !confirmation.Token.Equals(
                    token,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(
                       expectedVersion) ||
                   confirmation.Version.Equals(
                       expectedVersion,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int RollbackAfterFailure(
        UpdateOptions options,
        string failure,
        bool backupCreated)
    {
        if (!backupCreated)
        {
            WriteFailedUpdateState(
                options.AppDirectory,
                options.ReleaseTag,
                failure);

            TryStartCurrentApplication(
                options.AppDirectory);

            TryDeleteWorkspace(
                options.WorkspaceRoot);

            ShowError(
                "La actualización no pudo iniciarse." +
                Environment.NewLine +
                Environment.NewLine +
                "No se modificó BandaNV." +
                Environment.NewLine +
                Environment.NewLine +
                $"Detalle: {failure}");

            return 1;
        }

        try
        {

            var stagedManifest =
                TryReadManifest(
                    options.StagedDirectory);

            if (stagedManifest is not null)
            {
                foreach (var relative in
                         stagedManifest.Files)
                {
                    var normalized =
                        NormalizeRelativePath(
                            relative);

                    if (IsProtectedUserPath(
                            normalized))
                    {
                        continue;
                    }

                    var path =
                        Path.GetFullPath(
                            Path.Combine(
                                options.AppDirectory,
                                normalized));

                    EnsurePathInside(
                        path,
                        options.AppDirectory);

                    try
                    {
                        if (File.Exists(path))
                        {
                            File.SetAttributes(
                                path,
                                FileAttributes.Normal);

                            File.Delete(
                                path);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            RestoreBackup(
                options.BackupDirectory,
                options.AppDirectory);

            WriteFailedUpdateState(
                options.AppDirectory,
                options.ReleaseTag,
                failure);

            TryStartCurrentApplication(
                options.AppDirectory);

            TryDeleteWorkspace(
                options.WorkspaceRoot);

            ShowError(
                "La actualización no pudo completarse y BandaNV restauró automáticamente la versión anterior." +
                Environment.NewLine +
                Environment.NewLine +
                $"Detalle: {failure}");

            return 2;
        }
        catch (Exception rollbackException)
        {
            ShowError(
                "La actualización falló y tampoco se pudo completar el rollback automático." +
                Environment.NewLine +
                Environment.NewLine +
                $"Backup: {options.BackupDirectory}" +
                Environment.NewLine +
                Environment.NewLine +
                $"Detalle: {rollbackException.Message}");

            return 3;
        }
    }

    private static void TryStartCurrentApplication(
        string appDirectory)
    {
        try
        {
            var executable =
                Path.Combine(
                    appDirectory,
                    "BandaNV.exe");

            if (!File.Exists(executable))
            {
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        executable,
                    WorkingDirectory =
                        appDirectory,
                    UseShellExecute = false
                });
        }
        catch
        {
        }
    }

    private static void RestoreBackup(
        string backupDirectory,
        string appDirectory)
    {
        foreach (var file in
                 Directory.EnumerateFiles(
                     backupDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative =
                Path.GetRelativePath(
                    backupDirectory,
                    file);

            var destination =
                Path.Combine(
                    appDirectory,
                    relative);

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destination)!);

            File.Copy(
                file,
                destination,
                overwrite: true);
        }
    }

    private static void WriteFailedUpdateState(
        string appDirectory,
        string releaseTag,
        string detail)
    {
        if (string.IsNullOrWhiteSpace(
                releaseTag))
        {
            return;
        }

        try
        {
            var configDirectory =
                Path.Combine(
                    appDirectory,
                    "config");

            Directory.CreateDirectory(
                configDirectory);

            var statePath =
                Path.Combine(
                    configDirectory,
                    "bandanv_update_state.json");

            var temporaryPath =
                statePath +
                $".tmp_{Guid.NewGuid():N}";

            var state =
                new FailedUpdateState
                {
                    FailedUpdateTag =
                        releaseTag,
                    FailedAt =
                        DateTime.Now,
                    Detail =
                        detail
                };

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    state,
                    JsonOptions),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temporaryPath,
                statePath,
                overwrite: true);
        }
        catch
        {
        }
    }

    private static UpdateManifest? TryReadManifest(
        string directory)
    {
        try
        {
            var path =
                Path.Combine(
                    directory,
                    "bandanv_update_manifest.json");

            if (!File.Exists(path))
            {
                return null;
            }

            var manifest =
                JsonSerializer.Deserialize<UpdateManifest>(
                    File.ReadAllText(
                        path,
                        Encoding.UTF8),
                    JsonOptions);

            return manifest is not null &&
                   manifest.Format.Equals(
                       ManifestFormat,
                       StringComparison.Ordinal)
                ? manifest
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool WaitForProcessExit(
        int processId,
        TimeSpan timeout)
    {
        try
        {
            using var process =
                Process.GetProcessById(
                    processId);

            return process.WaitForExit(
                (int)Math.Max(
                    1000,
                    timeout.TotalMilliseconds));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryStopProcess(
        Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Refresh();

            if (process.HasExited)
            {
                return;
            }

            try
            {
                process.CloseMainWindow();
            }
            catch
            {
            }

            if (!process.WaitForExit(
                    1500))
            {
                process.Kill(
                    entireProcessTree: true);

                process.WaitForExit(
                    3000);
            }
        }
        catch
        {
        }
    }

    private static void RemoveEmptyApplicationDirectories(
        string appDirectory)
    {
        try
        {
            var directories =
                Directory
                    .EnumerateDirectories(
                        appDirectory,
                        "*",
                        SearchOption.AllDirectories)
                    .OrderByDescending(path =>
                        path.Length)
                    .ToList();

            foreach (var directory in
                     directories)
            {
                var relative =
                    Path.GetRelativePath(
                        appDirectory,
                        directory);

                if (IsProtectedUserPath(
                        relative))
                {
                    continue;
                }

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(
                            directory)
                        .Any())
                    {
                        Directory.Delete(
                            directory);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static bool IsProtectedUserPath(
        string relativePath)
    {
        var normalized =
            NormalizeRelativePath(
                relativePath);

        var firstSegment =
            normalized.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ??
            string.Empty;

        return ProtectedDirectoryNames.Any(
            protectedName =>
                firstSegment.Equals(
                    protectedName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(
        string path) =>
        path.Replace(
                Path.DirectorySeparatorChar,
                '/')
            .Replace(
                Path.AltDirectorySeparatorChar,
                '/')
            .TrimStart('/');

    private static void EnsurePathInside(
        string path,
        string root)
    {
        var fullPath =
            Path.GetFullPath(
                path);

        var fullRoot =
            Path.GetFullPath(
                root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        if (fullPath.Equals(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var prefix =
            fullRoot +
            Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La actualización intentó acceder a una ruta fuera del área permitida.");
        }
    }

    private static void TryDeleteWorkspace(
        string workspaceRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    workspaceRoot) ||
                !Directory.Exists(
                    workspaceRoot))
            {
                return;
            }

            Directory.Delete(
                workspaceRoot,
                recursive: true);
        }
        catch
        {
        }
    }

    private static void ShowInfo(
        string message) =>
        MessageBox(
            IntPtr.Zero,
            message,
            "BandaNV — NVupdate",
            0x00000040);

    private static void ShowError(
        string message) =>
        MessageBox(
            IntPtr.Zero,
            message,
            "BandaNV — NVupdate",
            0x00000010);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr hWnd,
        string text,
        string caption,
        uint type);

    private sealed class UpdateOptions
    {
        public int ParentProcessId { get; set; }
        public string AppDirectory { get; set; } = string.Empty;
        public string StagedDirectory { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
        public string ConfirmationPath { get; set; } = string.Empty;
        public string WorkspaceRoot { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string ExpectedVersion { get; set; } = string.Empty;
        public string ReleaseTag { get; set; } = string.Empty;
    }

    private sealed class UpdateManifest
    {
        public string Format { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<string> Files { get; set; } = [];
    }

    private sealed class UpdateConfirmation
    {
        public string Token { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed class FailedUpdateState
    {
        public string FailedUpdateTag { get; set; } = string.Empty;
        public DateTime FailedAt { get; set; }
        public string Detail { get; set; } = string.Empty;
    }
}
