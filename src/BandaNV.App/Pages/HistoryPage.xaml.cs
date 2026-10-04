using System.Collections.ObjectModel;
using System.Globalization;
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

        LoadPreviewData();
        HistoryList.SelectedIndex = 0;
    }

    private void LoadPreviewData()
    {
        _allPreviewExecutions.Clear();

        _allPreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "04/10/2026 · 00:47:18",
            Type = "ORGANIZAR",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads",
            Destination = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            FileCount = 34,
            FileCountText = "34",
            SizeText = "1.8 GB",
            CanUndo = true,
            UndoBadgeText = "Reversible",
            Files = BuildPrimaryPreviewFiles()
        });

        _allPreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "03/10/2026 · 18:12:42",
            Type = "ORGANIZAR",
            OriginShort = "Escritorio",
            Origin = @"C:\Users\Usuario\Desktop",
            Destination = @"C:\Users\Usuario\Desktop\ORGANIZADO",
            FileCount = 12,
            FileCountText = "12",
            SizeText = "420 MB",
            CanUndo = false,
            UndoBadgeText = "Ya deshecha",
            Files =
            [
                new HistoryFilePreview("brief.pdf", "DOCUMENTS", "3.8 MB"),
                new HistoryFilePreview("referencia.png", "IMAGES", "7.6 MB"),
                new HistoryFilePreview("entrega.zip", "RAR", "386 MB"),
                new HistoryFilePreview("audio.wav", "AUDIO", "22.6 MB")
            ]
        });

        _allPreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "02/10/2026 · 23:08:07",
            Type = "ORGANIZAR",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads",
            Destination = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            FileCount = 57,
            FileCountText = "57",
            SizeText = "3.1 GB",
            CanUndo = false,
            UndoBadgeText = "No reversible",
            Files =
            [
                new HistoryFilePreview("captura_01.png", "IMAGES", "5.1 MB"),
                new HistoryFilePreview("materiales.7z", "RAR", "1.7 GB"),
                new HistoryFilePreview("clase.mp4", "VIDEOS", "884 MB"),
                new HistoryFilePreview("fuentes.zip", "RAR", "118 MB"),
                new HistoryFilePreview("documentacion.pdf", "DOCUMENTS", "11.4 MB")
            ]
        });

        _allPreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "01/10/2026 · 14:36:55",
            Type = "DESHACER",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            Destination = @"C:\Users\Usuario\Downloads",
            FileCount = 12,
            FileCountText = "12",
            SizeText = "420 MB",
            CanUndo = false,
            UndoBadgeText = "Registro Undo",
            Files =
            [
                new HistoryFilePreview("brief.pdf", "DOCUMENTS", "3.8 MB"),
                new HistoryFilePreview("referencia.png", "IMAGES", "7.6 MB"),
                new HistoryFilePreview("entrega.zip", "RAR", "386 MB"),
                new HistoryFilePreview("audio.wav", "AUDIO", "22.6 MB")
            ]
        });

        RefreshHistoryResults();
    }

    private static IReadOnlyList<HistoryFilePreview> BuildPrimaryPreviewFiles()
    {
        var files = new List<HistoryFilePreview>
        {
            new("foto_rolling_01.jpg", "IMAGES", "14.2 MB"),
            new("TP_final.pdf", "DOCUMENTS", "8.1 MB"),
            new("pack_autos.rar", "RAR", "1.2 GB"),
            new("video_final.mp4", "VIDEOS", "542 MB"),
            new("logo_nako.ai", "DESIGN", "35 MB"),
            new("referencia_01.png", "IMAGES", "6.4 MB"),
            new("referencia_02.webp", "IMAGES", "3.8 MB"),
            new("presupuesto.xlsx", "DOCUMENTS", "182 KB"),
            new("brief_cliente.docx", "DOCUMENTS", "1.1 MB"),
            new("tipografia.otf", "FONTS", "624 KB"),
            new("musica_demo.mp3", "AUDIO", "9.7 MB"),
            new("captura.gif", "GIF", "4.3 MB"),
            new("setup_herramienta.exe", "INSTALLERS", "118 MB"),
            new("recursos.7z", "RAR", "286 MB")
        };

        for (var index = files.Count + 1; index <= 34; index++)
        {
            files.Add(new HistoryFilePreview(
                $"archivo_{index:00}.png",
                "IMAGES",
                $"{2 + (index % 8)}.{index % 10} MB"));
        }

        return files;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryExecutionPreview execution)
        {
            ShowExecutionDetails(execution);
        }
    }

    private void ShowExecutionDetails(HistoryExecutionPreview execution)
    {
        DetailDateText.Text = $"{execution.DateTimeText} · {execution.Type}";
        DetailFileCountText.Text = execution.FileCountText;
        DetailSizeText.Text = execution.SizeText;
        DetailOriginText.Text = execution.Origin;
        DetailDestinationText.Text = execution.Destination;
        UndoStatusText.Text = execution.UndoBadgeText;
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

    private static void ApplyFileHistoryState(HistoryFilePreview file, bool executionCanUndo)
    {
        file.CanDelete = executionCanUndo && !file.IsDeleted;
        file.DeleteVisibility = file.CanDelete
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            "Origin" => HistorySortMode.Origin,
            _ => HistorySortMode.Newest
        };

        HistorySortValueText.Text = GetHistorySortDisplayName(_sortMode);
        HistorySortFlyout.Hide();
        RefreshHistoryResults();
    }

    private void RefreshHistoryResults()
    {
        var selectedExecution = HistoryList.SelectedItem as HistoryExecutionPreview;
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
            HistorySortMode.Origin =>
                query.OrderBy(execution => execution.OriginShort, StringComparer.CurrentCultureIgnoreCase)
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

        if (selectedExecution is not null && results.Contains(selectedExecution))
        {
            HistoryList.SelectedItem = selectedExecution;
        }
        else if (results.Count > 0)
        {
            HistoryList.SelectedIndex = 0;
        }
        else
        {
            SelectedFiles.Clear();
            ClearExecutionDetails();
        }

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
        DetailDateText.Text = "—";
        DetailFileCountText.Text = "—";
        DetailSizeText.Text = "—";
        DetailOriginText.Text = "—";
        DetailDestinationText.Text = "—";
        UndoStatusText.Text = "Sin selección";
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
            HistorySortMode.Origin => "Origen · A–Z",
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
        if (sender is not Button { Tag: string fileName } ||
            HistoryList.SelectedItem is not HistoryExecutionPreview { CanUndo: true } execution)
        {
            return;
        }

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
        if (HistoryList.SelectedItem is not HistoryExecutionPreview { CanUndo: true } execution)
        {
            return;
        }

        var deletedCount = execution.Files.Count(file => file.IsDeleted);
        var recoverableCount = Math.Max(0, execution.FileCount - deletedCount);

        var detail = deletedCount > 0
            ? $"En esta vista previa, {recoverableCount} archivos siguen siendo recuperables y {deletedCount} quedan fuera del Undo porque fueron eliminados después. El historial conserva igualmente sus registros tachados."
            : "El botón ya muestra cuándo una ejecución es reversible. La operación real se conectará cuando migremos el motor de logs y Undo.";

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
    SizeAscending,
    Origin
}

public sealed class HistoryExecutionPreview
{
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
