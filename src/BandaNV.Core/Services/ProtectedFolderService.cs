using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

/// <summary>
/// Protección explícita configurada por el usuario. Bloquea la modificación
/// de una carpeta protegida, sus descendientes y cualquier carpeta superior
/// cuya modificación también afectaría al contenido protegido.
/// </summary>
public static class ProtectedFolderService
{
    public static List<string> NormalizePaths(IEnumerable<string>? paths)
    {
        var result = new List<string>();

        foreach (var raw in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = NormalizePath(raw);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (result.Any(folder =>
                    IsSameOrInside(normalized, folder)))
            {
                continue;
            }

            result.RemoveAll(folder =>
                IsSameOrInside(folder, normalized));

            result.Add(normalized);
        }

        return result;
    }

    public static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(path.Trim()));

    public static bool IsProtected(AppSettings settings, string path) =>
        TryGetProtectedFolder(settings, path, out _);

    public static bool TryGetProtectedFolder(
        AppSettings settings,
        string path,
        out string? protectedFolder)
    {
        protectedFolder = null;

        if (settings.ProtectedFolders is not { Count: > 0 })
        {
            return false;
        }

        var normalized = NormalizePath(path);

        foreach (var raw in settings.ProtectedFolders.ToArray())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string folder;
            try
            {
                folder = NormalizePath(raw);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsSameOrInside(normalized, folder) &&
                !IsSameOrInside(folder, normalized))
            {
                continue;
            }

            protectedFolder = folder;
            return true;
        }

        return false;
    }

    public static void EnsureAllowed(AppSettings settings, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (TryGetProtectedFolder(settings, path, out var protectedFolder))
            {
                throw new InvalidOperationException(
                    $"Operación bloqueada: la carpeta protegida \"{protectedFolder}\" " +
                    "o su contenido podrían verse modificados. Quitá la protección desde Configuración para permitir esta acción.");
            }
        }
    }

    private static bool IsSameOrInside(string path, string directory)
    {
        if (path.Equals(directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
            path.Length <= directory.Length)
        {
            return false;
        }

        return Path.EndsInDirectorySeparator(directory) ||
               path[directory.Length] is '/' or '\\';
    }
}
