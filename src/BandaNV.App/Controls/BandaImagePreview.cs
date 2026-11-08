using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using System.Text;
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
    private static readonly TimeSpan DocumentLoadTimeout = TimeSpan.FromSeconds(20);
    private const int MaxTextCharacters = 150_000;

    private readonly Border _frame;
    private readonly Image _image;
    private readonly TextBlock _status;
    private readonly TextBlock _textContent;
    private readonly ScrollViewer _textViewer;
    private readonly StackPanel _pdfNavigation;
    private readonly TextBlock _pdfPageCounter;
    private readonly Button _pdfPreviousButton;
    private readonly Button _pdfNextButton;
    private PdfDocument? _pdfDocument;
    private uint _pdfPageCount;
    private uint _pdfPageIndex;
    private FrameworkElement? _detailLayout;
    private string? _currentPath;
    private int _loadVersion;

    // Solo Explorar utiliza una franja compacta de vista previa.
    // Buscar y Organizar mantienen la altura adaptable habitual.
    public bool UseCompactExplorerFrame { get; set; }

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

        _textContent = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = (Brush)resources["BandaTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        _textViewer = new ScrollViewer
        {
            Content = _textContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(4, 2, 4, 2)
        };

        _pdfPageCounter = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)resources["BandaMutedStrongBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        _pdfPreviousButton = new Button
        {
            Content = "Anterior",
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false
        };
        _pdfPreviousButton.Click += (_, _) => ChangePdfPage(-1);

        _pdfNextButton = new Button
        {
            Content = "Siguiente",
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false
        };
        _pdfNextButton.Click += (_, _) => ChangePdfPage(1);

        _pdfNavigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _pdfNavigation.Children.Add(_pdfPreviousButton);
        _pdfNavigation.Children.Add(_pdfPageCounter);
        _pdfNavigation.Children.Add(_pdfNextButton);

        var content = new Grid();
        content.Children.Add(_image);
        content.Children.Add(_textViewer);
        content.Children.Add(_status);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(content, 0);
        Grid.SetRow(_pdfNavigation, 1);
        layout.Children.Add(content);
        layout.Children.Add(_pdfNavigation);

        _frame = new Border
        {
            Height = 380,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)resources["BandaNavIconBrush"],
            BorderBrush = (Brush)resources["BandaBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = layout
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
        if (UseCompactExplorerFrame)
        {
            _frame.Height = 207;
            _frame.Padding = new Thickness(10);
            _frame.CornerRadius = new CornerRadius(0);
            _frame.BorderThickness = new Thickness(0);
            return;
        }

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
            string.IsNullOrWhiteSpace(filePath))
        {
            Clear();
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!ImageExtensions.Contains(extension) &&
            !extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
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
        ResetDisplayedContent();
        _status.Text = "Cargando vista previa...";
        _status.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;

        if (UseCompactExplorerFrame)
        {
            ResizePreview(207);
        }

        if (ImageExtensions.Contains(extension))
        {
            // Mantener intacto el decodificador de imágenes que ya funciona.
            _ = LoadImageAsync(filePath, version);
        }
        else if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            _ = RunDocumentLoadAsync(LoadSvgAsync(filePath, version), version);
        }
        else if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            _ = RunDocumentLoadAsync(LoadTextAsync(filePath, version), version);
        }
        else
        {
            _ = RunDocumentLoadAsync(LoadPdfAsync(filePath, version), version);
        }
    }

    public void Clear()
    {
        ++_loadVersion;
        _currentPath = null;
        ResetDisplayedContent();
        _status.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    private void ResetDisplayedContent()
    {
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _textContent.Text = string.Empty;
        _textViewer.Visibility = Visibility.Collapsed;
        _textViewer.ChangeView(null, 0, null, true);
        _pdfDocument = null;
        _pdfPageCount = 0;
        _pdfPageIndex = 0;
        _pdfNavigation.Visibility = Visibility.Collapsed;
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

    private async Task RunDocumentLoadAsync(Task loadTask, int version)
    {
        using var timeoutCancellation = new CancellationTokenSource();

        try
        {
            var timeoutTask = Task.Delay(DocumentLoadTimeout, timeoutCancellation.Token);
            if (await Task.WhenAny(loadTask, timeoutTask) != loadTask)
            {
                _ = ObserveLateCompletionAsync(loadTask);
                ShowLoadError(version, "La vista previa tardó demasiado en cargar.");
                return;
            }

            timeoutCancellation.Cancel();
            await loadTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV - vista previa de documento: {ex}");
            ShowLoadError(version, "No se pudo mostrar la vista previa.");
        }
    }

    private async Task LoadSvgAsync(string filePath, int version)
    {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await file.OpenReadAsync();
        var source = new SvgImageSource();
        var result = await source.SetSourceAsync(stream);

        if (version != _loadVersion)
        {
            return;
        }

        if (result != SvgImageSourceLoadStatus.Success)
        {
            throw new InvalidDataException($"SVG no compatible: {result}.");
        }

        _image.Source = source;
        _image.Visibility = Visibility.Visible;
        _status.Visibility = Visibility.Collapsed;
    }

    private async Task LoadTextAsync(string filePath, int version)
    {
        // Limitar la cantidad leída evita bloquear el panel ante TXT enormes.
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true);
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var chars = new char[MaxTextCharacters + 1];
        var length = 0;
        while (length < chars.Length)
        {
            var read = await reader.ReadAsync(chars.AsMemory(length, chars.Length - length));
            if (read == 0)
            {
                break;
            }

            length += read;
            if (version != _loadVersion)
            {
                return;
            }
        }

        if (version != _loadVersion)
        {
            return;
        }

        var truncated = length > MaxTextCharacters;
        var text = new string(chars, 0, Math.Min(length, MaxTextCharacters));
        _textContent.Text = truncated
            ? text + "\n\n[Vista previa limitada a los primeros 150.000 caracteres.]"
            : text.Length == 0 ? "El archivo está vacío." : text;
        _textViewer.Visibility = Visibility.Visible;
        _status.Visibility = Visibility.Collapsed;
    }

    private async Task LoadPdfAsync(string filePath, int version)
    {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        var document = await PdfDocument.LoadFromFileAsync(file);

        if (version != _loadVersion)
        {
            return;
        }

        if (document.PageCount == 0)
        {
            throw new InvalidDataException("El documento PDF no tiene páginas.");
        }

        _pdfDocument = document;
        _pdfPageCount = document.PageCount;
        _pdfPageIndex = 0;
        _pdfNavigation.Visibility = Visibility.Visible;
        UpdatePdfNavigation();
        await RenderPdfPageAsync(document, _pdfPageIndex, version);
    }

    private void ChangePdfPage(int direction)
    {
        if (_pdfDocument is null || _pdfPageCount == 0)
        {
            return;
        }

        if (direction < 0 && _pdfPageIndex == 0 ||
            direction > 0 && _pdfPageIndex >= _pdfPageCount - 1)
        {
            return;
        }

        _pdfPageIndex = direction < 0
            ? _pdfPageIndex - 1
            : _pdfPageIndex + 1;
        var version = ++_loadVersion;

        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _status.Text = "Cargando página...";
        _status.Visibility = Visibility.Visible;
        UpdatePdfNavigation();

        _ = RunDocumentLoadAsync(
            RenderPdfPageAsync(_pdfDocument, _pdfPageIndex, version), version);
    }

    private void UpdatePdfNavigation()
    {
        _pdfPageCounter.Text = $"Página {_pdfPageIndex + 1} de {_pdfPageCount}";
        _pdfPreviousButton.IsEnabled = _pdfPageIndex > 0;
        _pdfNextButton.IsEnabled = _pdfPageCount > 0 &&
            _pdfPageIndex < _pdfPageCount - 1;
    }

    private async Task RenderPdfPageAsync(PdfDocument document, uint index, int version)
    {
        using var page = document.GetPage(index);
        using var output = new InMemoryRandomAccessStream();

        // Render limitado para no gastar memoria con páginas grandes.
        var ratio = page.Size.Height / Math.Max(1d, page.Size.Width);
        var targetWidth = Math.Min(1500d, Math.Max(300d, page.Size.Width * 1.8d));
        targetWidth = Math.Min(targetWidth, 2400d / Math.Max(0.01d, ratio));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1d, Math.Round(targetWidth))
        };

        await page.RenderToStreamAsync(output, options);
        if (version != _loadVersion)
        {
            return;
        }

        output.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(output);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        if (version != _loadVersion)
        {
            return;
        }

        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);

        if (version != _loadVersion)
        {
            return;
        }

        _image.Source = source;
        _image.Visibility = Visibility.Visible;
        _status.Visibility = Visibility.Collapsed;
    }

    private void ShowLoadError(int version, string message)
    {
        if (version != _loadVersion)
        {
            return;
        }

        ++_loadVersion;
        _currentPath = null;
        ResetDisplayedContent();
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
