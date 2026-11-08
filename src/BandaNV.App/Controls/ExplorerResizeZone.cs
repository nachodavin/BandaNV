using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BandaNV.App.Controls;

/// <summary>
/// Área de captura del borde redimensionable.
/// Contiene un Thumb estándar de WinUI (que es sealed), pero configura el
/// cursor SizeWestEast desde este Grid derivado y destaca la guía al pasar
/// o arrastrar el mouse. No altera el diseño cuando cambia el estado visual.
/// </summary>
public sealed class ExplorerResizeZone : Grid
{
    private Rectangle? _guide;
    private Thumb? _dragThumb;
    private bool _hovered;
    private bool _dragging;

    public ExplorerResizeZone()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerEntered += (_, _) =>
        {
            _hovered = true;
            UpdateGuide();
        };
        PointerExited += (_, _) =>
        {
            _hovered = false;
            UpdateGuide();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(
            InputSystemCursorShape.SizeWestEast);

        _guide = Children.OfType<Rectangle>().FirstOrDefault();
        var thumb = Children.OfType<Thumb>().FirstOrDefault();

        if (!ReferenceEquals(thumb, _dragThumb))
        {
            DetachThumb();
            _dragThumb = thumb;
            if (_dragThumb is not null)
            {
                _dragThumb.DragStarted += OnDragStarted;
                _dragThumb.DragCompleted += OnDragCompleted;
                _dragThumb.PointerEntered += OnThumbPointerEntered;
                _dragThumb.PointerExited += OnThumbPointerExited;
            }
        }

        UpdateGuide();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachThumb();
        _dragging = false;
        _hovered = false;
    }

    private void DetachThumb()
    {
        if (_dragThumb is not null)
        {
            _dragThumb.DragStarted -= OnDragStarted;
            _dragThumb.DragCompleted -= OnDragCompleted;
            _dragThumb.PointerEntered -= OnThumbPointerEntered;
            _dragThumb.PointerExited -= OnThumbPointerExited;
            _dragThumb = null;
        }
    }

    private void OnThumbPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        UpdateGuide();
    }

    private void OnThumbPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        UpdateGuide();
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _dragging = true;
        UpdateGuide();
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        UpdateGuide();
    }

    private void UpdateGuide()
    {
        if (_guide is null)
        {
            return;
        }

        var highlighted = _hovered || _dragging;
        _guide.Fill = (Brush)Application.Current.Resources[
            highlighted ? "BandaAccentBrush" : "BandaBorderStrongBrush"];
        _guide.Width = highlighted ? 2 : 1;
        _guide.Opacity = highlighted ? 1 : 0.58;
    }
}
