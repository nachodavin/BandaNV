using BandaNV.Core.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;

namespace BandaNV.App.Pages;

public sealed partial class SearchPage : Page
{
    private const int CategoriesPerPage = 10;
    private const double DateWheelItemHeight = 36d;

    private readonly List<SearchCategorySummary> _allCategoryCards = new();
    private readonly List<SearchFileResult> _allFiles = new();

    public ObservableCollection<SearchFileResult> VisibleSearchResults { get; } = new();

    private SearchFileResult? _selectedSearchFile;
    private readonly List<SearchFileResult> _managedSearchFiles = new();
    private SearchManageMode _searchManageMode = SearchManageMode.None;
    private string? _pendingSearchCategoryName;

    private readonly HashSet<string> _selectedCategoryNames =
        new(StringComparer.CurrentCultureIgnoreCase);

    private int _currentCategoryPage;
    private SearchDateFilter _dateFilter = SearchDateFilter.All;
    private DateTime? _specificDateFilter;
    private SearchSizeFilter _sizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _extensionFilters =
        new(StringComparer.OrdinalIgnoreCase);
    private SearchSortMode _sortMode = SearchSortMode.Newest;

    private SearchDateFilter _pendingDateFilter = SearchDateFilter.All;
    private DateTime? _pendingSpecificDateFilter;
    private SearchSizeFilter _pendingSizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _pendingExtensionFilters =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<int> _dateWheelYears = new();
    private bool _isUpdatingDateWheels;
    private int _wheelDay = 1;
    private int _wheelMonth = 1;
    private int _wheelYear = DateTime.Today.Year;

    public SearchPage()
    {
        InitializeComponent();
        InitializeSpecificDateWheels();

        // Datos de maqueta hasta conectar el índice real de archivos.
        // La interacción de filtros ya queda preparada para reutilizarse con datos reales.
        _allFiles.AddRange(BuildPreviewFiles());
        LoadCategories(GetPreviewCategories());
        RefreshSearchResults();
    }

