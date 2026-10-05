using System.Collections.ObjectModel;
using System.Globalization;
using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class HistoryPage : Page
{
    public ObservableCollection<HistoryExecutionPreview> PreviewExecutions { get; } = new();
    public ObservableCollection<HistoryFilePreview> SelectedFiles { get; } = new();

    private readonly List<HistoryExecutionPreview> _allPreviewExecutions = new();

    private HistoryTypeFilter _typeFilter = HistoryTypeFilter.All;
    private HistoryUndoFilter _undoFilter = HistoryUndoFilter.All;
    private string? _originFilter;
    private HistorySortMode _sortMode = HistorySortMode.Newest;

    private HistoryTypeFilter _pendingTypeFilter = HistoryTypeFilter.All;
    private HistoryUndoFilter _pendingUndoFilter = HistoryUndoFilter.All;
    private string? _pendingOriginFilter;

    private HistoryFilePreview? _pendingDeleteFile;
    private HistoryExecutionPreview? _pendingDeleteExecution;

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HistoryPage_Loaded;
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        _allPreviewExecutions.Clear();

        try
        {
            var records =
                await global::BandaNV.App.App.History.LoadAsync();

            foreach (var record in records)
            {
                var movedItems = record.Items
                    .Where(item =>
                        item.Status == OrganizationExecutionItemStatus.Moved)
                    .ToList();

                var canUndo =
                    global::BandaNV.App.App.Settings.Current.UndoEnabled &&
                    movedItems.Count > 0 &&
                    movedItems.All(IsItemCurrentlyReversible);

                var files = movedItems
                    .Select(item => new HistoryFilePreview(
                        item.FileName,
                        item.CategoryName ?? "Sin categoría",
                        FormatHistoryBytes(item.SizeBytes)))
                    .ToList();

                var type = record.Type.Equals(
                        "UNDO",
                        StringComparison.OrdinalIgnoreCase)
                    ? "DESHACER"
                    : "ORGANIZAR";

                _allPreviewExecutions.Add(new HistoryExecutionPreview
                {
                    ExecutionId = record.ExecutionId,
                    DateTimeText =
                        record.StartedAt.ToString(
                            "dd/MM/yyyy · HH:mm:ss",
                            CultureInfo.GetCultureInfo("es-AR")),
                    Type = type,
                    OriginShort = GetFolderDisplayName(record.SourceFolder),
                    Origin = record.SourceFolder,
                    Destination = record.DestinationFolder,
                    FileCount = movedItems.Count,
                    FileCountText =
                        movedItems.Count.ToString(CultureInfo.CurrentCulture),
                    SizeText = FormatHistoryBytes(
                        movedItems.Sum(item => item.SizeBytes)),
                    CanUndo = canUndo,
                    UndoBadgeText = type == "DESHACER"
                        ? "Registro Undo"
                        : canUndo
                            ? "Reversible"
                            : movedItems.Count == 0
                                ? "Sin movimientos"
                                : "No reversible",
                    Files = files
                });
            }
        }
        catch
        {
            // Historial queda vacío si no puede leerse; nunca se rellenan mocks.
        }

        RefreshHistoryResults();
    }

    private static bool IsItemCurrentlyReversible(
        OrganizationExecutionItemRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.FinalPath))
        {
            return false;
        }

        try
        {
            return File.Exists(item.FinalPath) &&
                   !File.Exists(item.OriginalPath);
        }
        catch
        {
            return false;
        }
    }

    private static string GetFolderDisplayName(string path)
    {
        var normalized = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        var name = Path.GetFileName(normalized);

        return string.IsNullOrWhiteSpace(name)
            ? path
            : name;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHistorySelectionDetails();
    }

    private void HistoryList_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;

        while (current is not null && current != HistoryList)
        {
            if (current is ListViewItem)
            {
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        HistoryList.SelectedItems.Clear();
        UpdateHistorySelectionDetails();
    }

    private void HistorySelectAllAccelerator_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var execution in PreviewExecutions)
        {
            if (!HistoryList.SelectedItems.Contains(execution))
            {
                HistoryList.SelectedItems.Add(execution);
            }
        }

        UpdateHistorySelectionDetails();
        args.Handled = true;
    }

    private List<HistoryExecutionPreview> GetSelectedHistoryExecutions() =>
        HistoryList.SelectedItems
            .OfType<HistoryExecutionPreview>()
            .ToList();

    private void UpdateHistorySelectionDetails()
    {
        var executions = GetSelectedHistoryExecutions();

        if (executions.Count == 0)
        {
            SelectedFiles.Clear();
            ClearExecutionDetails();
            return;
        }

        if (executions.Count == 1)
        {
            ShowExecutionDetails(executions[0]);
            return;
        }

        ShowMultipleExecutionDetails(executions);
    }

    private void ShowExecutionDetails(HistoryExecutionPreview execution)
    {
        DetailTitleText.Text = "Detalle de ejecución";
        DetailFilesSectionTitleText.Text = "ARCHIVOS DE LA EJECUCIÓN";
        DetailDateText.Text = $"{execution.DateTimeText} · {execution.Type}";
        DetailFileCountText.Text = execution.FileCountText;
        DetailSizeText.Text = execution.SizeText;
        DetailOriginText.Text = execution.Origin;
        DetailDestinationText.Text = execution.Destination;
        UndoStatusText.Text = execution.UndoBadgeText;
        UndoPreviewButton.Content = "Deshacer ejecución";
        UndoPreviewButton.IsEnabled = execution.CanUndo;

        var activeBackground = (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
        var inactiveBackground = (Brush)Application.Current.Resources["BandaNavIconBrush"];
        var activeForeground = (Brush)Application.Current.Resources["BandaAccentBrush"];
        var inactiveForeground = (Brush)Application.Current.Resources["BandaMutedStrongBrush"];

        UndoStatusBorder.Background = execution.CanUndo ? activeBackground : inactiveBackground;
        UndoStatusText.Foreground = execution.CanUndo ? activeForeground : inactiveForeground;

        SelectedFiles.Clear();
        foreach (var file in execution.Files)
        {
            ApplyFileHistoryState(file, execution.CanUndo);
            SelectedFiles.Add(file);
        }
    }

    private void ShowMultipleExecutionDetails(IReadOnlyList<HistoryExecutionPreview> executions)
    {
        var totalFiles = executions.Sum(execution => execution.FileCount);
        var totalBytes = executions.Sum(execution => ParseSizeBytes(execution.SizeText));

        var originCount = executions
            .Select(execution => execution.Origin)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();

        var destinationCount = executions
            .Select(execution => execution.Destination)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();

        var reversibleCount = executions.Count(execution => execution.CanUndo);

        DetailTitleText.Text = "Selección múltiple";
        DetailFilesSectionTitleText.Text = "ARCHIVOS DE LAS EJECUCIONES";
        DetailDateText.Text = $"{executions.Count} ejecuciones seleccionadas";
        DetailFileCountText.Text = totalFiles.ToString(CultureInfo.CurrentCulture);
        DetailSizeText.Text = FormatHistoryBytes(totalBytes);
        DetailOriginText.Text =
            originCount == 1
                ? executions[0].Origin
                : $"{originCount} orígenes";
        DetailDestinationText.Text =
            destinationCount == 1
                ? executions[0].Destination
                : $"{destinationCount} destinos";

        UndoStatusText.Text =
            reversibleCount == 0
                ? "Sin ejecuciones reversibles"
                : $"{reversibleCount} reversible{(reversibleCount == 1 ? string.Empty : "s")}";

        UndoStatusBorder.Background =
            (Brush)Application.Current.Resources[
                reversibleCount > 0 ? "BandaAccentSoftBrush" : "BandaNavIconBrush"];
        UndoStatusText.Foreground =
            (Brush)Application.Current.Resources[
                reversibleCount > 0 ? "BandaAccentBrush" : "BandaMutedStrongBrush"];

        UndoPreviewButton.Content = "Deshacer ejecución";
        UndoPreviewButton.IsEnabled = false;

        SelectedFiles.Clear();
        foreach (var execution in executions)
        {
            foreach (var file in execution.Files)
            {
                // En selección múltiple los archivos son informativos;
                // las acciones individuales permanecen desactivadas.
                file.CanDelete = false;
                file.DeleteVisibility = Visibility.Collapsed;
                file.DeletedStatusVisibility =
                    file.IsDeleted ? Visibility.Visible : Visibility.Collapsed;
                file.NormalNameVisibility =
                    file.IsDeleted ? Visibility.Collapsed : Visibility.Visible;
                file.DeletedNameVisibility =
                    file.IsDeleted ? Visibility.Visible : Visibility.Collapsed;
                file.RowOpacity = file.IsDeleted ? 0.58 : 1.0;

                SelectedFiles.Add(file);
            }
        }
    }

    private static string FormatHistoryBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static void ApplyFileHistoryState(HistoryFilePreview file, bool executionCanUndo)
    {
        // El historial ya usa datos reales. La eliminación individual desde
        // esta vista se habilitará junto al motor de acciones de Historial.
        file.CanDelete = false;
        file.DeleteVisibility = Visibility.Collapsed;
        file.DeletedStatusVisibility = file.IsDeleted
            ? Visibility.Visible
            : Visibility.Collapsed;
        file.NormalNameVisibility = file.IsDeleted
            ? Visibility.Collapsed
            : Visibility.Visible;
        file.DeletedNameVisibility = file.IsDeleted
            ? Visibility.Visible
            : Visibility.Collapsed;
        file.RowOpacity = file.IsDeleted ? 0.58 : 1.0;
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshHistoryResults();
    }

    private void HistoryFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingTypeFilter = _typeFilter;
        _pendingUndoFilter = _undoFilter;
        _pendingOriginFilter = _originFilter;

        UpdatePendingHistoryFilterLabels();
        BuildHistoryOriginOptions();
        HistoryFiltersOverlay.Visibility = Visibility.Visible;
    }

    private void CloseHistoryFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        CloseHistoryFiltersOverlay();
    }

    private void HistoryFiltersBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseHistoryFiltersOverlay();
    }

    private void ResetHistoryFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingTypeFilter = HistoryTypeFilter.All;
        _pendingUndoFilter = HistoryUndoFilter.All;
        _pendingOriginFilter = null;

        UpdatePendingHistoryFilterLabels();
        BuildHistoryOriginOptions();
    }

    private void ApplyHistoryFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _typeFilter = _pendingTypeFilter;
        _undoFilter = _pendingUndoFilter;
        _originFilter = _pendingOriginFilter;

        CloseHistoryFiltersOverlay();
        RefreshHistoryResults();
    }

    private void HistoryTypeOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } ||
            !Enum.TryParse<HistoryTypeFilter>(key, out var parsed))
        {
            return;
        }

        _pendingTypeFilter = parsed;
        HistoryTypeValueText.Text = GetHistoryTypeFilterDisplayName(parsed);
        HistoryTypeFlyout.Hide();
    }

    private void HistoryUndoOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } ||
            !Enum.TryParse<HistoryUndoFilter>(key, out var parsed))
        {
            return;
        }

        _pendingUndoFilter = parsed;
        HistoryUndoValueText.Text = GetHistoryUndoFilterDisplayName(parsed);
        HistoryUndoFlyout.Hide();
    }

    private void BuildHistoryOriginOptions()
    {
        HistoryOriginOptionsPanel.Children.Clear();

        HistoryOriginOptionsPanel.Children.Add(new TextBlock
        {
            Text = "ORIGEN",
            Margin = new Thickness(10, 6, 10, 4),
            Foreground = (Brush)Application.Current.Resources["BandaMutedStrongBrush"],
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var origins = _allPreviewExecutions
            .Select(execution => execution.OriginShort)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(origin => origin, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AddHistoryOriginOption("All", "Todos los orígenes", string.IsNullOrWhiteSpace(_pendingOriginFilter));

        foreach (var origin in origins)
        {
            AddHistoryOriginOption(
                origin,
                origin,
                string.Equals(
                    origin,
                    _pendingOriginFilter,
                    StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private void AddHistoryOriginOption(string key, string label, bool isSelected)
    {
        var button = new Button
        {
            Tag = key,
            Content = label,
            Style = (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
        };

        if (isSelected)
        {
            button.Background =
                (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
            button.Foreground =
                (Brush)Application.Current.Resources["BandaAccentBrush"];
        }

        button.Click += HistoryOriginOptionButton_Click;
        HistoryOriginOptionsPanel.Children.Add(button);
    }

    private void HistoryOriginOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        _pendingOriginFilter =
            key.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? null
                : key;

        HistoryOriginValueText.Text =
            string.IsNullOrWhiteSpace(_pendingOriginFilter)
                ? "Todos los orígenes"
                : _pendingOriginFilter;

        BuildHistoryOriginOptions();
        HistoryOriginFlyout.Hide();
    }

    private void UpdatePendingHistoryFilterLabels()
    {
        HistoryTypeValueText.Text =
            GetHistoryTypeFilterDisplayName(_pendingTypeFilter);
        HistoryUndoValueText.Text =
            GetHistoryUndoFilterDisplayName(_pendingUndoFilter);
        HistoryOriginValueText.Text =
            string.IsNullOrWhiteSpace(_pendingOriginFilter)
                ? "Todos los orígenes"
                : _pendingOriginFilter;
    }

    private static string GetHistoryTypeFilterDisplayName(HistoryTypeFilter filter) =>
        filter switch
        {
            HistoryTypeFilter.Organize => "Organizar",
            HistoryTypeFilter.Undo => "Deshacer",
            _ => "Todos los tipos"
        };

    private static string GetHistoryUndoFilterDisplayName(HistoryUndoFilter filter) =>
        filter switch
        {
            HistoryUndoFilter.Reversible => "Reversibles",
            HistoryUndoFilter.NotReversible => "No reversibles",
            _ => "Cualquier estado"
        };

    private void CloseHistoryFiltersOverlay()
    {
        HistoryTypeFlyout.Hide();
        HistoryUndoFlyout.Hide();
        HistoryOriginFlyout.Hide();
        HistoryFiltersOverlay.Visibility = Visibility.Collapsed;
    }

    private void HistoryClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _typeFilter = HistoryTypeFilter.All;
        _undoFilter = HistoryUndoFilter.All;
        _originFilter = null;

        if (!string.IsNullOrEmpty(HistorySearchBox.Text))
        {
            HistorySearchBox.Text = string.Empty;
        }
        else
        {
            RefreshHistoryResults();
        }
    }

    private void HistorySortOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sortKey })
        {
            return;
        }

        _sortMode = sortKey switch
        {
            "Oldest" => HistorySortMode.Oldest,
            "FilesDescending" => HistorySortMode.FilesDescending,
            "FilesAscending" => HistorySortMode.FilesAscending,
            "SizeDescending" => HistorySortMode.SizeDescending,
            "SizeAscending" => HistorySortMode.SizeAscending,
            _ => HistorySortMode.Newest
        };

        HistorySortValueText.Text = GetHistorySortDisplayName(_sortMode);
        HistorySortFlyout.Hide();
        RefreshHistoryResults();
    }

    private void RefreshHistoryResults()
    {
        var selectedExecutions = HistoryList.SelectedItems
            .OfType<HistoryExecutionPreview>()
            .ToList();

        IEnumerable<HistoryExecutionPreview> query = _allPreviewExecutions;

        var searchText = HistorySearchBox?.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(execution =>
                execution.DateTimeText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                execution.Type.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                execution.OriginShort.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                execution.Origin.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                execution.Destination.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                execution.Files.Any(file =>
                    file.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                    file.Category.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)));
        }

        query = _typeFilter switch
        {
            HistoryTypeFilter.Organize =>
                query.Where(execution =>
                    execution.Type.Equals("ORGANIZAR", StringComparison.OrdinalIgnoreCase)),
            HistoryTypeFilter.Undo =>
                query.Where(execution =>
                    execution.Type.Equals("DESHACER", StringComparison.OrdinalIgnoreCase)),
            _ => query
        };

        query = _undoFilter switch
        {
            HistoryUndoFilter.Reversible =>
                query.Where(execution => execution.CanUndo),
            HistoryUndoFilter.NotReversible =>
                query.Where(execution => !execution.CanUndo),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(_originFilter))
        {
            query = query.Where(execution =>
                execution.OriginShort.Equals(
                    _originFilter,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        var results = _sortMode switch
        {
            HistorySortMode.Oldest =>
                query.OrderBy(GetExecutionDateTime).ToList(),
            HistorySortMode.FilesDescending =>
                query.OrderByDescending(execution => execution.FileCount)
                    .ThenByDescending(GetExecutionDateTime)
                    .ToList(),
            HistorySortMode.FilesAscending =>
                query.OrderBy(execution => execution.FileCount)
                    .ThenByDescending(GetExecutionDateTime)
                    .ToList(),
            HistorySortMode.SizeDescending =>
                query.OrderByDescending(execution => ParseSizeBytes(execution.SizeText))
                    .ThenByDescending(GetExecutionDateTime)
                    .ToList(),
            HistorySortMode.SizeAscending =>
                query.OrderBy(execution => ParseSizeBytes(execution.SizeText))
                    .ThenByDescending(GetExecutionDateTime)
                    .ToList(),
            _ =>
                query.OrderByDescending(GetExecutionDateTime).ToList()
        };

        PreviewExecutions.Clear();
        foreach (var execution in results)
        {
            PreviewExecutions.Add(execution);
        }

        foreach (var execution in selectedExecutions.Where(results.Contains))
        {
            HistoryList.SelectedItems.Add(execution);
        }

        UpdateHistorySelectionDetails();
        UpdateHistoryToolState(results.Count, searchText);
    }

    private void UpdateHistoryToolState(int resultCount, string searchText)
    {
        HistoryResultCountText.Text = resultCount.ToString(CultureInfo.CurrentCulture);
        HistoryResultCountLabel.Text = resultCount == 1 ? "ejecución" : "ejecuciones";
        HistoryFooterCountText.Text =
            resultCount == 1 ? "1 resultado" : $"{resultCount} resultados";

        HistorySortValueText.Text = GetHistorySortDisplayName(_sortMode);
        HistoryActiveSortText.Text = GetHistorySortDisplayName(_sortMode);

        var filterParts = new List<string>();

        if (_typeFilter != HistoryTypeFilter.All)
        {
            filterParts.Add(GetHistoryTypeFilterDisplayName(_typeFilter));
        }

        if (_undoFilter != HistoryUndoFilter.All)
        {
            filterParts.Add(GetHistoryUndoFilterDisplayName(_undoFilter));
        }

        if (!string.IsNullOrWhiteSpace(_originFilter))
        {
            filterParts.Add(_originFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filterParts.Add("búsqueda activa");
        }

        var advancedFilterCount =
            (_typeFilter != HistoryTypeFilter.All ? 1 : 0) +
            (_undoFilter != HistoryUndoFilter.All ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(_originFilter) ? 1 : 0);

        var hasAdvancedFilters = advancedFilterCount > 0;
        var hasAnyFilter = hasAdvancedFilters || !string.IsNullOrWhiteSpace(searchText);

        HistoryAllChip.Visibility =
            hasAnyFilter ? Visibility.Collapsed : Visibility.Visible;

        HistoryActiveFilterText.Text =
            filterParts.Count == 0
                ? "Sin filtros"
                : string.Join(" · ", filterParts);

        HistoryFiltersButton.Content =
            hasAdvancedFilters
                ? $"Filtros ({advancedFilterCount})"
                : "Filtros";

        HistoryFiltersButton.Background =
            (Brush)Application.Current.Resources[
                hasAdvancedFilters ? "BandaAccentSoftBrush" : "BandaCardBrush"];

        HistoryFiltersButton.Foreground =
            (Brush)Application.Current.Resources[
                hasAdvancedFilters ? "BandaAccentBrush" : "BandaTextBrush"];

        HistoryClearFiltersButton.IsEnabled = hasAnyFilter;
    }

    private void ClearExecutionDetails()
    {
        DetailTitleText.Text = "Detalle de ejecución";
        DetailFilesSectionTitleText.Text = "ARCHIVOS DE LA EJECUCIÓN";
        DetailDateText.Text = "—";
        DetailFileCountText.Text = "—";
        DetailSizeText.Text = "—";
        DetailOriginText.Text = "—";
        DetailDestinationText.Text = "—";
        UndoStatusText.Text = "Sin selección";
        UndoPreviewButton.Content = "Deshacer ejecución";
        UndoPreviewButton.IsEnabled = false;

        UndoStatusBorder.Background =
            (Brush)Application.Current.Resources["BandaNavIconBrush"];
        UndoStatusText.Foreground =
            (Brush)Application.Current.Resources["BandaMutedStrongBrush"];
    }

    private static string GetHistorySortDisplayName(HistorySortMode sortMode) =>
        sortMode switch
        {
            HistorySortMode.Oldest => "Fecha · más antigua",
            HistorySortMode.FilesDescending => "Archivos · mayor primero",
            HistorySortMode.FilesAscending => "Archivos · menor primero",
            HistorySortMode.SizeDescending => "Tamaño · mayor primero",
            HistorySortMode.SizeAscending => "Tamaño · menor primero",
            _ => "Fecha · más reciente"
        };

    private static DateTime GetExecutionDateTime(HistoryExecutionPreview execution)
    {
        return DateTime.TryParseExact(
            execution.DateTimeText,
            "dd/MM/yyyy · HH:mm:ss",
            CultureInfo.GetCultureInfo("es-AR"),
            DateTimeStyles.None,
            out var parsed)
                ? parsed
                : DateTime.MinValue;
    }

    private static double ParseSizeBytes(string sizeText)
    {
        var parts = sizeText.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2 ||
            !double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return 0;
        }

        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "TB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return value * multiplier;
    }

    private void DeleteFilePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var executions = GetSelectedHistoryExecutions();

        if (sender is not Button { Tag: string fileName } ||
            executions.Count != 1 ||
            !executions[0].CanUndo)
        {
            return;
        }

        var execution = executions[0];

        var file = execution.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, fileName, StringComparison.Ordinal));

        if (file is null || file.IsDeleted)
        {
            return;
        }

        _pendingDeleteFile = file;
        _pendingDeleteExecution = execution;

        HistoryModalTitleText.Text = "Eliminar archivo";
        HistoryModalBodyText.Text =
            $"¿Eliminar \"{file.Name}\"? En esta maqueta no se modifica ningún archivo real: " +
            "se simula el resultado para definir cómo queda registrado en Historial.";

        HistoryModalIconText.Text = "!";
        HistoryModalIconBorder.Background =
            (Brush)Application.Current.Resources["BandaDangerSoftBrush"];
        HistoryModalIconText.Foreground =
            (Brush)Application.Current.Resources["BandaDangerBrush"];

        HistoryModalSecondaryButton.Content = "Cancelar";
        HistoryModalPrimaryButton.Content = "Eliminar";
        HistoryModalPrimaryButton.Visibility = Visibility.Visible;
        HistoryModalOverlay.Visibility = Visibility.Visible;
    }

    private void UndoPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var executions = GetSelectedHistoryExecutions();

        if (executions.Count != 1 ||
            !executions[0].CanUndo)
        {
            return;
        }

        var execution = executions[0];

        var deletedCount = execution.Files.Count(file => file.IsDeleted);
        var recoverableCount = Math.Max(0, execution.FileCount - deletedCount);

        var detail = deletedCount > 0
            ? $"En esta vista previa, {recoverableCount} archivos siguen siendo recuperables y {deletedCount} quedan fuera del Undo porque fueron eliminados después. El historial conserva igualmente sus registros tachados."
            : "Esta ejecución real sigue siendo reversible. La restauración física se conectará en el próximo bloque de Undo.";

        _pendingDeleteFile = null;
        _pendingDeleteExecution = null;

        HistoryModalTitleText.Text = "Vista previa de Undo";
        HistoryModalBodyText.Text = detail;
        HistoryModalIconText.Text = "↶";
        HistoryModalIconBorder.Background =
            (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
        HistoryModalIconText.Foreground =
            (Brush)Application.Current.Resources["BandaAccentBrush"];

        HistoryModalSecondaryButton.Content = "Entendido";
        HistoryModalPrimaryButton.Visibility = Visibility.Collapsed;
        HistoryModalOverlay.Visibility = Visibility.Visible;
    }

    private void HistoryModalPrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteFile is not { } file ||
            _pendingDeleteExecution is not { } execution ||
            file.IsDeleted)
        {
            CloseHistoryModal();
            return;
        }

        file.IsDeleted = true;
        ApplyFileHistoryState(file, execution.CanUndo);

        var index = SelectedFiles.IndexOf(file);
        if (index >= 0)
        {
            SelectedFiles.RemoveAt(index);
            SelectedFiles.Insert(index, file);
        }

        CloseHistoryModal();
    }

    private void HistoryModalCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseHistoryModal();
    }

    private void HistoryModalBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseHistoryModal();
    }

    private void CloseHistoryModal()
    {
        HistoryModalOverlay.Visibility = Visibility.Collapsed;
        _pendingDeleteFile = null;
        _pendingDeleteExecution = null;
    }

}

