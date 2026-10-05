using Microsoft.Win32;

namespace BandaNV.App.Services;

public static class WindowsStartupService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName =
        "BandaNV";

    public static void Apply(
        bool enabled)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true)
            ?? throw new InvalidOperationException(
                "Windows no permitió abrir la configuración de inicio automático.");

        if (!enabled)
        {
            key.DeleteValue(
                ValueName,
                throwOnMissingValue: false);

            return;
        }

        var executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executablePath) ||
            !File.Exists(
                executablePath))
        {
            throw new InvalidOperationException(
                "No se pudo determinar la ruta de BandaNV para iniciar con Windows.");
        }

        key.SetValue(
            ValueName,
            $"\"{Path.GetFullPath(executablePath)}\"",
            RegistryValueKind.String);
    }
}
