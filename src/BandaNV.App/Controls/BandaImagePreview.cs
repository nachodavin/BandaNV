using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Graphics.Imaging;
using System.Diagnostics;

namespace BandaNV.App.Controls;

/// <summary>
/// Vista previa integrada en los paneles de detalle de Buscar y Organizar.
/// Nunca modifica el archivo original.
/// </summary>
public sealed class BandaImagePreview : UserControl
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

    private static readonly TimeSpan ImageLoadTimeout = TimeSpan.FromSeconds(12);

    private readonly Border _frame;
    private readonly Image _image;
    private readonly TextBlock _status;
    private FrameworkElement? _detailLayout;
    private string? _currentPath;
    private int _loadVersion;

    public BandaImagePreview()
    {
        var resources = Application.Current.Resources;
        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };

        _status = new TextBlock
        {
            Text = "Cargando vista previa...",
            Foreground = (Brush)resources["BandaMutedStrongBrush"],
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new Grid();
        content.Children.Add(_image);
        content.Children.Add(_status);

        _frame = new Border
        {
            Height = 380,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)resources["BandaNavIconBrush"],
            BorderBrush = (Brush)resources["BandaBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content
        };

        Content = _frame;
        Margin = new Thickness(0, 0, 0, 12);
        Visibility = Visibility.Collapsed;

        Loaded += BandaImagePreview_Loaded;
        Unloaded += BandaImagePreview_Unloaded;
    }

    private void BandaImagePreview_Loaded(object sender, RoutedEventArgs e)
    {
        // En ambas páginas el control está dentro del StackPanel superior
        // del Grid de detalle. Su altura permite aprovechar el espacio libre.
        var layout = (Parent as FrameworkElement)?.Parent as FrameworkElement;
        if (ReferenceEquals(layout, _detailLayout))
        {
            return;
        }

        if (_detailLayout is not null)
        {
            _detailLayout.SizeChanged -= DetailLayout_SizeChanged;
        }

        _detailLayout = layout;

        if (_detailLayout is not null)
        {
            _detailLayout.SizeChanged += DetailLayout_SizeChanged;
            ResizePreview(_detailLayout.ActualHeight);
        }
    }

    private void BandaImagePreview_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_detailLayout is not null)
        {
            _detailLayout.SizeChanged -= DetailLayout_SizeChanged;
            _detailLayout = null;
        }

        Clear();
    }

    private void DetailLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizePreview(e.NewSize.Height);
    }

    private void ResizePreview(double panelHeight)
    {
        if (double.IsNaN(panelHeight) || double.IsInfinity(panelHeight) ||
            panelHeight <= 0)
        {
            return;
        }

        // Reservar espacio para nombre, categoría, tamaño y metadatos.
        // En pantallas grandes crece; en las chicas no se impone un alto fijo.
        _frame.Height = Math.Clamp(panelHeight - 320, 220, 510);
    }

    public void Show(string? filePath, bool isDirectory)
    {
        if (isDirectory ||
            string.IsNullOrWhiteSpace(filePath) ||
            !ImageExtensions.Contains(Path.GetExtension(filePath)))
        {
            Clear();
            return;
        }

        if (string.Equals(_currentPath, filePath, StringComparison.OrdinalIgnoreCase) &&
            Visibility == Visibility.Visible)
        {
            return;
        }

        _currentPath = filePath;
        var version = ++_loadVersion;
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _status.Text = "Cargando vista previa...";
        _status.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        _ = LoadImageAsync(filePath, version);
    }

    public void Clear()
    {
        ++_loadVersion;
        _currentPath = null;
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _status.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    private async Task LoadImageAsync(string filePath, int version)
    {
        // Cada selección obtiene su propia decodificación, sin reutilizar
        // BitmapImage ni modificar el origen de la imagen anterior.
        var loadTask = DecodeImageAsync(filePath, version);
        using var timeoutCancellation = new CancellationTokenSource();

        try
        {
            var timeoutTask = Task.Delay(ImageLoadTimeout, timeoutCancellation.Token);
            if (await Task.WhenAny(loadTask, timeoutTask) != loadTask)
            {
                _ = ObserveLateCompletionAsync(loadTask);
                ShowLoadError(version, "La vista previa tardó demasiado en cargar.");
                return;
            }

            timeoutCancellation.Cancel();
            var source = await loadTask;

            // Una carga vieja nunca puede alterar la selección actual.
            if (version != _loadVersion || source is null)
            {
                return;
            }

            _image.Source = source;
            _image.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV - vista previa de imagen ({filePath}): {ex}");
            ShowLoadError(version, "No se pudo mostrar la vista previa.");
        }
    }

    private async Task<SoftwareBitmapSource?> DecodeImageAsync(string filePath, int version)
    {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        if (version != _loadVersion)
        {
            return null;
        }

        // Decodificar a 1600 px máximo en el lado mayor, conservando
        // proporciones y la orientación EXIF de fotografías.
        var width = Math.Max(1u, decoder.PixelWidth);
        var height = Math.Max(1u, decoder.PixelHeight);
        var scale = Math.Min(1d, 1600d / Math.Max(width, height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1d, Math.Round(width * scale)),
            ScaledHeight = (uint)Math.Max(1d, Math.Round(height * scale))
        };

        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        if (version != _loadVersion)
        {
            return null;
        }

        // SoftwareBitmapSource evita la cadena BitmapImage.SetSourceAsync
        // que podía quedar pendiente al cambiar de archivo.
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(softwareBitmap);
        return version == _loadVersion ? source : null;
    }

    private void ShowLoadError(int version, string message)
    {
        if (version != _loadVersion)
        {
            return;
        }

        ++_loadVersion;
        _currentPath = null;
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _status.Text = message;
        _status.Visibility = Visibility.Visible;
    }

    private static async Task ObserveLateCompletionAsync(Task task)
    {
        // Las cargas vencidas todavía pueden completar: observar sus fallos
        // sin actualizar la selección ni provocar excepciones no observadas.
        try
        {
            await task;
        }
        catch
        {
            // El estado visible ya fue informado por ShowLoadError.
        }
    }
}
