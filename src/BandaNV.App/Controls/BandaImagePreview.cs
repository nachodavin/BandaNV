using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

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

    private readonly Image _image;
    private readonly TextBlock _status;
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

        Content = new Border
        {
            Height = 214,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)resources["BandaNavIconBrush"],
            BorderBrush = (Brush)resources["BandaBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content
        };
        Margin = new Thickness(0, 0, 0, 12);
        Visibility = Visibility.Collapsed;
        Unloaded += (_, _) => Clear();
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
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();
            // Decodificar a un tamaño acotado evita consumir memoria de más.
            var bitmap = new BitmapImage { DecodePixelWidth = 1200 };
            await bitmap.SetSourceAsync(stream);

            // Ignorar cargas obsoletas si el usuario seleccionó otro archivo.
            if (version != _loadVersion)
            {
                return;
            }

            _image.Source = bitmap;
            _image.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Los archivos ausentes, dañados o no decodificables no deben cerrar BandaNV.
            if (version != _loadVersion)
            {
                return;
            }

            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _status.Text = "No se pudo mostrar la vista previa.";
            _status.Visibility = Visibility.Visible;
        }
    }
}
