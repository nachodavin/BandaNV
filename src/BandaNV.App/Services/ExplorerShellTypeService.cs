using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BandaNV.Core.Models;

namespace BandaNV.App.Services;

/// <summary>
/// Obtiene el nombre del tipo que Windows muestra en la columna «Tipo»
/// a partir de las asociaciones de archivos instaladas en el sistema.
/// La consulta se hace por extensión, no requiere abrir cada archivo.
/// </summary>
internal static class ExplorerShellTypeService
{
    private const uint ShgfiTypeName = 0x00000400;
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly ConcurrentDictionary<string, string> TypeNames =
        new(StringComparer.OrdinalIgnoreCase);

    internal static string GetDisplayType(ExplorerItem item)
    {
        if (item.IsDrive)
        {
            return "Unidad";
        }

        if (item.IsDirectory)
        {
            return "Carpeta";
        }

        var extension = Path.GetExtension(item.Name);
        if (string.IsNullOrEmpty(extension))
        {
            return "Archivo";
        }

        return TypeNames.GetOrAdd(extension, ResolveWindowsType);
    }

    private static string ResolveWindowsType(string extension)
    {
        var fallback = extension.TrimStart('.').ToUpperInvariant();

        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            // USEFILEATTRIBUTES permite consultar la asociación de Windows
            // sin acceder al archivo real ni necesitar permisos adicionales.
            var size = (uint)Marshal.SizeOf<ShellFileInfo>();
            var result = SHGetFileInfo(
                "archivo" + extension,
                FileAttributeNormal,
                out var info,
                size,
                ShgfiTypeName | ShgfiUseFileAttributes);

            if (result != 0 && !string.IsNullOrWhiteSpace(info.TypeName))
            {
                return info.TypeName.Trim();
            }
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or EntryPointNotFoundException or
                TypeLoadException or MarshalDirectiveException)
        {
            // En un sistema sin shell disponible conservamos la extensión.
        }

        return fallback;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SHGetFileInfo(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        uint fileAttributes,
        out ShellFileInfo shellFileInfo,
        uint fileInfoSize,
        uint flags);
}