    public void LoadCategories(IEnumerable<CategoryDefinition> categories)
    {
        _allCategoryCards.Clear();

        var orderedCategories = categories
            .OrderBy(category => category.Order)
            .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var validCategoryNames = orderedCategories
            .Select(category => category.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        _selectedCategoryNames.RemoveWhere(name => !validCategoryNames.Contains(name));

        foreach (var category in orderedCategories)
        {
            var fileCount = _allFiles.Count(file =>
                file.Category.Equals(category.Name, StringComparison.CurrentCultureIgnoreCase));

            _allCategoryCards.Add(new SearchCategorySummary(
                category.Name,
                category.Order,
                BuildExtensionsText(category.Extensions),
                fileCount.ToString(CultureInfo.CurrentCulture),
                _selectedCategoryNames.Contains(category.Name)));
        }

        _currentCategoryPage = Math.Min(_currentCategoryPage, CategoryPageCount - 1);
        UpdateCategoryPage();
    }

    private int CategoryPageCount =>
        Math.Max(1, (int)Math.Ceiling(_allCategoryCards.Count / (double)CategoriesPerPage));

    private void UpdateCategoryPage()
    {
        CategoryCardsGrid.Children.Clear();
        CategoryCardsGrid.ColumnDefinitions.Clear();

        for (var column = 0; column < CategoriesPerPage; column++)
        {
            CategoryCardsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        }

        foreach (var category in _allCategoryCards)
        {
            category.IsSelected = _selectedCategoryNames.Contains(category.Name);
        }

        var start = _currentCategoryPage * CategoriesPerPage;
        var visibleCategories = _allCategoryCards
            .Skip(start)
            .Take(CategoriesPerPage)
            .ToList();

        var template = (DataTemplate)Resources["CategoryCardTemplate"];

        for (var index = 0; index < visibleCategories.Count; index++)
        {
            var presenter = new ContentControl
            {
                Content = visibleCategories[index],
                ContentTemplate = template,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            Grid.SetColumn(presenter, index);
            CategoryCardsGrid.Children.Add(presenter);
        }

        var hasMultiplePages = CategoryPageCount > 1;
        var pagerVisibility = hasMultiplePages ? Visibility.Visible : Visibility.Collapsed;

        PreviousCategoriesButton.Visibility = pagerVisibility;
        NextCategoriesButton.Visibility = pagerVisibility;
        CategoryPageDots.Visibility = pagerVisibility;

        PreviousCategoriesButton.IsEnabled = hasMultiplePages && _currentCategoryPage > 0;
        NextCategoriesButton.IsEnabled = hasMultiplePages && _currentCategoryPage < CategoryPageCount - 1;

        UpdateCategoryPageDots();
    }

    private void UpdateCategoryPageDots()
    {
        CategoryPageDots.Children.Clear();

        if (CategoryPageCount <= 1)
        {
            return;
        }

        var activeBrush = (Brush)Application.Current.Resources["BandaAccentBrush"];
        var inactiveBrush = (Brush)Application.Current.Resources["BandaMutedBrush"];

        for (var page = 0; page < CategoryPageCount; page++)
        {
            CategoryPageDots.Children.Add(new Ellipse
            {
                Width = page == _currentCategoryPage ? 8 : 6,
                Height = page == _currentCategoryPage ? 8 : 6,
                Fill = page == _currentCategoryPage ? activeBrush : inactiveBrush,
                Opacity = page == _currentCategoryPage ? 1 : 0.55,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
    }

    private void PreviousCategoriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategoryPage <= 0)
        {
            return;
        }

        _currentCategoryPage--;
        UpdateCategoryPage();
    }

    private void NextCategoriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategoryPage >= CategoryPageCount - 1)
        {
            return;
        }

        _currentCategoryPage++;
        UpdateCategoryPage();
    }

    private void CategoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string categoryName } ||
            string.IsNullOrWhiteSpace(categoryName))
        {
            return;
        }

        if (!_selectedCategoryNames.Add(categoryName))
        {
            _selectedCategoryNames.Remove(categoryName);
        }

        UpdateCategoryPage();
        RefreshSearchResults();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSearchResults();
    }

    private void FiltersButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateFilterOverlayControls();
        FiltersOverlay.Visibility = Visibility.Visible;
    }

    private void CloseFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        FiltersOverlay.Visibility = Visibility.Collapsed;
    }

    private void FiltersBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        FiltersOverlay.Visibility = Visibility.Collapsed;
    }

    private void ResetFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDateFilter = SearchDateFilter.All;
        _pendingSpecificDateFilter = null;
        _pendingSizeFilter = SearchSizeFilter.All;
        _pendingExtensionFilters.Clear();

        SpecificDateWheelPanel.Visibility = Visibility.Collapsed;

        UpdatePendingFilterLabels();
        BuildExtensionFilterOptions();
    }

    private void ApplyFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDateFilter == SearchDateFilter.SpecificDate &&
            !_pendingSpecificDateFilter.HasValue)
        {
            _pendingSpecificDateFilter = DateTime.Today;
        }

        _dateFilter = _pendingDateFilter;
        _specificDateFilter =
            _pendingDateFilter == SearchDateFilter.SpecificDate
                ? _pendingSpecificDateFilter?.Date
                : null;

        _sizeFilter = _pendingSizeFilter;

        _extensionFilters.Clear();
        _extensionFilters.UnionWith(_pendingExtensionFilters);

        FiltersOverlay.Visibility = Visibility.Collapsed;
        RefreshSearchResults();
    }

    private void PopulateFilterOverlayControls()
    {
        _pendingDateFilter = _dateFilter;
        _pendingSpecificDateFilter = _specificDateFilter;
        _pendingSizeFilter = _sizeFilter;

        _pendingExtensionFilters.Clear();
        _pendingExtensionFilters.UnionWith(_extensionFilters);

        SpecificDateWheelPanel.Visibility =
            _pendingDateFilter == SearchDateFilter.SpecificDate
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (_pendingDateFilter == SearchDateFilter.SpecificDate)
        {
            var selectedDate = _pendingSpecificDateFilter ?? DateTime.Today;
            _pendingSpecificDateFilter = selectedDate.Date;
            SetSpecificDateWheel(selectedDate);
            QueueSpecificDateWheelSync();
        }

        UpdatePendingFilterLabels();
        BuildExtensionFilterOptions();
    }

    private void DateFilterOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterKey } ||
            !Enum.TryParse<SearchDateFilter>(filterKey, out var parsedFilter))
        {
            return;
        }

        _pendingDateFilter = parsedFilter;

        if (parsedFilter == SearchDateFilter.SpecificDate)
        {
            var selectedDate = DateTime.Today;
            _pendingSpecificDateFilter = selectedDate.Date;
            SpecificDateWheelPanel.Visibility = Visibility.Visible;
            SetSpecificDateWheel(selectedDate);
            QueueSpecificDateWheelSync();
        }
        else
        {
            SpecificDateWheelPanel.Visibility = Visibility.Collapsed;
        }

        DateFilterValueText.Text =
            GetDateFilterDisplayName(parsedFilter, _pendingSpecificDateFilter);

        DateFilterFlyout.Hide();
    }

    private void SpecificDateTodayButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDateFilter = SearchDateFilter.SpecificDate;
        _pendingSpecificDateFilter = DateTime.Today;
        SpecificDateWheelPanel.Visibility = Visibility.Visible;
        SetSpecificDateWheel(DateTime.Today);
        DateFilterValueText.Text =
            GetDateFilterDisplayName(
                SearchDateFilter.SpecificDate,
                _pendingSpecificDateFilter);
        QueueSpecificDateWheelSync();
    }

    private void DateWheelScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_isUpdatingDateWheels ||
            e.IsIntermediate ||
            sender is not ScrollViewer { Tag: string wheelName } scrollViewer)
        {
            return;
        }

        var itemCount = wheelName switch
        {
            "Day" => DateTime.DaysInMonth(_wheelYear, _wheelMonth),
            "Month" => 12,
            "Year" => _dateWheelYears.Count,
            _ => 0
        };

        if (itemCount <= 0)
        {
            return;
        }

        var index = Math.Clamp(
            (int)Math.Round(scrollViewer.VerticalOffset / DateWheelItemHeight),
            0,
            itemCount - 1);

        _isUpdatingDateWheels = true;
        scrollViewer.ChangeView(
            null,
            index * DateWheelItemHeight,
            null,
            true);
        _isUpdatingDateWheels = false;

        var rebuildDays = false;

        switch (wheelName)
        {
            case "Day":
                _wheelDay = index + 1;
                break;

            case "Month":
                _wheelMonth = index + 1;
                rebuildDays = true;
                break;

            case "Year":
                _wheelYear = _dateWheelYears[index];
                rebuildDays = true;
                break;
        }

        if (rebuildDays)
        {
            var daysInMonth = DateTime.DaysInMonth(_wheelYear, _wheelMonth);
            _wheelDay = Math.Min(_wheelDay, daysInMonth);
            RebuildDayWheelItems();
            QueueSpecificDateWheelSync();
        }

        _pendingSpecificDateFilter =
            new DateTime(_wheelYear, _wheelMonth, _wheelDay);

        DateFilterValueText.Text =
            GetDateFilterDisplayName(
                SearchDateFilter.SpecificDate,
                _pendingSpecificDateFilter);
    }

    private void InitializeSpecificDateWheels()
    {
        var culture = CultureInfo.GetCultureInfo("es-AR");

        MonthWheelItems.ItemsSource =
            Enumerable.Range(1, 12)
                .Select(month =>
                    culture.TextInfo.ToTitleCase(
                        culture.DateTimeFormat.GetMonthName(month)))
                .ToList();

        var firstYear = 1900;
        var lastYear = DateTime.Today.Year + 20;

        _dateWheelYears.Clear();
        _dateWheelYears.AddRange(
            Enumerable.Range(firstYear, (lastYear - firstYear) + 1));

        YearWheelItems.ItemsSource =
            _dateWheelYears
                .Select(year => year.ToString(CultureInfo.InvariantCulture))
                .ToList();

        SetSpecificDateWheel(DateTime.Today);
    }

    private void SetSpecificDateWheel(DateTime date)
    {
        var minYear = _dateWheelYears.First();
        var maxYear = _dateWheelYears.Last();

        _wheelYear = Math.Clamp(date.Year, minYear, maxYear);
        _wheelMonth = Math.Clamp(date.Month, 1, 12);

        var daysInMonth = DateTime.DaysInMonth(_wheelYear, _wheelMonth);
        _wheelDay = Math.Clamp(date.Day, 1, daysInMonth);

        RebuildDayWheelItems();
    }

    private void RebuildDayWheelItems()
    {
        var daysInMonth = DateTime.DaysInMonth(_wheelYear, _wheelMonth);

        DayWheelItems.ItemsSource =
            Enumerable.Range(1, daysInMonth)
                .Select(day => day.ToString("00", CultureInfo.InvariantCulture))
                .ToList();
    }

    private void QueueSpecificDateWheelSync()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SpecificDateWheelPanel.UpdateLayout();
            DayWheelScrollViewer.UpdateLayout();
            MonthWheelScrollViewer.UpdateLayout();
            YearWheelScrollViewer.UpdateLayout();

            SyncSpecificDateWheelOffsets();

            // Segundo pase para asegurar que las ruedas se posicionen
            // después de que WinUI termine el layout al mostrar el panel.
            DispatcherQueue.TryEnqueue(SyncSpecificDateWheelOffsets);
        });
    }

    private void SyncSpecificDateWheelOffsets()
    {
        if (SpecificDateWheelPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _isUpdatingDateWheels = true;

        DayWheelScrollViewer.ChangeView(
            null,
            (_wheelDay - 1) * DateWheelItemHeight,
            null,
            true);

        MonthWheelScrollViewer.ChangeView(
            null,
            (_wheelMonth - 1) * DateWheelItemHeight,
            null,
            true);

        var yearIndex = _dateWheelYears.IndexOf(_wheelYear);
        YearWheelScrollViewer.ChangeView(
            null,
            Math.Max(0, yearIndex) * DateWheelItemHeight,
            null,
            true);

        _isUpdatingDateWheels = false;
    }

    private void SizeFilterOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterKey } ||
            !Enum.TryParse<SearchSizeFilter>(filterKey, out var parsedFilter))
        {
            return;
        }

        _pendingSizeFilter = parsedFilter;
        SizeFilterValueText.Text = GetSizeFilterDisplayName(parsedFilter);
        SizeFilterFlyout.Hide();
    }

    private void ExtensionFilterOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string extensionKey })
        {
            return;
        }

        if (extensionKey.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            _pendingExtensionFilters.Clear();
        }
        else if (!_pendingExtensionFilters.Add(extensionKey))
        {
            _pendingExtensionFilters.Remove(extensionKey);
        }

        ExtensionFilterValueText.Text =
            GetExtensionFilterDisplayName(_pendingExtensionFilters);

        // Este submenu queda abierto para permitir seleccionar varias extensiones
        // antes de aplicar los filtros del popup principal.
        BuildExtensionFilterOptions();
    }

    private void SortOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sortKey })
        {
            return;
        }

        _sortMode = sortKey switch
        {
            "Oldest" => SearchSortMode.Oldest,
            "NameAscending" => SearchSortMode.NameAscending,
            "NameDescending" => SearchSortMode.NameDescending,
            "SizeDescending" => SearchSortMode.SizeDescending,
            "SizeAscending" => SearchSortMode.SizeAscending,
            "Category" => SearchSortMode.Category,
            _ => SearchSortMode.Newest
        };

        SortValueText.Text = GetSortDisplayName(_sortMode);
        SortFlyout.Hide();
        RefreshSearchResults();
    }

    private void UpdatePendingFilterLabels()
    {
        DateFilterValueText.Text =
            GetDateFilterDisplayName(_pendingDateFilter, _pendingSpecificDateFilter);
        SizeFilterValueText.Text = GetSizeFilterDisplayName(_pendingSizeFilter);
        ExtensionFilterValueText.Text =
            GetExtensionFilterDisplayName(_pendingExtensionFilters);
    }

    private void BuildExtensionFilterOptions()
    {
        ExtensionFilterOptionsPanel.Children.Clear();

        ExtensionFilterOptionsPanel.Children.Add(new TextBlock
        {
            Text = "EXTENSIÓN",
            Margin = new Thickness(10, 6, 10, 4),
            Foreground = (Brush)Application.Current.Resources["BandaMutedStrongBrush"],
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var options = new List<string> { "All" };

        options.AddRange(
            _allFiles
                .Select(file => file.ExtensionDisplay.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase));

        foreach (var option in options)
        {
            var isSelected =
                option.Equals("All", StringComparison.OrdinalIgnoreCase)
                    ? _pendingExtensionFilters.Count == 0
                    : _pendingExtensionFilters.Contains(option);

            var button = new Button
            {
                Tag = option,
                Content = option.Equals("All", StringComparison.OrdinalIgnoreCase)
                    ? "Todas las extensiones"
                    : option,
                Style = (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
            };

            if (isSelected)
            {
                button.Background =
                    (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
                button.Foreground =
                    (Brush)Application.Current.Resources["BandaAccentBrush"];
            }

            button.Click += ExtensionFilterOptionButton_Click;
            ExtensionFilterOptionsPanel.Children.Add(button);
        }
    }

    private static string GetDateFilterDisplayName(
        SearchDateFilter filter,
        DateTime? specificDate = null) =>
        filter switch
        {
            SearchDateFilter.Today => "Hoy",
            SearchDateFilter.Last7Days => "Últimos 7 días",
            SearchDateFilter.Last30Days => "Últimos 30 días",
            SearchDateFilter.SpecificDate when specificDate.HasValue =>
                specificDate.Value.ToString(
                    "dd/MM/yyyy",
                    CultureInfo.GetCultureInfo("es-AR")),
            SearchDateFilter.SpecificDate => "Fecha específica",
            _ => "Cualquier fecha"
        };

    private static string GetSizeFilterDisplayName(SearchSizeFilter filter) =>
        filter switch
        {
            SearchSizeFilter.Under100Mb => "Menos de 100 MB",
            SearchSizeFilter.From100To500Mb => "100 MB a 500 MB",
            SearchSizeFilter.From500MbTo1Gb => "500 MB a 1 GB",
            SearchSizeFilter.From1To5Gb => "1 GB a 5 GB",
            SearchSizeFilter.From5To20Gb => "5 GB a 20 GB",
            SearchSizeFilter.Over20Gb => "Más de 20 GB",
            _ => "Cualquier tamaño"
        };

    private static string GetExtensionFilterDisplayName(
        IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0)
        {
            return "Todas las extensiones";
        }

        var ordered = extensions
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Select(extension => extension.ToLowerInvariant())
            .ToList();

        return ordered.Count <= 2
            ? string.Join(" · ", ordered)
            : $"{ordered.Count} extensiones seleccionadas";
    }

    private static string GetSortDisplayName(SearchSortMode sortMode) =>
        sortMode switch
        {
            SearchSortMode.Oldest => "Fecha · más antigua",
            SearchSortMode.NameAscending => "Nombre · A–Z",
            SearchSortMode.NameDescending => "Nombre · Z–A",
            SearchSortMode.SizeDescending => "Tamaño · mayor primero",
            SearchSortMode.SizeAscending => "Tamaño · menor primero",
            SearchSortMode.Category => "Categoría",
            _ => "Fecha · más reciente"
        };

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategoryNames.Clear();
        _dateFilter = SearchDateFilter.All;
        _specificDateFilter = null;
        _sizeFilter = SearchSizeFilter.All;
        _extensionFilters.Clear();
        UpdateCategoryPage();

        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Text = string.Empty;
        }
        else
        {
            RefreshSearchResults();
        }
    }

    private void RefreshSearchResults()
    {
        IEnumerable<SearchFileResult> query = _allFiles;

        if (_selectedCategoryNames.Count > 0)
        {
            query = query.Where(file =>
                _selectedCategoryNames.Contains(file.Category));
        }

        var searchText = SearchBox.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(file =>
                file.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                file.ExtensionDisplay.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                file.Category.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                file.Location.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }

        if (_extensionFilters.Count > 0)
        {
            query = query.Where(file =>
                _extensionFilters.Contains(file.ExtensionDisplay));
        }

        var now = DateTime.Now;

        query = _dateFilter switch
        {
            SearchDateFilter.Today =>
                query.Where(file =>
                    file.ModifiedAt >= now.Date &&
                    file.ModifiedAt < now.Date.AddDays(1)),
            SearchDateFilter.Last7Days =>
                query.Where(file => file.ModifiedAt >= now.AddDays(-7)),
            SearchDateFilter.Last30Days =>
                query.Where(file => file.ModifiedAt >= now.AddDays(-30)),
            SearchDateFilter.SpecificDate when _specificDateFilter.HasValue =>
                query.Where(file =>
                    file.ModifiedAt >= _specificDateFilter.Value.Date &&
                    file.ModifiedAt < _specificDateFilter.Value.Date.AddDays(1)),
            _ => query
        };

        const long megabyte = 1024L * 1024L;
        const long gigabyte = 1024L * megabyte;

        query = _sizeFilter switch
        {
            SearchSizeFilter.Under100Mb =>
                query.Where(file => file.SizeBytes < 100 * megabyte),

            SearchSizeFilter.From100To500Mb =>
                query.Where(file => file.SizeBytes >= 100 * megabyte &&
                                    file.SizeBytes < 500 * megabyte),

            SearchSizeFilter.From500MbTo1Gb =>
                query.Where(file => file.SizeBytes >= 500 * megabyte &&
                                    file.SizeBytes < gigabyte),

            SearchSizeFilter.From1To5Gb =>
                query.Where(file => file.SizeBytes >= gigabyte &&
                                    file.SizeBytes < 5 * gigabyte),

            SearchSizeFilter.From5To20Gb =>
                query.Where(file => file.SizeBytes >= 5 * gigabyte &&
                                    file.SizeBytes < 20 * gigabyte),

            SearchSizeFilter.Over20Gb =>
                query.Where(file => file.SizeBytes >= 20 * gigabyte),

            _ => query
        };

        var categoryOrder = _allCategoryCards.ToDictionary(
            category => category.Name,
            category => category.Order,
            StringComparer.CurrentCultureIgnoreCase);

        var results = _sortMode switch
        {
            SearchSortMode.Oldest => query
                .OrderBy(file => file.ModifiedAt)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            SearchSortMode.NameAscending => query
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            SearchSortMode.NameDescending => query
                .OrderByDescending(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            SearchSortMode.SizeDescending => query
                .OrderByDescending(file => file.SizeBytes)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            SearchSortMode.SizeAscending => query
                .OrderBy(file => file.SizeBytes)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            SearchSortMode.Category => query
                .OrderBy(file =>
                    categoryOrder.TryGetValue(file.Category, out var order)
                        ? order
                        : int.MaxValue)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            _ => query
                .OrderByDescending(file => file.ModifiedAt)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };

        var previouslySelected = SearchResultsList.SelectedItems
            .OfType<SearchFileResult>()
            .ToList();

        VisibleSearchResults.Clear();
        foreach (var result in results)
        {
            VisibleSearchResults.Add(result);
        }

        SearchResultCountText.Text = results.Count.ToString(CultureInfo.CurrentCulture);
        SearchResultsFooterText.Text =
            results.Count == 1 ? "1 resultado" : $"{results.Count} resultados";

        SearchResultsList.Visibility =
            results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyStatePanel.Visibility =
            results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var file in previouslySelected.Where(results.Contains))
        {
            SearchResultsList.SelectedItems.Add(file);
        }

        if (SearchResultsList.SelectedItems.Count == 0 && results.Count > 0)
        {
            SearchResultsList.SelectedItems.Add(results[0]);
        }

        UpdateSearchSelectionDetails();

        if (results.Count == 0)
        {
            EmptyStateTitle.Text = "No hay archivos que coincidan";
            EmptyStateDescription.Text =
                _selectedCategoryNames.Count > 0 || !string.IsNullOrWhiteSpace(searchText)
                    ? "Probá cambiando las categorías seleccionadas o el texto de búsqueda."
                    : "Todavía no hay archivos para mostrar.";
        }

        var selectedInOrder = _allCategoryCards
            .Where(category => _selectedCategoryNames.Contains(category.Name))
            .OrderBy(category => category.Order)
            .Select(category => category.Name)
            .ToList();

        var hasCategoryFilters = selectedInOrder.Count > 0;
        var hasSearchFilter = !string.IsNullOrWhiteSpace(searchText);
        var advancedFilterDescriptions = BuildAdvancedFilterDescriptions();
        var hasAdvancedFilters = advancedFilterDescriptions.Count > 0;
        var hasAnyFilter = hasCategoryFilters || hasSearchFilter || hasAdvancedFilters;

        AllCategoriesChip.Visibility =
            hasCategoryFilters ? Visibility.Collapsed : Visibility.Visible;

        ActiveCategoriesChip.Visibility =
            hasCategoryFilters ? Visibility.Visible : Visibility.Collapsed;

        ActiveCategoriesText.Text =
            hasCategoryFilters ? string.Join(" · ", selectedInOrder) : string.Empty;

        var statusParts = new List<string>();

        if (hasCategoryFilters)
        {
            statusParts.Add(
                $"{selectedInOrder.Count} categoría{(selectedInOrder.Count == 1 ? string.Empty : "s")} seleccionada{(selectedInOrder.Count == 1 ? string.Empty : "s")}");
        }

        if (hasSearchFilter)
        {
            statusParts.Add("búsqueda activa");
        }

        statusParts.AddRange(advancedFilterDescriptions);

        NoFiltersText.Text =
            statusParts.Count == 0
                ? "Sin filtros"
                : string.Join(" · ", statusParts);

        FiltersButton.Content =
            hasAdvancedFilters
                ? $"Filtros ({advancedFilterDescriptions.Count})"
                : "Filtros";

        FiltersButton.Background =
            (Brush)Application.Current.Resources[
                hasAdvancedFilters ? "BandaAccentSoftBrush" : "BandaCardBrush"];

        FiltersButton.Foreground =
            (Brush)Application.Current.Resources[
                hasAdvancedFilters ? "BandaAccentBrush" : "BandaTextBrush"];

        ClearFiltersButton.IsEnabled = hasAnyFilter;
    }

    private void SearchResultsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateSearchSelectionDetails();
    }

    private List<SearchFileResult> GetSelectedSearchFiles() =>
        SearchResultsList.SelectedItems
            .OfType<SearchFileResult>()
            .ToList();

    private void UpdateSearchSelectionDetails()
    {
        var selectedFiles = GetSelectedSearchFiles();

        if (selectedFiles.Count == 0)
        {
            _selectedSearchFile = null;
            ClearSearchFileDetails();
            return;
        }

        if (selectedFiles.Count == 1)
        {
            _selectedSearchFile = selectedFiles[0];
            ShowSearchFileDetails(selectedFiles[0]);
            return;
        }

        _selectedSearchFile = null;
        ShowMultipleSearchFileDetails(selectedFiles);
    }

    private void ShowSearchFileDetails(SearchFileResult file)
    {
        SearchDetailTitleText.Text = "Detalle del archivo";
        SearchDetailFileNameText.Text = file.Name;
        SearchDetailCategoryText.Text = file.Category;
        SearchDetailSizeText.Text = file.SizeText;
        SearchDetailExtensionText.Text = file.ExtensionDisplay;
        SearchDetailModifiedText.Text = file.ModifiedText;
        SearchDetailLocationText.Text = file.Location;

        SearchOpenFileButton.Content = "Abrir archivo";
        SearchCopyPathButton.Content = "Copiar ruta";
        SearchChangeCategoryButton.Content = "Cambiar categoría";
        SearchDeleteButton.Content = "Eliminar archivo";

        SearchOpenFileButton.IsEnabled = true;
        SearchOpenLocationButton.IsEnabled = true;
        SearchCopyPathButton.IsEnabled = true;
        SearchChangeCategoryButton.IsEnabled = true;
        SearchRenameButton.IsEnabled = true;
        SearchDeleteButton.IsEnabled = true;

        SearchDetailActionStatusText.Text = string.Empty;
        SearchDetailActionStatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowMultipleSearchFileDetails(IReadOnlyList<SearchFileResult> files)
    {
        var categoryCount = files
            .Select(file => file.Category)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();

        var extensionCount = files
            .Select(file => file.ExtensionDisplay)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var locations = files
            .Select(file => file.Location)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        SearchDetailTitleText.Text = "Selección múltiple";
        SearchDetailFileNameText.Text =
            $"{files.Count} archivos seleccionados";
        SearchDetailCategoryText.Text =
            categoryCount == 1
                ? files[0].Category
                : $"{categoryCount} categorías";
        SearchDetailSizeText.Text =
            FormatSearchBytes(files.Sum(file => file.SizeBytes));
        SearchDetailExtensionText.Text =
            extensionCount == 1
                ? files[0].ExtensionDisplay
                : $"{extensionCount} extensiones";
        SearchDetailModifiedText.Text = "Varias fechas";
        SearchDetailLocationText.Text =
            locations.Count == 1
                ? locations[0]
                : $"{locations.Count} ubicaciones";

        SearchOpenFileButton.Content = "Abrir archivo";
        SearchCopyPathButton.Content = "Copiar rutas";
        SearchChangeCategoryButton.Content = "Cambiar categoría";
        SearchDeleteButton.Content = $"Eliminar {files.Count} archivos";

        SearchOpenFileButton.IsEnabled = false;
        SearchOpenLocationButton.IsEnabled = false;
        SearchCopyPathButton.IsEnabled = true;
        SearchChangeCategoryButton.IsEnabled = true;
        SearchRenameButton.IsEnabled = false;
        SearchDeleteButton.IsEnabled = true;

        SearchDetailActionStatusText.Text =
            "Abrir y renombrar requieren una selección individual.";
        SearchDetailActionStatusText.Visibility = Visibility.Visible;
    }

    private void ClearSearchFileDetails()
    {
        SearchDetailTitleText.Text = "Detalle del archivo";
        SearchDetailFileNameText.Text = "Seleccioná uno o varios archivos";
        SearchDetailCategoryText.Text = "—";
        SearchDetailSizeText.Text = "—";
        SearchDetailExtensionText.Text = "—";
        SearchDetailModifiedText.Text = "—";
        SearchDetailLocationText.Text = "—";
        SearchDetailActionStatusText.Text = string.Empty;
        SearchDetailActionStatusText.Visibility = Visibility.Collapsed;

        SearchOpenFileButton.Content = "Abrir archivo";
        SearchCopyPathButton.Content = "Copiar ruta";
        SearchChangeCategoryButton.Content = "Cambiar categoría";
        SearchDeleteButton.Content = "Eliminar archivo";

        SearchOpenFileButton.IsEnabled = false;
        SearchOpenLocationButton.IsEnabled = false;
        SearchCopyPathButton.IsEnabled = false;
        SearchChangeCategoryButton.IsEnabled = false;
        SearchRenameButton.IsEnabled = false;
        SearchDeleteButton.IsEnabled = false;
    }

    private void SearchOpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSearchFile is not { } file)
        {
            return;
        }

        var filePath = file.FilePath;

        if (!System.IO.File.Exists(filePath))
        {
            ShowSearchDetailStatus(
                "El archivo de esta maqueta todavía no existe físicamente. " +
                "Cuando Buscar use el índice real, este botón abrirá el archivo directamente.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });

            ShowSearchDetailStatus("Archivo abierto.");
        }
        catch
        {
            ShowSearchDetailStatus("No se pudo abrir el archivo.");
        }
    }

    private void SearchOpenLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSearchFile is not { } file)
        {
            return;
        }

        if (!System.IO.Directory.Exists(file.Location))
        {
            ShowSearchDetailStatus(
                "La ubicación de esta maqueta todavía no existe físicamente. " +
                "Con el índice real, este botón abrirá la carpeta correspondiente.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file.Location,
                UseShellExecute = true
            });

            ShowSearchDetailStatus("Ubicación abierta.");
        }
        catch
        {
            ShowSearchDetailStatus("No se pudo abrir la ubicación.");
        }
    }

    private void SearchCopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        var files = GetSelectedSearchFiles();
        if (files.Count == 0)
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(
            string.Join(
                Environment.NewLine,
                files.Select(file => file.FilePath)));
        Clipboard.SetContent(dataPackage);

        ShowSearchDetailStatus(
            files.Count == 1
                ? "Ruta copiada al portapapeles."
                : $"{files.Count} rutas copiadas al portapapeles.");
    }

    private void SearchChangeCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var files = GetSelectedSearchFiles();
        if (files.Count == 0)
        {
            return;
        }

        _managedSearchFiles.Clear();
        _managedSearchFiles.AddRange(files);

        _searchManageMode = SearchManageMode.ChangeCategory;

        var commonCategory = files
            .Select(file => file.Category)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _pendingSearchCategoryName =
            commonCategory.Count == 1 ? commonCategory[0] : null;

        SearchManageTitleText.Text = "Cambiar categoría";
        SearchManageSubtitleText.Text =
            files.Count == 1
                ? files[0].Name
                : $"{files.Count} archivos seleccionados";
        SearchManageIconText.Text = "↻";
        SearchManageIconBorder.Background =
            (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
        SearchManageIconText.Foreground =
            (Brush)Application.Current.Resources["BandaAccentBrush"];

        SearchManageCategoryPanel.Visibility = Visibility.Visible;
        SearchManageRenamePanel.Visibility = Visibility.Collapsed;
        SearchManageDeletePanel.Visibility = Visibility.Collapsed;

        SearchManageCategoryValueText.Text =
            _pendingSearchCategoryName ?? "Elegir categoría";
        SearchManagePrimaryButton.Content =
            files.Count == 1
                ? "Cambiar categoría"
                : $"Cambiar {files.Count} archivos";
        SearchManagePrimaryButton.Visibility = Visibility.Visible;
        SearchManageDangerButton.Visibility = Visibility.Collapsed;

        SearchManageValidationText.Visibility = Visibility.Collapsed;
        BuildSearchManageCategoryOptions();
        SearchManageOverlay.Visibility = Visibility.Visible;
    }

    private void SearchRenameButton_Click(object sender, RoutedEventArgs e)
    {
        var files = GetSelectedSearchFiles();
        if (files.Count != 1)
        {
            return;
        }

        var file = files[0];

        _managedSearchFiles.Clear();
        _managedSearchFiles.Add(file);

        _searchManageMode = SearchManageMode.Rename;
        _pendingSearchCategoryName = null;

        SearchManageTitleText.Text = "Renombrar archivo";
        SearchManageSubtitleText.Text = file.Name;
        SearchManageIconText.Text = "✎";
        SearchManageIconBorder.Background =
            (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
        SearchManageIconText.Foreground =
            (Brush)Application.Current.Resources["BandaAccentBrush"];

        SearchManageCategoryPanel.Visibility = Visibility.Collapsed;
        SearchManageRenamePanel.Visibility = Visibility.Visible;
        SearchManageDeletePanel.Visibility = Visibility.Collapsed;

        SearchManageRenameTextBox.Text = file.Name;
        SearchManagePrimaryButton.Content = "Guardar nombre";
        SearchManagePrimaryButton.Visibility = Visibility.Visible;
        SearchManageDangerButton.Visibility = Visibility.Collapsed;

        SearchManageValidationText.Visibility = Visibility.Collapsed;
        SearchManageOverlay.Visibility = Visibility.Visible;

        SearchManageRenameTextBox.SelectAll();
        SearchManageRenameTextBox.Focus(FocusState.Programmatic);
    }

    private void SearchDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var files = GetSelectedSearchFiles();
        if (files.Count == 0)
        {
            return;
        }

        _managedSearchFiles.Clear();
        _managedSearchFiles.AddRange(files);

        _searchManageMode = SearchManageMode.Delete;
        _pendingSearchCategoryName = null;

        SearchManageTitleText.Text =
            files.Count == 1 ? "Eliminar archivo" : "Eliminar archivos";
        SearchManageSubtitleText.Text =
            files.Count == 1
                ? files[0].Name
                : $"{files.Count} archivos seleccionados";
        SearchManageIconText.Text = "!";
        SearchManageIconBorder.Background =
            (Brush)Application.Current.Resources["BandaDangerSoftBrush"];
        SearchManageIconText.Foreground =
            (Brush)Application.Current.Resources["BandaDangerBrush"];

        SearchManageCategoryPanel.Visibility = Visibility.Collapsed;
        SearchManageRenamePanel.Visibility = Visibility.Collapsed;
        SearchManageDeletePanel.Visibility = Visibility.Visible;

        SearchManageDeleteText.Text =
            files.Count == 1
                ? $"¿Eliminar \"{files[0].Name}\"? La versión final eliminará físicamente el archivo de la PC. " +
                  "En esta maqueta todavía se elimina solo del conjunto de datos de prueba."
                : $"¿Eliminar los {files.Count} archivos seleccionados? La versión final los eliminará físicamente de la PC. " +
                  "En esta maqueta todavía se eliminan solo del conjunto de datos de prueba.";

        SearchManagePrimaryButton.Visibility = Visibility.Collapsed;
        SearchManageDangerButton.Content =
            files.Count == 1 ? "Eliminar" : $"Eliminar {files.Count} archivos";
        SearchManageDangerButton.Visibility = Visibility.Visible;

        SearchManageValidationText.Visibility = Visibility.Collapsed;
        SearchManageOverlay.Visibility = Visibility.Visible;
    }

    private void BuildSearchManageCategoryOptions()
    {
        SearchManageCategoryOptionsPanel.Children.Clear();

        SearchManageCategoryOptionsPanel.Children.Add(new TextBlock
        {
            Text = "CATEGORÍAS",
            Margin = new Thickness(10, 6, 10, 4),
            Foreground =
                (Brush)Application.Current.Resources["BandaMutedStrongBrush"],
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        foreach (var category in _allCategoryCards
                     .OrderBy(category => category.Order)
                     .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var button = new Button
            {
                Tag = category.Name,
                Content = category.Name,
                Style =
                    (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
            };

            if (category.Name.Equals(
                    _pendingSearchCategoryName,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                button.Background =
                    (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
                button.Foreground =
                    (Brush)Application.Current.Resources["BandaAccentBrush"];
            }

            button.Click += SearchManageCategoryOptionButton_Click;
            SearchManageCategoryOptionsPanel.Children.Add(button);
        }
    }

    private void SearchManageCategoryOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string categoryName })
        {
            return;
        }

        _pendingSearchCategoryName = categoryName;
        SearchManageCategoryValueText.Text = categoryName;
        SearchManageValidationText.Visibility = Visibility.Collapsed;

        BuildSearchManageCategoryOptions();
        SearchManageCategoryFlyout.Hide();
    }

    private void SearchManagePrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_managedSearchFiles.Count == 0)
        {
            CloseSearchManageOverlay();
            return;
        }

        SearchManageValidationText.Visibility = Visibility.Collapsed;

        switch (_searchManageMode)
        {
            case SearchManageMode.ChangeCategory:
                if (string.IsNullOrWhiteSpace(_pendingSearchCategoryName))
                {
                    SearchManageValidationText.Text =
                        "Elegí una categoría antes de continuar.";
                    SearchManageValidationText.Visibility = Visibility.Visible;
                    return;
                }

                foreach (var file in _managedSearchFiles)
                {
                    file.ChangeCategory(_pendingSearchCategoryName);
                }

                CloseSearchManageOverlay();
                LoadCategories(GetPreviewCategories());
                RefreshSearchResults();
                break;

            case SearchManageMode.Rename:
                if (_managedSearchFiles.Count != 1)
                {
                    CloseSearchManageOverlay();
                    return;
                }

                var proposedName = SearchManageRenameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(proposedName))
                {
                    SearchManageValidationText.Text =
                        "Escribí un nombre para el archivo.";
                    SearchManageValidationText.Visibility = Visibility.Visible;
                    return;
                }

                if (proposedName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    SearchManageValidationText.Text =
                        "El nombre contiene caracteres no permitidos.";
                    SearchManageValidationText.Visibility = Visibility.Visible;
                    return;
                }

                _managedSearchFiles[0].Rename(proposedName);
                CloseSearchManageOverlay();
                RefreshSearchResults();
                break;
        }
    }

    private void SearchManageDangerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchManageMode != SearchManageMode.Delete ||
            _managedSearchFiles.Count == 0)
        {
            CloseSearchManageOverlay();
            return;
        }

        foreach (var file in _managedSearchFiles.ToList())
        {
            _allFiles.Remove(file);
        }

        _selectedSearchFile = null;

        CloseSearchManageOverlay();
        LoadCategories(GetPreviewCategories());
        RefreshSearchResults();
    }

    private void SearchManageBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseSearchManageOverlay();
    }

    private void CloseSearchManageOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSearchManageOverlay();
    }

    private void CloseSearchManageOverlay()
    {
        SearchManageCategoryFlyout.Hide();
        SearchManageOverlay.Visibility = Visibility.Collapsed;
        SearchManageValidationText.Visibility = Visibility.Collapsed;

        _searchManageMode = SearchManageMode.None;
        _pendingSearchCategoryName = null;
        _managedSearchFiles.Clear();
        SearchManageDangerButton.Content = "Eliminar";
    }

    private static string FormatSearchBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
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

    private void ShowSearchDetailStatus(string message)
    {
        SearchDetailActionStatusText.Text = message;
        SearchDetailActionStatusText.Visibility = Visibility.Visible;
    }

    private List<string> BuildAdvancedFilterDescriptions()
    {
        var descriptions = new List<string>();

        switch (_dateFilter)
        {
            case SearchDateFilter.Today:
                descriptions.Add("hoy");
                break;
            case SearchDateFilter.Last7Days:
                descriptions.Add("últimos 7 días");
                break;
            case SearchDateFilter.Last30Days:
                descriptions.Add("últimos 30 días");
                break;
            case SearchDateFilter.SpecificDate when _specificDateFilter.HasValue:
                descriptions.Add(
                    _specificDateFilter.Value.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.GetCultureInfo("es-AR")));
                break;
        }

        switch (_sizeFilter)
        {
            case SearchSizeFilter.Under100Mb:
                descriptions.Add("< 100 MB");
                break;
            case SearchSizeFilter.From100To500Mb:
                descriptions.Add("100–500 MB");
                break;
            case SearchSizeFilter.From500MbTo1Gb:
                descriptions.Add("500 MB–1 GB");
                break;
            case SearchSizeFilter.From1To5Gb:
                descriptions.Add("1–5 GB");
                break;
            case SearchSizeFilter.From5To20Gb:
                descriptions.Add("5–20 GB");
                break;
            case SearchSizeFilter.Over20Gb:
                descriptions.Add("> 20 GB");
                break;
        }

        if (_extensionFilters.Count == 1)
        {
            descriptions.Add(_extensionFilters.First().ToLowerInvariant());
        }
        else if (_extensionFilters.Count > 1)
        {
            descriptions.Add($"{_extensionFilters.Count} extensiones");
        }

        return descriptions;
    }

    private static IReadOnlyList<CategoryDefinition> GetPreviewCategories()
    {
        return
        [
            new("RAR", [".zip", ".rar", ".7z"], 1),
            new("INSTALLERS", [".exe", ".msi", ".bat"], 2),
            new("DOCUMENTS", [".pdf", ".docx", ".xlsx", ".txt"], 3),
            new("IMAGES", [".jpg", ".jpeg", ".png", ".webp", ".avif"], 4),
            new("GIF", [".gif"], 5),
            new("VIDEOS", [".mp4", ".mkv", ".mov", ".avi"], 6),
            new("AUDIO", [".mp3", ".wav", ".flac", ".aac", ".ogg"], 7),
            new("FONTS", [".ttf", ".otf", ".woff", ".woff2"], 8),
            new("DESIGN", [".ai", ".psd", ".indd", ".fig"], 9),
            new("CODE", [".cs", ".ps1", ".js", ".json"], 10),
            new("BACKUPS", [".bak", ".backup"], 11),
            new("PROJECTS", [".sln", ".slnx", ".csproj"], 12),
            new("TEXTURES", [".tga", ".dds", ".exr"], 13),
            new("PACKAGES", [".nupkg", ".appx", ".msix"], 14)
        ];
    }

    private static IReadOnlyList<SearchFileResult> BuildPreviewFiles()
    {
        var categories = GetPreviewCategories();
        var files = new List<SearchFileResult>();
        var anchor = DateTime.Now;

        foreach (var category in categories)
        {
            var fileCount = 2 + (category.Order % 3);

            for (var index = 1; index <= fileCount; index++)
            {
                var extension = category.Extensions[(index - 1) % category.Extensions.Count];
                var normalizedExtension =
                    extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";

                var fileName =
                    $"{category.Name.ToLowerInvariant()}_{index:00}{normalizedExtension}";

                var sizeBytes = GetPreviewFileSizeBytes(category.Order, index);

                var modifiedAt = anchor
                    .AddDays(-(category.Order - 1))
                    .AddHours(-(index * 2))
                    .AddMinutes(-(index * 11))
                    .AddSeconds(-(category.Order * index));

                files.Add(new SearchFileResult(
                    fileName,
                    category.Name,
                    sizeBytes,
                    modifiedAt,
                    $@"C:\Users\Usuario\Downloads\ORGANIZADO\{category.Name}"));
            }
        }

        return files;
    }

    private static long GetPreviewFileSizeBytes(int categoryOrder, int index)
    {
        const long megabyte = 1024L * 1024L;
        const long gigabyte = 1024L * megabyte;

        var bucket = (categoryOrder + index - 2) % 6;

        return bucket switch
        {
            0 => (35L + categoryOrder + index) * megabyte,
            1 => (140L + (categoryOrder * 9L) + (index * 18L)) * megabyte,
            2 => (560L + (categoryOrder * 12L) + (index * 24L)) * megabyte,
            3 => gigabyte + ((categoryOrder * 180L) + (index * 260L)) * megabyte,
            4 => (5L * gigabyte) + ((categoryOrder * 420L) + (index * 520L)) * megabyte,
            _ => (20L * gigabyte) + ((categoryOrder * 650L) + (index * 800L)) * megabyte
        };
    }

    private static string BuildExtensionsText(IReadOnlyList<string> extensions)
    {
        var normalized = extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension =>
            {
                var value = extension.Trim();
                return value.StartsWith('.') ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return "Sin extensiones";
        }

        const int visibleExtensions = 4;
        var visible = normalized.Take(visibleExtensions);
        var text = string.Join(" · ", visible);

        var remaining = normalized.Count - visibleExtensions;
        return remaining > 0 ? $"{text} +{remaining}" : text;
    }
}

