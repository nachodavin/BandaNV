using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BandaNV.App.Controls;

/// <summary>
/// Ícono nativo de un archivo. Las filas del ListView se reciclan, por eso
/// cada instancia valida la versión del archivo antes de mostrar el resultado.
/// Los íconos compartidos (ZIP/RAR/7z) se cargan una sola vez por extensión y
/// resolución, incluso si muchas filas aparecen al mismo tiempo.
/// </summary>
public sealed class ExplorerItemIcon : UserControl
{
    private static readonly HashSet<string> SharedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };

    // Todas las operaciones de iconos parten del hilo UI de WinUI. Las tareas
    // pendientes se conservan aunque la fila que inició la carga se recicle.
    private static readonly Dictionary<string, Task<BitmapImage?>> PendingIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, BitmapImage> CachedIcons =
        new(StringComparer.OrdinalIgnoreCase);

    // Evita una avalancha de solicitudes al Shell al abrir carpetas grandes.
    private static readonly SemaphoreSlim ThumbnailSlots = new(6, 6);

    private readonly Image _image;
    private readonly FontIcon _fallback;
    private bool _isVisible;
    private int _loadVersion;

    public static readonly DependencyProperty IsLargePreviewProperty =
        DependencyProperty.Register(
            nameof(IsLargePreview), typeof(bool), typeof(ExplorerItemIcon),
            new PropertyMetadata(false, OnIconPropertyChanged));

    public bool IsLargePreview
    {
        get => (bool)GetValue(IsLargePreviewProperty);
        set => SetValue(IsLargePreviewProperty, value);
    }

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(
            nameof(FilePath), typeof(string), typeof(ExplorerItemIcon),
            new PropertyMetadata(string.Empty, OnIconPropertyChanged));

    public static readonly DependencyProperty IsDirectoryProperty =
        DependencyProperty.Register(
            nameof(IsDirectory), typeof(bool), typeof(ExplorerItemIcon),
            new PropertyMetadata(false, OnIconPropertyChanged));

    public static readonly DependencyProperty IsDriveProperty =
        DependencyProperty.Register(
            nameof(IsDrive), typeof(bool), typeof(ExplorerItemIcon),
            new PropertyMetadata(false, OnIconPropertyChanged));

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public bool IsDirectory
    {
        get => (bool)GetValue(IsDirectoryProperty);
        set => SetValue(IsDirectoryProperty, value);
    }

    public bool IsDrive
    {
        get => (bool)GetValue(IsDriveProperty);
        set => SetValue(IsDriveProperty, value);
    }

    public ExplorerItemIcon()
    {
        _fallback = new FontIcon
        {
            Glyph = "\uE8A5",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var layout = new Grid();
        layout.Children.Add(_fallback);
        layout.Children.Add(_image);
        Content = layout;

        Loaded += (_, _) =>
        {
            _isVisible = true;
            Refresh();
        };

        Unloaded += (_, _) =>
        {
            _isVisible = false;
            ++_loadVersion;
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
        };
    }

    /// <summary>
    /// Da inicio a la carga de un único ícono pequeño por extensión compartida.
    /// Se llama al abrir una carpeta para que la primera pantalla no cree
    /// decenas de solicitudes simultáneas de ZIP o RAR.
    /// </summary>
    public static void WarmUpSharedIcons(IEnumerable<string> filePaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            if (SharedExtensions.Contains(extension) && seen.Add(extension))
            {
                _ = GetSharedIconAsync(path, extension, largePreview: false);
            }
        }
    }

    private static void OnIconPropertyChanged(
        DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ExplorerItemIcon { _isVisible: true } icon)
        {
            icon.Refresh();
        }
    }

    private void Refresh()
    {
        var version = ++_loadVersion;
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;

        _fallback.Glyph = IsDrive
            ? "\uE7F1"
            : IsDirectory ? "\uE8B7" : "\uE8A5";

        _fallback.Foreground = IsDirectory && !IsDrive
            ? new SolidColorBrush(Microsoft.UI.Colors.Goldenrod)
            : (Brush)Application.Current.Resources["BandaMutedStrongBrush"];

        if (_isVisible && !IsDirectory && !IsDrive &&
            !string.IsNullOrWhiteSpace(FilePath))
        {
            _ = LoadIconAsync(FilePath, version, IsLargePreview);
        }
    }

    private async Task LoadIconAsync(string path, int version, bool largePreview)
    {
        var extension = Path.GetExtension(path);
        BitmapImage? icon;

        if (SharedExtensions.Contains(extension))
        {
            icon = await GetSharedIconAsync(path, extension, largePreview);
        }
        else
        {
            icon = await CreateBitmapAsync(path, largePreview);
        }

        if (icon is not null && _isVisible && version == _loadVersion &&
            string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase) &&
            IsLargePreview == largePreview && !IsDirectory && !IsDrive)
        {
            _image.Source = icon;
            _image.Visibility = Visibility.Visible;
            _fallback.Visibility = Visibility.Collapsed;
        }
    }

    private static async Task<BitmapImage?> GetSharedIconAsync(
        string path, string extension, bool largePreview)
    {
        var key = extension + (largePreview ? "|large" : "|list");

        if (CachedIcons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!PendingIcons.TryGetValue(key, out var pending))
        {
            pending = CreateBitmapAsync(path, largePreview);
            PendingIcons[key] = pending;
        }

        var icon = await pending;

        if (icon is not null)
        {
            CachedIcons[key] = icon;
        }

        // Si la primera muestra no puede leerse, otra fila podrá reintentar
        // con otro archivo de la misma extensión. No cachear fallos.
        PendingIcons.Remove(key);
        return icon;
    }

    private static async Task<BitmapImage?> CreateBitmapAsync(
        string path, bool largePreview)
    {
        await ThumbnailSlots.WaitAsync();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(
                largePreview ? ThumbnailMode.SingleItem : ThumbnailMode.ListView,
                largePreview ? 256u : 40u,
                ThumbnailOptions.UseCurrentScale);

            if (thumbnail is null)
            {
                return null;
            }

            var source = new BitmapImage();
            await source.SetSourceAsync(thumbnail);
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"BandaNV Explorer - ícono {Path.GetExtension(path)}: {ex.GetType().Name}");
            return null;
        }
        finally
        {
            ThumbnailSlots.Release();
        }
    }
}
