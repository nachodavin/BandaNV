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
            new PropertyMetadata(new GridLength(320)));

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

    // Los cuatro anchos de columna son píxeles, no estrellas: la fila
    // seleccionable termina en Tamaño y deja libre el resto del listado.
    // Incluye 20 px de padding y 3 separaciones de 12 px.
    private const double CellPaddingAndSpacing = 56;

    public static readonly DependencyProperty RowWidthProperty =
        DependencyProperty.Register(
            nameof(RowWidth), typeof(double), typeof(ExplorerColumnLayout),
            new PropertyMetadata(876d));

    public double RowWidth
    {
        get => (double)GetValue(RowWidthProperty);
        private set => SetValue(RowWidthProperty, value);
    }

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

    private void RecalculateRowWidth()
    {
        RowWidth = NameWidth.Value + ModifiedWidth.Value + TypeWidth.Value +
                   SizeWidth.Value + CellPaddingAndSpacing;
    }

    /// <summary>
    /// Ajusta las cuatro columnas cuando la ventana de Explorar es angosta.
    /// Sin personalizaciones recupera las proporciones predeterminadas al
    /// ampliar la ventana; después de un ajuste manual solo reduce si
    /// resulta necesario, sin sobrescribir la elección del usuario.
    /// </summary>
    public void FitToViewport(double availableWidth, bool userResized)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return;
        }

        double[] minimums = [180, 130, 100, 85];
        double[] preferred = userResized
            ? [NameWidth.Value, ModifiedWidth.Value, TypeWidth.Value, SizeWidth.Value]
            : [320, 200, 175, 125];

        var minTotal = minimums.Sum();
        var desired = Math.Max(minTotal, availableWidth - CellPaddingAndSpacing);
        var preferredTotal = preferred.Sum();

        if (userResized && preferredTotal <= desired + 1)
        {
            return;
        }

        if (preferredTotal > desired)
        {
            var capacity = preferredTotal - minTotal;
            var proportion = capacity <= 0
                ? 0d
                : Math.Clamp((desired - minTotal) / capacity, 0d, 1d);

            for (var index = 0; index < preferred.Length; index++)
            {
                preferred[index] = minimums[index] +
                    Math.Max(0d, preferred[index] - minimums[index]) * proportion;
            }
        }

        NameWidth = new GridLength(preferred[0]);
        ModifiedWidth = new GridLength(preferred[1]);
        TypeWidth = new GridLength(preferred[2]);
        SizeWidth = new GridLength(preferred[3]);
        RecalculateRowWidth();
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

        RecalculateRowWidth();
    }
}