public enum SearchManageMode
{
    None,
    ChangeCategory,
    Rename,
    Delete
}

public enum SearchDateFilter
{
    All,
    Today,
    Last7Days,
    Last30Days,
    SpecificDate
}

public enum SearchSizeFilter
{
    All,
    Under100Mb,
    From100To500Mb,
    From500MbTo1Gb,
    From1To5Gb,
    From5To20Gb,
    Over20Gb
}

public enum SearchSortMode
{
    Newest,
    Oldest,
    NameAscending,
    NameDescending,
    SizeDescending,
    SizeAscending,
    Category
}

public sealed class SearchCategorySummary
{
    public SearchCategorySummary()
    {
    }

    public SearchCategorySummary(
        string name,
        int order,
        string extensionsText,
        string countText,
        bool isSelected)
    {
        Name = name;
        Order = order;
        ExtensionsText = extensionsText;
        CountText = countText;
        IsSelected = isSelected;
    }

    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string ExtensionsText { get; set; } = "Sin extensiones";
    public string CountText { get; set; } = "0";
    public bool IsSelected { get; set; }

    public Visibility SelectedVisibility =>
        IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush CardBackground =>
        (Brush)Application.Current.Resources[
            IsSelected ? "BandaAccentFaintBrush" : "BandaCardBrush"];

