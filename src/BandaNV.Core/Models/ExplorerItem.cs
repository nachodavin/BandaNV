using System.Globalization;

namespace BandaNV.Core.Models;

/// <summary>
/// Elemento del explorador de solo lectura. Puede representar una unidad,
/// una carpeta o un archivo. Las operaciones de modificación serán otro servicio.
/// </summary>
public sealed record ExplorerItem(
    string FullPath,
    string Name,
    bool IsDirectory,
    long? SizeBytes,
    DateTime? ModifiedAt,
    string TypeName,
    bool IsDrive = false,
    bool IsReparsePoint = false)
{
    // Propiedades de presentación: permiten cambiar Ver sin tocar el archivo.
    public bool ShowFileExtension { get; init; } = true;
    public bool CompactMode { get; init; }

    public string DisplayName =>
        ShowFileExtension || IsDirectory
            ? Name
            : Path.GetFileNameWithoutExtension(Name);

    public double RowHeight => CompactMode ? 30 : 41;

    public string IconGlyph =>
        IsDrive ? "\uE7F1" : IsDirectory ? "\uE8B7" : "\uE8A5";

    public string SizeText
    {
        get
        {
            if (IsDirectory || !SizeBytes.HasValue)
            {
                return "—";
            }

            var bytes = Math.Max(0L, SizeBytes.Value);
            if (bytes == 0)
            {
                return "0 KB";
            }

            string[] units = ["KB", "MB", "GB", "TB"];
            var value = bytes / 1024d;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture) +
                   " " + units[unit];
        }
    }

    public string ModifiedText =>
        ModifiedAt.HasValue && ModifiedAt.Value > DateTime.MinValue
            ? ModifiedAt.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
            : "—";
}

/// <summary>
/// Enumeración parcial a demanda; no se recorren subcarpetas recursivamente.
/// </summary>
public sealed record ExplorerListing(
    IReadOnlyList<ExplorerItem> Items,
    int SkippedCount = 0,
    bool Truncated = false);
