using Microsoft.UI.Xaml;

namespace BandaNV.App.Controls;

/// <summary>
/// Comparte los anchos entre los encabezados y las filas virtualizadas de
/// Explorar. Las propiedades de dependencia actualizan las filas ya visibles
/// sin reiniciar el listado ni perder la selección al redimensionar.
/// </summary>
public sealed class ExplorerColumnLayout : DependencyObject
{
    public static readonly DependencyProperty NameWidthProperty =
        DependencyProperty.Register(
            nameof(NameWidth), typeof(GridLength), typeof(ExplorerColumnLayout),
            new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

    public static readonly DependencyProperty ModifiedWidthProperty =
        DependencyProperty.Register(
            nameof(ModifiedWidth), typeof(GridLength), typeof(ExplorerColumnLayout),
            new PropertyMetadata(new GridLength(200)));

    public static readonly DependencyProperty TypeWidthProperty =
        DependencyProperty.Register(
            nameof(TypeWidth), typeof(GridLength), typeof(ExplorerColumnLayout),
            new PropertyMetadata(new GridLength(175)));

    public static readonly DependencyProperty SizeWidthProperty =
        DependencyProperty.Register(
            nameof(SizeWidth), typeof(GridLength), typeof(ExplorerColumnLayout),
            new PropertyMetadata(new GridLength(125)));

    public GridLength NameWidth
    {
        get => (GridLength)GetValue(NameWidthProperty);
        set => SetValue(NameWidthProperty, value);
    }

    public GridLength ModifiedWidth
    {
        get => (GridLength)GetValue(ModifiedWidthProperty);
        set => SetValue(ModifiedWidthProperty, value);
    }

    public GridLength TypeWidth
    {
        get => (GridLength)GetValue(TypeWidthProperty);
        set => SetValue(TypeWidthProperty, value);
    }

    public GridLength SizeWidth
    {
        get => (GridLength)GetValue(SizeWidthProperty);
        set => SetValue(SizeWidthProperty, value);
    }

    public void Resize(string column, double actualWidth, double delta)
    {
        if (!double.IsFinite(actualWidth) || !double.IsFinite(delta))
        {
            return;
        }

        var (minimum, maximum) = column switch
        {
            "name" => (180d, 1400d),
            "modified" => (130d, 700d),
            "type" => (100d, 650d),
            "size" => (85d, 500d),
            _ => (0d, 0d)
        };

        if (minimum == 0d)
        {
            return;
        }

        var newWidth = new GridLength(Math.Clamp(actualWidth + delta, minimum, maximum));

        switch (column)
        {
            case "name":
                NameWidth = newWidth;
                break;
            case "modified":
                ModifiedWidth = newWidth;
                break;
            case "type":
                TypeWidth = newWidth;
                break;
            case "size":
                SizeWidth = newWidth;
                break;
        }
    }
}
