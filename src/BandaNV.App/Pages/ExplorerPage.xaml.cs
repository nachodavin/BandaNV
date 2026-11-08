using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using BandaNV.Core.Models;
using BandaNV.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace BandaNV.App.Pages;

/// <summary>
/// Primera etapa del explorador de archivos: navegación real, solo lectura.
/// Buscar y Organizar continúan disponibles sin cambios.
/// </summary>
public sealed partial class ExplorerPage : Page
{
    private const string ThisPc = "bandanv://this-pc";
    private readonly ExplorerFileSystemService _fileSystem = new();
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    private IReadOnlyList<ExplorerItem> _allItems = [];
    private string? _currentLocation;
    private CancellationTokenSource? _navigationCts;
    private int _navigationVersion;
    private bool _started;
    private bool _isNavigating;
    private int _skippedCount;
    private bool _truncated;
    private bool _detailsVisible = true;
    private string _sortField = "name";
    private bool _sortDescending;

    private static readonly HashSet<string> PreviewExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
            ".pdf", ".txt", ".svg"
        };

    private enum HistoryAction
    {
        Navigate,
        Back,
        Forward,
        Refresh
    }

    public ExplorerPage()
    {
        InitializeComponent();
        UpdateNavigationButtons();
        UpdateSelectionDetails();
    }

    private async void ExplorerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await NavigateAsync(ThisPc);
    }

    private void ExplorerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ++_navigationVersion;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
        ExplorerSelectedPreview.Clear();
    }

    private async Task<bool> NavigateAsync(
        string requestedLocation,
        HistoryAction action = HistoryAction.Navigate)
    {
        var location = requestedLocation;

        if (!string.Equals(location, ThisPc, StringComparison.Ordinal))
        {
            try
            {
                var input = requestedLocation.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(input))
                {
                    ExplorerStatusText.Text = "Ingresá una ruta absoluta, por ejemplo C:\\ o D:\\Carpeta.";
                    return false;
                }

                location = Path.GetFullPath(input);
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                ExplorerStatusText.Text = "La ruta indicada no es válida.";
                return false;
            }
        }

        // Si la ubicación no cambió, actualizar sin crear una entrada duplicada.
        if (string.Equals(location, _currentLocation, StringComparison.OrdinalIgnoreCase) &&
            action == HistoryAction.Navigate)
        {
            action = HistoryAction.Refresh;
        }

        var version = ++_navigationVersion;
        var navigationSucceeded = false;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        var request = new CancellationTokenSource();
        _navigationCts = request;
        _isNavigating = true;
        ExplorerLoadingRing.IsActive = true;
        ExplorerLoadingRing.Visibility = Visibility.Visible;
        ExplorerStatusText.Text = "Cargando contenido...";

        try
        {
            var listing = string.Equals(location, ThisPc, StringComparison.Ordinal)
                ? await _fileSystem.GetDrivesAsync(request.Token)
                : await _fileSystem.GetFolderAsync(location, request.Token);

            if (version != _navigationVersion || request.IsCancellationRequested)
            {
                return false;
            }

            switch (action)
            {
                case HistoryAction.Navigate:
                    if (_currentLocation is not null)
                    {
                        _back.Push(_currentLocation);
                    }
                    _forward.Clear();
                    break;
                case HistoryAction.Back:
                    if (_back.Count > 0 && _back.Peek().Equals(location, StringComparison.OrdinalIgnoreCase))
                    {
                        _back.Pop();
                        if (_currentLocation is not null)
                        {
                            _forward.Push(_currentLocation);
                        }
                    }
                    break;
                case HistoryAction.Forward:
                    if (_forward.Count > 0 &&
                        _forward.Peek().Equals(location, StringComparison.OrdinalIgnoreCase))
                    {
                        _forward.Pop();
                        if (_currentLocation is not null)
                        {
                            _back.Push(_currentLocation);
                        }
                    }
                    break;
            }

            _currentLocation = location;
            _allItems = listing.Items;
            _skippedCount = listing.SkippedCount;
            _truncated = listing.Truncated;

            // El filtro se aplica solo a esta carpeta; no se hace escaneo global.
            ExplorerFilterBox.Text = string.Empty;
            ApplyFilter();
            RenderBreadcrumbs();
            UpdateNavigationButtons();
            UpdateFolderNotice();

            if (location == ThisPc)
            {
                RenderDriveShortcuts(listing.Items);
            }

            navigationSucceeded = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (version == _navigationVersion)
            {
                ExplorerStatusText.Text = ex is UnauthorizedAccessException
                    ? "No tenés permisos para acceder a esa carpeta."
                    : "No se pudo abrir la ubicación. Es posible que ya no esté disponible.";
                Debug.WriteLine($"BandaNV Explorer - no se pudo navegar a {location}: {ex}");
            }

            // La ubicación y su historial solo cambian si el listado fue exitoso.
            return false;
        }
        finally
        {
            if (version == _navigationVersion)
            {
                _isNavigating = false;
                ExplorerLoadingRing.IsActive = false;
                ExplorerLoadingRing.Visibility = Visibility.Collapsed;
                if (navigationSucceeded)
                {
                    UpdateCountText();
                }
                UpdateSelectionDetails();
            }
        }
    }

    private void ApplyFilter()
    {
        var query = ExplorerFilterBox.Text?.Trim() ?? string.Empty;
        var visible = (string.IsNullOrEmpty(query)
                ? _allItems
                : _allItems.Where(item => item.Name.Contains(
                    query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(item => item, Comparer<ExplorerItem>.Create(CompareExplorerItems))
            .ToArray();

        ExplorerFilesList.ItemsSource = visible;

        var count = visible.Length;
        var total = _allItems.Count;

        if (!_isNavigating)
        {
            ExplorerStatusText.Text = string.IsNullOrEmpty(query)
                ? $"{total:N0} elementos"
                : $"{count:N0} de {total:N0} elementos";
        }

        ExplorerSelectionCountText.Text = string.Empty;
        UpdateSelectionDetails();
    }

    private int CompareExplorerItems(ExplorerItem left, ExplorerItem right)
    {
        // Windows Explorer conserva las carpetas primero, sea cual sea el orden.
        var foldersFirst = right.IsDirectory.CompareTo(left.IsDirectory);
        if (foldersFirst != 0)
        {
            return foldersFirst;
        }

        var result = _sortField switch
        {
            "modified" => Comparer<DateTime?>.Default.Compare(left.ModifiedAt, right.ModifiedAt),
            "type" => StringComparer.CurrentCultureIgnoreCase.Compare(left.TypeName, right.TypeName),
            "size" => Comparer<long?>.Default.Compare(left.SizeBytes, right.SizeBytes),
            _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name)
        };

        if (result == 0)
        {
            result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        }

        return _sortDescending ? -result : result;
    }

    private void ExplorerSortFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string field })
        {
            return;
        }

        if (field is not ("name" or "modified" or "type" or "size"))
        {
            return;
        }

        _sortField = field;
        UpdateSortLabel();
        ApplyFilter();
        UpdateCountText();
    }

    private void ExplorerSortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string direction })
        {
            return;
        }

        _sortDescending = direction == "desc";
        UpdateSortLabel();
        ApplyFilter();
        UpdateCountText();
    }

    private void UpdateSortLabel()
    {
        var label = _sortField switch
        {
            "modified" => "Fecha",
            "type" => "Tipo",
            "size" => "Tamaño",
            _ => "Nombre"
        };

        ExplorerSortLabel.Text = $"Ordenar: {label}";
    }

    private void ExplorerToggleDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        _detailsVisible = !_detailsVisible;
        ExplorerDetailsPane.Visibility = _detailsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExplorerDetailsColumn.Width = _detailsVisible
            ? new GridLength(280)
            : new GridLength(0);

        if (_detailsVisible)
        {
            UpdateSelectionDetails();
        }
        else
        {
            ExplorerSelectedPreview.Clear();
        }
    }

    private void ExplorerCopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ExplorerFilesList.SelectedItems
            .OfType<ExplorerItem>()
            .ToArray();

        if (selected.Length == 0)
        {
            return;
        }

        try
        {
            var content = new DataPackage();
            content.SetText(string.Join(Environment.NewLine,
                selected.Select(item => item.FullPath)));
            Clipboard.SetContent(content);
            ExplorerStatusText.Text = selected.Length == 1
                ? "Ruta copiada al portapapeles."
                : $"{selected.Length:N0} rutas copiadas al portapapeles.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV Explorer - no se pudo copiar la ruta: {ex}");
            ExplorerStatusText.Text = "No se pudo copiar la ruta al portapapeles.";
        }
    }

    private void UpdateFolderNotice()
    {
        var messages = new List<string>();
        if (_truncated)
        {
            messages.Add(
                $"Listado parcial: se muestran los primeros {ExplorerFileSystemService.MaxItemsPerFolder:N0} elementos.");
        }
        if (_skippedCount > 0)
        {
            messages.Add($"{_skippedCount:N0} elementos no pudieron leerse.");
        }
        if (_allItems.Count == 0)
        {
            messages.Add(_currentLocation == ThisPc
                ? "No se encontraron unidades."
                : "Esta carpeta está vacía o no tiene elementos visibles.");
        }

        ExplorerFolderNotice.Text = string.Join(" ", messages);
        ExplorerFolderNotice.Visibility = messages.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateCountText();
    }

    private void UpdateCountText()
    {
        if (_isNavigating)
        {
            return;
        }

        var shown = ExplorerFilesList.ItemsSource is IReadOnlyList<ExplorerItem> list
            ? list.Count
            : _allItems.Count;

        var text = ExplorerFilterBox.Text?.Trim();
        ExplorerStatusText.Text = string.IsNullOrEmpty(text)
            ? $"{shown:N0} elementos"
            : $"{shown:N0} de {_allItems.Count:N0} elementos";

        if (_truncated)
        {
            ExplorerStatusText.Text += " · listado parcial";
        }
    }

    private void RenderBreadcrumbs()
    {
        ExplorerBreadcrumbPanel.Children.Clear();

        AddBreadcrumb("Este equipo", ThisPc);

        if (_currentLocation is null || _currentLocation == ThisPc)
        {
            ExplorerAddressBox.Text = "Este equipo";
            return;
        }

        ExplorerAddressBox.Text = _currentLocation;

        var root = Path.GetPathRoot(_currentLocation);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        AddBreadcrumb(root, root);

        var rest = _currentLocation[root.Length..];
        var current = root;

        foreach (var segment in rest.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            AddBreadcrumb(segment, current);
        }
    }

    private void AddBreadcrumb(string label, string destination)
    {
        if (ExplorerBreadcrumbPanel.Children.Count > 0)
        {
            ExplorerBreadcrumbPanel.Children.Add(new TextBlock
            {
                Text = "›",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["BandaMutedBrush"],
                Margin = new Thickness(2, 0, 2, 0)
            });
        }

        var button = new Button
        {
            Content = label,
            Tag = destination,
            FontSize = 12,
            MinHeight = 30,
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources["BandaCardAltBrush"],
            Foreground = (Brush)Application.Current.Resources["BandaTextBrush"],
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7)
        };
        button.Click += ExplorerBreadcrumbButton_Click;
        ExplorerBreadcrumbPanel.Children.Add(button);
    }

    private void RenderDriveShortcuts(IReadOnlyList<ExplorerItem> drives)
    {
        ExplorerDrivesSidebar.Children.Clear();

        foreach (var drive in drives.Where(item => item.IsDrive))
        {
            var button = new Button
            {
                Content = drive.Name,
                Tag = drive.FullPath,
                Style = (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
            };
            button.Click += ExplorerBreadcrumbButton_Click;
            ExplorerDrivesSidebar.Children.Add(button);
        }
    }

    private void UpdateNavigationButtons()
    {
        ExplorerBackButton.IsEnabled = _back.Count > 0;
        ExplorerForwardButton.IsEnabled = _forward.Count > 0;
        ExplorerUpButton.IsEnabled =
            _currentLocation is not null && _currentLocation != ThisPc;
    }

    private string GetParentLocation()
    {
        if (_currentLocation is null || _currentLocation == ThisPc)
        {
            return ThisPc;
        }

        try
        {
            return Directory.GetParent(_currentLocation)?.FullName ?? ThisPc;
        }
        catch (Exception)
        {
            return ThisPc;
        }
    }

    private async void ExplorerBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_back.TryPeek(out var previous))
        {
            await NavigateAsync(previous, HistoryAction.Back);
        }
    }

    private async void ExplorerForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forward.TryPeek(out var next))
        {
            await NavigateAsync(next, HistoryAction.Forward);
        }
    }

    private async void ExplorerUpButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync(GetParentLocation());
    }

    private async void ExplorerRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync(_currentLocation ?? ThisPc, HistoryAction.Refresh);
    }

    private async void ExplorerBreadcrumbButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            await NavigateAsync(path);
        }
    }

    private async void ExplorerShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string shortcut })
        {
            return;
        }

        var path = shortcut switch
        {
            "thispc" => ThisPc,
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "downloads" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "source" => App.Settings.Current.SourceFolder,
            "destination" => App.Settings.Current.DestinationFolder,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            ExplorerStatusText.Text = "Esta ubicación todavía no está configurada.";
            return;
        }

        await NavigateAsync(path);
    }

    private void ExplorerEditAddressButton_Click(object sender, RoutedEventArgs e)
    {
        ExplorerAddressBox.Text = _currentLocation == ThisPc
            ? "Este equipo"
            : _currentLocation ?? string.Empty;

        ExplorerBreadcrumbScroll.Visibility = Visibility.Collapsed;
        ExplorerAddressBox.Visibility = Visibility.Visible;
        ExplorerAddressBox.Focus(FocusState.Programmatic);
        ExplorerAddressBox.SelectAll();
    }

    private void HideAddressEditor()
    {
        ExplorerAddressBox.Visibility = Visibility.Collapsed;
        ExplorerBreadcrumbScroll.Visibility = Visibility.Visible;
    }

    private void ExplorerAddressBox_LostFocus(object sender, RoutedEventArgs e)
    {
        HideAddressEditor();
    }

    private async void ExplorerAddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideAddressEditor();
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        var path = ExplorerAddressBox.Text?.Trim() ?? string.Empty;
        if (path.Equals("Este equipo", StringComparison.OrdinalIgnoreCase))
        {
            path = ThisPc;
        }

        if (await NavigateAsync(path))
        {
            HideAddressEditor();
        }
    }

    private void ExplorerFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
        UpdateCountText();
    }

    private async void ExplorerFilesList_DoubleTapped(
        object sender, DoubleTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;

        while (current is not null && current != ExplorerFilesList)
        {
            if (current is ListViewItem { Content: ExplorerItem item })
            {
                await OpenItemAsync(item);
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private async void ExplorerOpenSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorerFilesList.SelectedItems.Count == 1 &&
            ExplorerFilesList.SelectedItems[0] is ExplorerItem item)
        {
            await OpenItemAsync(item);
        }
    }

    private async Task OpenItemAsync(ExplorerItem item)
    {
        if (item.IsDirectory)
        {
            await NavigateAsync(item.FullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV Explorer - no se pudo abrir {item.FullPath}: {ex}");
            ExplorerStatusText.Text = "No se pudo abrir el archivo con la aplicación asociada.";
        }
    }

    private void ExplorerFilesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionDetails();
    }

    private void UpdateSelectionDetails()
    {
        if (ExplorerFilesList.SelectedItems.Count != 1 ||
            ExplorerFilesList.SelectedItems[0] is not ExplorerItem item)
        {
            ExplorerSelectedPreview.Clear();
            ExplorerOpenSelectionButton.IsEnabled = false;
            ExplorerToolbarOpenButton.IsEnabled = false;

            var count = ExplorerFilesList.SelectedItems.Count;
            ExplorerToolbarCopyPathButton.IsEnabled = count > 0;
            ExplorerDetailName.Text = count > 1
                ? $"{count:N0} elementos seleccionados"
                : "Seleccioná un archivo o carpeta.";
            ExplorerFileIconPanel.Visibility = count == 0 && _detailsVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            ExplorerFileLargeIcon.Glyph = "\uE8A5";
            ExplorerDetailType.Text = string.Empty;
            ExplorerDetailSize.Text = string.Empty;
            ExplorerDetailModified.Text = string.Empty;
            ExplorerDetailPath.Text = string.Empty;
            ExplorerSelectionCountText.Text = count > 0
                ? $"{count:N0} seleccionados"
                : string.Empty;
            return;
        }

        if (_detailsVisible)
        {
            ExplorerSelectedPreview.Show(item.FullPath, item.IsDirectory);
        }
        ExplorerOpenSelectionButton.IsEnabled = true;
        ExplorerToolbarOpenButton.IsEnabled = true;
        ExplorerToolbarCopyPathButton.IsEnabled = true;

        var canPreview = !item.IsDirectory &&
            PreviewExtensions.Contains(Path.GetExtension(item.FullPath));
        ExplorerFileIconPanel.Visibility = _detailsVisible && !canPreview
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExplorerFileLargeIcon.Glyph = item.IconGlyph;

        ExplorerDetailName.Text = item.Name;
        ExplorerDetailType.Text = item.TypeName;
        ExplorerDetailSize.Text = item.IsDirectory
            ? "Carpeta"
            : $"Tamaño: {item.SizeText}";
        ExplorerDetailModified.Text = $"Modificado: {item.ModifiedText}";
        ExplorerDetailPath.Text = item.FullPath;
        ExplorerSelectionCountText.Text = "1 seleccionado";
    }
}
