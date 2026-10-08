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
        var pressedBackground = (Brush)resources["BandaActionPressedBrush"];

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

        // Escopados a cada botón: los recursos globales de WinUI no se
        // modifican en caliente al abrir el menú.
        button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonForegroundPointerOver"] = hoverText;
        button.Resources["ButtonForegroundPressed"] = hoverText;
        button.Resources["ButtonBorderBrushPointerOver"] = hoverBackground;
        button.Resources["ButtonBorderBrushPressed"] = pressedBackground;

        // La etiqueta está dentro de un StackPanel. Su color se sincroniza
        // con el fondo teal para evitar texto negro sobre un fondo oscuro.
        void SetHovered(bool hovered)
        {
            label.Foreground = hovered ? hoverText : normalText;
            icon.Foreground = hovered ? hoverText : normalIcon;
        }

        button.PointerEntered += (_, _) => SetHovered(true);
        button.PointerExited += (_, _) => SetHovered(false);
        button.GotFocus += (_, _) => SetHovered(true);
        button.LostFocus += (_, _) => SetHovered(false);

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

        ((StackPanel)menu.Content).Children.Add(button);
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
        menu.ShowAt(
            list,
            new FlyoutShowOptions
            {
                Position = e.GetPosition(list)
            });

        e.Handled = true;
    }
}
