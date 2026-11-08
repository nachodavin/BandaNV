using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BandaNV.App.Controls;

/// <summary>
/// Separador redimensionable para columnas y paneles del Explorador.
/// Ofrece una zona cómoda para arrastrar, cursor horizontal de Windows
/// y una guía visible al pasar el puntero o al arrastrar.
/// </summary>
public sealed class ExplorerResizeThumb : Thumb
{
    private Rectangle? _guide;
    private bool _dragging;

    public ExplorerResizeThumb()
    {
        // ProtectedCursor es protegido en WinUI 3: debe establecerlo
        // una clase derivada, no se puede escribir desde XAML.
        Loaded += (_, _) =>
        {
            ProtectedCursor = InputSystemCursor.Create(
                InputSystemCursorShape.SizeWestEast);
            UpdateGuide();
        };

        PointerEntered += (_, _) => UpdateGuide(highlight: true);
        PointerExited += (_, _) => UpdateGuide(highlight: false);
        DragStarted += (_, _) =>
        {
            _dragging = true;
            UpdateGuide(highlight: true);
        };
        DragCompleted += (_, _) =>
        {
            _dragging = false;
            UpdateGuide();
        };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _guide = GetTemplateChild("ResizeGuide") as Rectangle;
        UpdateGuide();
    }

    private void UpdateGuide(bool highlight = false)
    {
        if (_guide is null)
        {
            return;
        }

        var isHighlighted = highlight || _dragging;
        _guide.Fill = (Brush)Application.Current.Resources[
            isHighlighted ? "BandaAccentBrush" : "BandaBorderStrongBrush"];
        _guide.Opacity = isHighlighted ? 1 : 0.58;
        _guide.Width = isHighlighted ? 2 : 1;
    }
}