    public Brush CardBorderBrush =>
        (Brush)Application.Current.Resources[
            IsSelected ? "BandaAccentBrush" : "BandaBorderBrush"];
}

public sealed class SearchFileResult
{
    public SearchFileResult(
        string name,
        string category,
        long sizeBytes,
        DateTime modifiedAt,
        string location)
    {
        Name = name;
        Category = category;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        Location = location;
    }

    public string Name { get; private set; }
    public string Category { get; private set; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string Location { get; private set; }

    public string FilePath =>
        System.IO.Path.Combine(Location, Name);

    public void Rename(string proposedName)
    {
        var value = proposedName.Trim();

        if (string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(value)))
        {
            value += System.IO.Path.GetExtension(Name);
        }

        Name = value;
    }

    public void ChangeCategory(string categoryName)
    {
        var parent = System.IO.Directory.GetParent(Location)?.FullName;

        Category = categoryName;

        if (!string.IsNullOrWhiteSpace(parent))
        {
            Location = System.IO.Path.Combine(parent, categoryName);
        }
    }

    public string ExtensionDisplay =>
        System.IO.Path.GetExtension(Name).ToUpperInvariant();

    public string SizeText => FormatBytes(SizeBytes);

    public string ModifiedText =>
        ModifiedAt.ToString(
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.GetCultureInfo("es-AR"));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        var value = (double)Math.Max(0, bytes);
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
}
