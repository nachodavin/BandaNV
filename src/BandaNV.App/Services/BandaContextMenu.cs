using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Services;

/// <summary>
/// Menú contextual de BandaNV. Se implementa con el Flyout que ya utilizan
/// los selectores de la app, evitando MenuFlyout y cambios globales de
/// recursos de controles al abrir el menú.
/// Las acciones siguen reutilizando los handlers de los paneles laterales.
/// </summary>
internal static class BandaContextMenu
{
    public static Flyout Create()
    {
        var resources = Application.Current.Resources;

        return new Flyout
        {
            FlyoutPresenterStyle =
                (Style)resources["BandaContextMenuPresenterStyle"],
            Content = new StackPanel
            {
                Spacing = 2,
                MinWidth = 220
            }
        };
    }

    public static void Add(
        Flyout menu,
        string text,
        string glyph,
        RoutedEventHandler action,
        bool enabled = true,
        bool danger = false)
    {
        var resources = Application.Current.Resources;
        var normalText =
            (Brush)resources[danger ? "BandaDangerBrush" : "BandaTextBrush"];
        var hoverText = (Brush)resources["BandaActionForegroundBrush"];
        var normalIcon =
            (Brush)resources[danger ? "BandaDangerBrush" : "BandaMutedStrongBrush"];
        var hoverBackground = (Brush)resources["BandaActionHoverBrush"];

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = 15,
            Foreground = normalIcon,
            Width = 21,
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = normalText,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(icon);
        row.Children.Add(label);

        var button = new Button
        {
            Style = (Style)resources["BandaContextMenuItemStyle"],
            Content = row,
            IsEnabled = enabled
        };

        // La superficie de selección es independiente del template
        // de Button. WinUI puede enfocar automáticamente la primera
        // opción al abrir; antes el texto pasaba a negro sin que el
        // template pintara el hover, y la opción parecía invisible.
        var normalBackground = new SolidColorBrush(
            Microsoft.UI.Colors.Transparent);
        var hoverSurface = new Border
        {
            Background = normalBackground,
            CornerRadius = new CornerRadius(9),
            Child = button
        };

        // Evitar que los estados propios del Button oculten el fondo
        // que administra el contenedor. No modificamos recursos globales.
        button.Resources["ButtonBackgroundPointerOver"] = normalBackground;
        button.Resources["ButtonBackgroundPressed"] = normalBackground;
        button.Resources["ButtonBorderBrushPointerOver"] = normalBackground;
        button.Resources["ButtonBorderBrushPressed"] = normalBackground;

        var isPointerOver = false;

        void RefreshVisualState()
        {
            // El primer botón recibe foco automáticamente al abrir el Flyout.
            // El destacado teal debe responder solamente al mouse.
            var hovered = enabled && isPointerOver;
            hoverSurface.Background = hovered ? hoverBackground : normalBackground;
            label.Foreground = hovered ? hoverText : normalText;
            icon.Foreground = hovered ? hoverText : normalIcon;
        }

        button.PointerEntered += (_, _) =>
        {
            isPointerOver = true;
            RefreshVisualState();
        };
        button.PointerExited += (_, _) =>
        {
            isPointerOver = false;
            RefreshVisualState();
        };

        button.Click += (sender, args) =>
        {
            // Un handler puede abrir un modal de renombrar o eliminar.
            // Ejecutarlo después de cerrar este flyout evita reentrancia
            // entre los dos popups.
            EventHandler<object>? closedHandler = null;
            closedHandler = (_, _) =>
            {
                menu.Closed -= closedHandler;
                action(sender, args);
            };

            menu.Closed += closedHandler;
            menu.Hide();
        };

        ((StackPanel)menu.Content).Children.Add(hoverSurface);
    }

    public static void Separator(Flyout menu)
    {
        ((StackPanel)menu.Content).Children.Add(
            new Border
            {
                Height = 1,
                Margin = new Thickness(8, 5, 8, 5),
                Background = (Brush)Application.Current.Resources[
                    "BandaBorderBrush"]
            });
    }

    /// <summary>
    /// Conserva la multiselección cuando el elemento ya está seleccionado.
    /// Si no estaba marcado, selecciona ese elemento solamente.
    /// Ignora los encabezados y el espacio vacío del listado.
    /// </summary>
    public static bool SelectForRightClick<T>(
        ListView list,
        object? originalSource)
        where T : class
    {
        var current = originalSource as DependencyObject;

        while (current is not null &&
               !ReferenceEquals(current, list))
        {
            if (current is ListViewItem container)
            {
                var item = container.Content as T ??
                           container.DataContext as T;

                if (item is null)
                {
                    return false;
                }

                if (!list.SelectedItems.Contains(item))
                {
                    list.SelectedItems.Clear();
                    list.SelectedItem = item;
                }

                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    public static void Show(
        Flyout menu,
        ListView list,
        RightTappedRoutedEventArgs e)
    {
        var click = e.GetPosition(list);

        // La posición refiere al clic derecho. Con la alineación
        // inferior-izquierda, el menú nace inmediatamente abajo y
        // a la derecha del puntero, como en el Explorador de Windows.
        // WinUI ajustará la ubicación cerca de los bordes de pantalla.
        menu.ShowAt(
            list,
            new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(
                    click.X + 2,
                    click.Y + 2),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
            });

        e.Handled = true;
    }
}
