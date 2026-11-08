using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BandaNV.App.Controls;

/// <summary>
/// Muestra el ícono/miniatura que devuelve Windows para el archivo.
/// Se carga al entrar en pantalla y se invalida si el ListView recicla la fila.
/// </summary>
public sealed class ExplorerItemIcon : UserControl
{
    private static readonly Dictionary<string, BitmapImage> ArchiveIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Image _image;
    private readonly FontIcon _fallback;
    private bool _isVisible;
    private int _loadVersion;

    public static readonly DependencyProperty IsLargePreviewProperty =
        DependencyProperty.Register(
            nameof(IsLargePreview), typeof(bool), typeof(ExplorerItemIcon),
            new PropertyMetadata(false, OnIconPropertyChanged));

    /// <summary>
    /// Solicita a Windows un ícono/miniatura de resolución alta en Detalles.
    /// Las filas mantienen la versión liviana de 40 px.
    /// </summary>
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
        };
    }

    private static void OnIconPropertyChanged(
        DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ExplorerItemIcon icon && icon._isVisible)
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

        if (!IsDirectory && !string.IsNullOrWhiteSpace(FilePath))
        {
            _ = LoadThumbnailAsync(FilePath, version, IsLargePreview);
        }
    }

    private async Task LoadThumbnailAsync(string path, int version, bool largePreview)
    {
        // La cache distingue la miniatura pequeña del ícono grande, de modo
        // que nunca se amplíe accidentalmente una imagen de solo 40 píxeles.
        var extension = Path.GetExtension(path);
        var cacheKey = extension + (largePreview ? "|large" : "|list");
        var isArchive = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);

        if (isArchive && ArchiveIcons.TryGetValue(cacheKey, out var cached))
        {
            ShowImage(cached, path, version);
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            // El panel derecho pide 256 px en modo SingleItem. La versión
            // anterior ampliaba un icono de 40 px y por eso se veía borroso.
            using var thumbnail = await file.GetThumbnailAsync(
                largePreview ? ThumbnailMode.SingleItem : ThumbnailMode.ListView,
                largePreview ? 256u : 40u,
                ThumbnailOptions.UseCurrentScale);

            if (thumbnail is null || !_isVisible || version != _loadVersion)
            {
                return;
            }

            var source = new BitmapImage();
            await source.SetSourceAsync(thumbnail);

            if (isArchive && !ArchiveIcons.ContainsKey(cacheKey))
            {
                ArchiveIcons[cacheKey] = source;
            }

            ShowImage(source, path, version);
        }
        catch (Exception)
        {
            // Archivos protegidos, desconectados o sin miniatura: ícono genérico.
        }
    }

    private void ShowImage(BitmapImage source, string path, int version)
    {
        if (!_isVisible || version != _loadVersion ||
            !string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _image.Source = source;
        _image.Visibility = Visibility.Visible;
        _fallback.Visibility = Visibility.Collapsed;
    }
}
