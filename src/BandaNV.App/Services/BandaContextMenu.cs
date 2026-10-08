using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Services;

/// <summary>
/// Menú contextual coherente entre los listados de Organizar y Buscar.
/// Reutiliza sus acciones existentes; no ejecuta operaciones de archivos directamente.
/// </summary>
internal static class BandaContextMenu
{
    public static MenuFlyout Create() =>
        new()
        {
            MenuFlyoutPresenterStyle =
                (Style)Application.Current.Resources[
                    "BandaContextMenuPresenterStyle"]
        };

    public static void Add(
        MenuFlyout menu,
        string text,
        string glyph,
        RoutedEventHandler action,
        bool enabled = true,
        bool danger = false)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = enabled,
            Style = (Style)Application.Current.Resources[
                "BandaContextMenuItemStyle"],
            Icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 15,
                Foreground = (Brush)Application.Current.Resources[
                    danger ? "BandaDangerBrush" : "BandaMutedStrongBrush"]
            }
        };

        item.Click += action;
        menu.Items.Add(item);
    }

    public static void Separator(MenuFlyout menu) =>
        menu.Items.Add(new MenuFlyoutSeparator());

    /// <summary>
    /// El clic derecho conserva la multiselección existente cuando el elemento
    /// ya está seleccionado. En otro caso selecciona exclusivamente ese elemento.
    /// Ignora encabezados de grupos y espacios vacíos.
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
                if (container.Content is not T item ||
                    !ReferenceEquals(
                        list.ContainerFromItem(item),
                        container))
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
        MenuFlyout menu,
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