public enum HistoryTypeFilter
{
    All,
    Organize,
    Undo
}

public enum HistoryUndoFilter
{
    All,
    Reversible,
    NotReversible
}

public enum HistorySortMode
{
    Newest,
    Oldest,
    FilesDescending,
    FilesAscending,
    SizeDescending,
    SizeAscending
}

public sealed class HistoryExecutionPreview
{
    public string ExecutionId { get; set; } = string.Empty;
    public string DateTimeText { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OriginShort { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;

    public string DestinationShort
    {
        get
        {
            var normalized = Destination.TrimEnd('\\', '/');
            var folderName = System.IO.Path.GetFileName(normalized);

            return string.IsNullOrWhiteSpace(folderName)
                ? Destination
                : folderName;
        }
    }

    public int FileCount { get; set; }
    public string FileCountText { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public bool CanUndo { get; set; }
    public string UndoBadgeText { get; set; } = string.Empty;
    public IReadOnlyList<HistoryFilePreview> Files { get; set; } = [];
}

public sealed class HistoryFilePreview
{
    public HistoryFilePreview()
    {
    }

    public HistoryFilePreview(string name, string category, string sizeText)
    {
        Name = name;
        Category = category;
        SizeText = sizeText;
    }

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public bool CanDelete { get; set; }
    public double RowOpacity { get; set; } = 1.0;
    public Visibility DeleteVisibility { get; set; } = Visibility.Collapsed;
    public Visibility DeletedStatusVisibility { get; set; } = Visibility.Collapsed;
    public Visibility NormalNameVisibility { get; set; } = Visibility.Visible;
    public Visibility DeletedNameVisibility { get; set; } = Visibility.Collapsed;
}
