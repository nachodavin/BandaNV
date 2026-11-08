using System.Diagnostics;
using BandaNV.App.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using BandaNV.Core.Models;
using BandaNV.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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
    private ExplorerItem[] _visibleItems = [];
    private readonly CollectionViewSource _groupedView = new()
    {
        IsSourceGrouped = true,
        ItemsPath = new PropertyPath(nameof(ExplorerGroup.Items))
    };
    private string? _currentLocation;
    private CancellationTokenSource? _navigationCts;
    private int _navigationVersion;
    private bool _started;
    private bool _isNavigating;
    private int _skippedCount;
    private bool _truncated;
    private bool _detailsVisible = true;
    private double _detailsWidth = 280;
    private string _sortField = "modified";
    private bool _sortDescending = true;
    private string _groupField = "modified";
    private bool _navigationPaneVisible = true;
    private bool _showHiddenItems;
    private bool _showFileExtensions = true;
    private bool _compactRows;
    private bool _showItemCheckboxes = true;
    private bool _previewOnly;
    private bool _selectionBatch;

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
                : await _fileSystem.GetFolderAsync(
                    location, request.Token, includeHidden: _showHiddenItems);

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
            // Actualizar o cambiar visibilidad no debe borrar el filtro actual.
            if (action != HistoryAction.Refresh)
            {
                ExplorerFilterBox.Text = string.Empty;
            }
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
            .Select(item => item with
            {
                ShowFileExtension = _showFileExtensions,
                CompactMode = _compactRows
            })
            .ToArray();

        _visibleItems = visible;

        // «Este equipo» contiene unidades sin fechas: se muestra como lista,
        // sin crear un grupo artificial «Sin fecha».
        if (_groupField == "none" || _currentLocation == ThisPc)
        {
            ExplorerFilesList.ItemsSource = visible;
        }
        else
        {
            var groups = visible
                .GroupBy(GetGroupKey)
                .OrderBy(group => group.Key.Item1)
                .ThenBy(group => group.Key.Item2, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ExplorerGroup(group.Key.Item2, group.ToList()))
                .ToArray();

            _groupedView.Source = groups;
            ExplorerFilesList.ItemsSource = _groupedView.View;
        }

        UpdateCountText();
        UpdateSelectionDetails();
    }

    private (int Order, string Label) GetGroupKey(ExplorerItem item)
    {
        if (_groupField == "name")
        {
            var first = item.Name.Length == 0 ? '#' : char.ToUpperInvariant(item.Name[0]);
            var label = char.IsLetter(first) ? first.ToString() : "#";
            return (label == "#" ? 0 : first, label);
        }

        if (_groupField == "type")
        {
            return (0, item.TypeName);
        }

        if (_groupField == "size")
        {
            if (item.IsDirectory) return (0, "Carpetas");
            var size = item.SizeBytes ?? 0;
            return size switch
            {
                < 1024L * 1024 => (1, "Menos de 1 MB"),
                < 100L * 1024 * 1024 => (2, "De 1 MB a 100 MB"),
                < 1024L * 1024 * 1024 => (3, "De 100 MB a 1 GB"),
                _ => (4, "Más de 1 GB")
            };
        }

        var date = item.ModifiedAt?.Date;
        if (!date.HasValue) return (999, "Sin fecha");

        var today = DateTime.Today;
        var firstOfWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var previousMonth = firstOfMonth.AddMonths(-1);

        if (date.Value >= today) return (0, "Hoy");
        if (date.Value >= today.AddDays(-1)) return (1, "Ayer");
        if (date.Value >= firstOfWeek) return (2, "Esta semana");
        if (date.Value >= firstOfWeek.AddDays(-7)) return (3, "La semana pasada");
        if (date.Value >= firstOfMonth) return (4, "Este mes");
        if (date.Value >= previousMonth) return (5, "El mes pasado");
        if (date.Value.Year == today.Year) return (6, "Anteriormente este año");

        return (10000 - date.Value.Year, date.Value.Year.ToString());
    }

    private void ExplorerDetailsResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_detailsVisible || Math.Abs(e.HorizontalChange) < 0.01)
        {
            return;
        }

        // El divisor está a la IZQUIERDA de Detalles: moverlo hacia la
        // izquierda agranda el panel y hacia la derecha lo achica.
        const double minDetails = 220;
        const double maxDetails = 800;
        const double minFileList = 400;

        var remaining = ExplorerContentGrid.ActualWidth
            - ExplorerLocationsColumn.ActualWidth
            - ExplorerDetailsDividerColumn.ActualWidth
            - minFileList;
        var allowedMaximum = Math.Max(
            minDetails, Math.Min(maxDetails, remaining));

        _detailsWidth = Math.Clamp(
            _detailsWidth - e.HorizontalChange, minDetails, allowedMaximum);

        ExplorerDetailsColumn.Width = new GridLength(_detailsWidth);
    }

    private void ExplorerColumnResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string column } ||
            Math.Abs(e.HorizontalChange) < 0.01)
        {
            return;
        }

        var widths = new[]
        {
            ExplorerHeaderNameColumn.ActualWidth,
            ExplorerHeaderModifiedColumn.ActualWidth,
            ExplorerHeaderTypeColumn.ActualWidth,
            ExplorerHeaderSizeColumn.ActualWidth
        };

        string[] keys = ["name", "modified", "type", "size"];
        double[] minimums = [180, 130, 100, 85];
        double[] maximums = [1400, 700, 650, 500];
        var index = Array.IndexOf(keys, column);

        if (index < 0 || widths.Any(width => width <= 0))
        {
            return;
        }

        var layout = (ExplorerColumnLayout)Resources["ExplorerColumnLayout"];

        if (e.HorizontalChange < 0)
        {
            // Reducir una columna libera espacio para ampliar otra.
            layout.Resize(column, widths[index], e.HorizontalChange);
            return;
        }

        var desired = Math.Clamp(
            widths[index] + e.HorizontalChange, minimums[index], maximums[index]);
        if (desired <= widths[index])
        {
            return;
        }

        // Una columna puede ampliarse usando espacio libre y, si es
        // necesario, reduciendo las vecinas hasta sus mínimos. Así el
        // encabezado sigue alineado con todas las filas del listado.
        const double spacingAndPadding = 56; // 10 + 10 + (12 * 3)
        var available = Math.Max(
            minimums.Sum(), ExplorerColumnHeaders.ActualWidth - spacingAndPadding);
        var need = Math.Max(0, widths.Sum() + desired - widths[index] - available);

        var neighbors = index switch
        {
            0 => new[] { 1, 2, 3 },
            1 => new[] { 0, 2, 3 },
            2 => new[] { 1, 0, 3 },
            _ => new[] { 2, 1, 0 }
        };

        foreach (var neighbor in neighbors)
        {
            if (need <= 0)
            {
                break;
            }

            var shrink = Math.Min(need,
                Math.Max(0, widths[neighbor] - minimums[neighbor]));

            if (shrink <= 0)
            {
                continue;
            }

            layout.Resize(keys[neighbor], widths[neighbor], -shrink);
            need -= shrink;
        }

        // Si todas las otras columnas llegaron al mínimo, el objetivo
        // también se limita: no se pierden columnas por los bordes.
        var appliedDelta = desired - widths[index] - need;
        if (appliedDelta > 0)
        {
            layout.Resize(column, widths[index], appliedDelta);
        }
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

    private void ExplorerGroupFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string group } ||
            group is not ("none" or "name" or "modified" or "type" or "size"))
        {
            return;
        }

        _groupField = group;
        ApplyFilter();
    }

    private void ExplorerViewModeButton_Click(object sender, RoutedEventArgs e)
    {
        // La vista Detalles está implementada; los demás modos se irán
        // habilitando a medida que estén disponibles.
        ExplorerFilesList.Focus(FocusState.Programmatic);
    }

    private void ExplorerViewDetailsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        SetRightPane(visible: !_detailsVisible || _previewOnly, previewOnly: false);
    }

    private void ExplorerViewPreviewMenuButton_Click(object sender, RoutedEventArgs e)
    {
        SetRightPane(visible: !_detailsVisible || !_previewOnly, previewOnly: true);
    }

    private async void ExplorerViewShowOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string option })
        {
            return;
        }

        switch (option)
        {
            case "navigation":
                _navigationPaneVisible = !_navigationPaneVisible;
                ExplorerLocationsPane.Visibility = _navigationPaneVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                ExplorerLocationsColumn.Width = new GridLength(
                    _navigationPaneVisible ? 185 : 0);
                break;
            case "compact":
                _compactRows = !_compactRows;
                ApplyFilter();
                break;
            case "checkboxes":
                _showItemCheckboxes = !_showItemCheckboxes;
                ExplorerFilesList.IsMultiSelectCheckBoxEnabled = _showItemCheckboxes;
                break;
            case "extensions":
                _showFileExtensions = !_showFileExtensions;
                ApplyFilter();
                break;
            case "hidden":
                _showHiddenItems = !_showHiddenItems;
                UpdateViewMenuChecks();
                await NavigateAsync(_currentLocation ?? ThisPc, HistoryAction.Refresh);
                return;
        }

        UpdateViewMenuChecks();
    }

    private void UpdateViewMenuChecks()
    {
        ExplorerShowNavigationButton.Content = (_navigationPaneVisible ? "✓ " : "") +
            "Panel de navegación";
        ExplorerShowCompactButton.Content = (_compactRows ? "✓ " : "") +
            "Vista compacta";
        ExplorerShowCheckboxesButton.Content = (_showItemCheckboxes ? "✓ " : "") +
            "Casillas de selección";
        ExplorerShowExtensionsButton.Content = (_showFileExtensions ? "✓ " : "") +
            "Extensiones de nombre";
        ExplorerShowHiddenButton.Content = (_showHiddenItems ? "✓ " : "") +
            "Elementos ocultos";

        ExplorerViewDetailsMenuButton.Content = (_detailsVisible && !_previewOnly ? "✓ " : "") +
            "Panel de detalles";
        ExplorerViewPreviewMenuButton.Content = (_detailsVisible && _previewOnly ? "✓ " : "") +
            "Panel de vista previa";
    }

    private void ExplorerSelectAllShortcut_Invoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_visibleItems.Length > 0)
        {
            ExplorerFilesList.SelectAll();
        }
    }

    private void ExplorerSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleItems.Length > 0)
        {
            ExplorerFilesList.SelectAll();
        }
    }

    private void ExplorerSelectNoneButton_Click(object sender, RoutedEventArgs e)
    {
        ExplorerFilesList.SelectedItems.Clear();
    }

    private void ExplorerInvertSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var previouslySelected = ExplorerFilesList.SelectedItems
            .OfType<ExplorerItem>()
            .ToHashSet();

        _selectionBatch = true;
        try
        {
            ExplorerFilesList.SelectedItems.Clear();
            foreach (var item in _visibleItems)
            {
                if (!previouslySelected.Contains(item))
                {
                    ExplorerFilesList.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _selectionBatch = false;
            UpdateSelectionDetails();
        }
    }

    private void ExplorerPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorerFilesList.SelectedItems.Count != 1 ||
            ExplorerFilesList.SelectedItems[0] is not ExplorerItem item)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                Verb = "properties",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV Explorer - propiedades: {ex}");
            ExplorerStatusText.Text =
                "Windows no pudo abrir las propiedades de este elemento.";
        }
    }

    private void ExplorerOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.OpenSettingsFromExplorer();
    }

    private bool CanCutSelection()
    {
        var selected = ExplorerFilesList.SelectedItems.OfType<ExplorerItem>().ToArray();

        if (selected.Length == 0 || selected.Any(item => item.IsDrive))
        {
            return false;
        }

        // Cortar representa mover el archivo; una selección protegida no debe
        // ofrecer operaciones que la modifiquen.
        try
        {
            return selected.All(item => !ProtectedFolderService.IsProtected(
                App.Settings.Current, item.FullPath));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void UpdateCommandBarState()
    {
        var selected = ExplorerFilesList.SelectedItems.OfType<ExplorerItem>().ToArray();
        var canCopy = selected.Length > 0 && selected.All(item => !item.IsDrive);
        var canOpen = selected.Length == 1;
        var canCut = !_isNavigating && CanCutSelection();

        ExplorerToolbarCopyButton.IsEnabled = !_isNavigating && canCopy;
        ExplorerToolbarCutButton.IsEnabled = canCut;
        ExplorerToolbarCopyPathButton.IsEnabled = selected.Length > 0;
        ExplorerToolbarOpenButton.IsEnabled = canOpen;
        ExplorerDetailPropertiesButton.IsEnabled = canOpen;

        ExplorerSelectAllButton.IsEnabled = _visibleItems.Length > 0;
        ExplorerSelectNoneButton.IsEnabled = selected.Length > 0;
        ExplorerInvertSelectionButton.IsEnabled = _visibleItems.Length > 0;
        ExplorerPropertiesButton.IsEnabled = !_isNavigating && selected.Length == 1;

        // Los botones de la BUILD 7B permanecen visibles pero deshabilitados
        // hasta disponer del motor de operaciones, validaciones y deshacer.
        ExplorerToolbarNewButton.IsEnabled = false;
        ExplorerToolbarPasteButton.IsEnabled = false;
        ExplorerToolbarRenameButton.IsEnabled = false;
        ExplorerToolbarShareButton.IsEnabled = false;
        ExplorerToolbarDeleteButton.IsEnabled = false;
        ExplorerToolbarOrganizeButton.IsEnabled = false;
    }

    private async void ExplorerToolbarCopyButton_Click(object sender, RoutedEventArgs e)
    {
        await SendSelectionToClipboardAsync(cut: false);
    }

    private async void ExplorerToolbarCutButton_Click(object sender, RoutedEventArgs e)
    {
        await SendSelectionToClipboardAsync(cut: true);
    }

    private async void ExplorerCopyShortcut_Invoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ExplorerToolbarCopyButton.IsEnabled)
        {
            await SendSelectionToClipboardAsync(cut: false);
        }
    }

    private async void ExplorerCutShortcut_Invoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ExplorerToolbarCutButton.IsEnabled)
        {
            await SendSelectionToClipboardAsync(cut: true);
        }
    }

    private async Task SendSelectionToClipboardAsync(bool cut)
    {
        var selected = ExplorerFilesList.SelectedItems.OfType<ExplorerItem>().ToArray();
        if (_isNavigating || selected.Length == 0 ||
            selected.Any(item => item.IsDrive) ||
            cut && !CanCutSelection())
        {
            return;
        }

        // El portapapeles almacena elementos físicos y la operación solicitada,
        // como el Explorador de Windows. No movemos ni borramos ningún archivo acá.
        try
        {
            var storageItems = new List<IStorageItem>();
            foreach (var item in selected)
            {
                IStorageItem storageItem = item.IsDirectory
                    ? await StorageFolder.GetFolderFromPathAsync(item.FullPath)
                    : await StorageFile.GetFileFromPathAsync(item.FullPath);
                storageItems.Add(storageItem);
            }

            var package = new DataPackage
            {
                RequestedOperation = cut
                    ? DataPackageOperation.Move
                    : DataPackageOperation.Copy
            };
            package.SetStorageItems(storageItems);
            Clipboard.SetContent(package);
            Clipboard.Flush();

            ExplorerStatusText.Text = cut
                ? $"{selected.Length:N0} elemento(s) preparado(s) para mover."
                : $"{selected.Length:N0} elemento(s) copiado(s) al portapapeles.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BandaNV Explorer - no se pudo preparar el portapapeles: {ex}");
            ExplorerStatusText.Text = "No se pudo copiar la selección al portapapeles.";
        }
    }

    private void ExplorerToggleDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        SetRightPane(visible: !_detailsVisible, previewOnly: _previewOnly);
    }

    private void SetRightPane(bool visible, bool previewOnly)
    {
        _detailsVisible = visible;
        _previewOnly = previewOnly;

        ExplorerDetailsPane.Visibility = visible
            ? Visibility.Visible : Visibility.Collapsed;
        ExplorerDetailsDivider.Visibility = visible
            ? Visibility.Visible : Visibility.Collapsed;
        ExplorerDetailsDividerColumn.Width = new GridLength(visible ? 14 : 0);
        ExplorerDetailsColumn.Width = new GridLength(visible ? _detailsWidth : 0);

        var metadataVisibility = previewOnly
            ? Visibility.Collapsed : Visibility.Visible;
        // El modo Vista previa conserva el visor, ocultando los metadatos.
        ExplorerDetailsHeading.Visibility = metadataVisibility;
        ExplorerMetadataGrid.Visibility = metadataVisibility;
        ExplorerDetailShareButton.Visibility = metadataVisibility;
        ExplorerDetailPropertiesButton.Visibility = metadataVisibility;

        UpdateViewMenuChecks();

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

        var shown = _visibleItems.Length;

        var text = ExplorerFilterBox.Text?.Trim();
        ExplorerTotalCountText.Text = string.IsNullOrEmpty(text)
            ? $"{shown:N0} elementos"
            : $"{shown:N0} de {_allItems.Count:N0} elementos";

        if (_truncated)
        {
            ExplorerTotalCountText.Text += " · listado parcial";
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
        if (!_selectionBatch)
        {
            UpdateSelectionDetails();
        }
    }

    private static string FormatSelectedFileSize(double bytes)
    {
        if (bytes <= 0)
        {
            return "0 KB";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        var value = bytes / 1024d;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return value.ToString(value >= 100 ? "N0" : "N1") + " " + units[unit];
    }

    private void UpdateSelectionStatus()
    {
        var selected = ExplorerFilesList.SelectedItems.OfType<ExplorerItem>().ToArray();
        var count = selected.Length;
        var visible = count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ExplorerSelectionDivider.Visibility = visible;
        ExplorerSelectionCountText.Visibility = visible;
        ExplorerSelectionCountText.Text = count == 1
            ? "1 elemento seleccionado"
            : $"{count:N0} elementos seleccionados";

        // Las carpetas no tienen un tamaño disponible en este listado; no
        // recorrerlas para calcularlo porque bloquearía la interfaz.
        var files = selected.Where(item => !item.IsDirectory && item.SizeBytes.HasValue)
            .ToArray();
        var hasSize = files.Length > 0;
        ExplorerSelectionSizeDivider.Visibility =
            hasSize ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSelectionSizeText.Visibility =
            hasSize ? Visibility.Visible : Visibility.Collapsed;

        if (hasSize)
        {
            var sum = files.Sum(item => (double)Math.Max(0L, item.SizeBytes ?? 0));
            ExplorerSelectionSizeText.Text = FormatSelectedFileSize(sum) +
                (files.Length < count ? " (archivos)" : "");
        }
        else
        {
            ExplorerSelectionSizeText.Text = string.Empty;
        }
    }

    private void UpdateSelectionDetails()
    {
        UpdateCommandBarState();
        UpdateSelectionStatus();

        if (ExplorerFilesList.SelectedItems.Count != 1 ||
            ExplorerFilesList.SelectedItems[0] is not ExplorerItem item)
        {
            ExplorerSelectedPreview.Clear();
            ExplorerDetailPropertiesButton.IsEnabled = false;
            ExplorerToolbarOpenButton.IsEnabled = false;

            var count = ExplorerFilesList.SelectedItems.Count;
            ExplorerToolbarCopyPathButton.IsEnabled = count > 0;
            ExplorerDetailName.Text = count > 1
                ? $"{count:N0} elementos seleccionados"
                : "Seleccioná un archivo o carpeta.";

            ExplorerFileIconPanel.Visibility = _detailsVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            ExplorerSelectedFileIcon.FilePath = string.Empty;
            ExplorerSelectedFileIcon.IsDirectory = false;
            ExplorerSelectedFileIcon.IsDrive = false;

            ExplorerDetailsHeading.Visibility = Visibility.Collapsed;
            ExplorerMetadataGrid.Visibility = Visibility.Collapsed;
            ExplorerDetailShareButton.Visibility = Visibility.Collapsed;
            ExplorerDetailPropertiesButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (_detailsVisible)
        {
            ExplorerSelectedPreview.Show(item.FullPath, item.IsDirectory);
        }

        ExplorerDetailPropertiesButton.IsEnabled = true;
        ExplorerToolbarOpenButton.IsEnabled = true;
        ExplorerToolbarCopyPathButton.IsEnabled = true;

        var canPreview = !item.IsDirectory &&
            PreviewExtensions.Contains(Path.GetExtension(item.FullPath));
        ExplorerFileIconPanel.Visibility = _detailsVisible && !canPreview
            ? Visibility.Visible
            : Visibility.Collapsed;

        ExplorerSelectedFileIcon.FilePath = item.FullPath;
        ExplorerSelectedFileIcon.IsDirectory = item.IsDirectory;
        ExplorerSelectedFileIcon.IsDrive = item.IsDrive;

        ExplorerDetailName.Text = item.Name;
        ExplorerDetailType.Text = item.TypeName;
        ExplorerDetailSize.Text = item.IsDirectory ? "—" : item.SizeText;
        ExplorerDetailModified.Text = item.ModifiedText;

        // En el panel de Windows «Ubicación» es la carpeta contenedora.
        ExplorerDetailPath.Text = item.IsDrive
            ? item.FullPath
            : Path.GetDirectoryName(item.FullPath) ?? item.FullPath;

        var metadataVisibility = _detailsVisible && !_previewOnly
            ? Visibility.Visible : Visibility.Collapsed;
        ExplorerDetailsHeading.Visibility = metadataVisibility;
        ExplorerMetadataGrid.Visibility = metadataVisibility;
        ExplorerDetailShareButton.Visibility = metadataVisibility;
        ExplorerDetailPropertiesButton.Visibility = metadataVisibility;
    }

}

/// <summary>
/// Encabezado y elementos para las agrupaciones del explorador.
/// </summary>
public sealed record ExplorerGroup(
    string Name,
    IReadOnlyList<ExplorerItem> Items);
