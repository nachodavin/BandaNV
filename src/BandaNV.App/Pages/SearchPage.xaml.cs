using BandaNV.Core.Models;
using BandaNV.Core.Services;
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
    private CancellationTokenSource? _scanCts;
    private bool _isUpdatingDateWheels;
    private int _wheelDay = 1;
    private int _wheelMonth = 1;
    private int _wheelYear = DateTime.Today.Year;

    public SearchPage()
    {
        InitializeComponent();
        InitializeSpecificDateWheels();

        Loaded += SearchPage_Loaded;
    }

    private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SearchPage_Loaded;
        await LoadRealSearchDataAsync();
    }

    private async Task LoadRealSearchDataAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        _allFiles.Clear();
        LoadCategories(GetCurrentCategories());
        RefreshSearchResults();

        try
        {
            var indexedFiles =
                await global::BandaNV.App.App.SearchIndex.ScanAsync(
                    global::BandaNV.App.App.Settings.Current,
                    _scanCts.Token);

            var categoryColors =
                global::BandaNV.App.App.Categories.GetAll()
                    .ToDictionary(
                        category => category.Id,
                        category => category.ColorHex,
                        StringComparer.OrdinalIgnoreCase);

            foreach (var file in indexedFiles)
            {
                var location =
                    System.IO.Path.GetDirectoryName(file.FullPath) ??
                    global::BandaNV.App.App.Settings.Current.DestinationFolder;

                var colorHex =
                    categoryColors.TryGetValue(
                        file.CategoryId,
                        out var savedColor)
                        ? savedColor
                        : CategoryColorPalette.Generate(
                            file.CategoryId,
                            global::BandaNV.App.App.Settings.Current.SecondaryColor);

                _allFiles.Add(
                    new SearchFileResult(
                        file.Name,
                        file.CategoryName,
                        file.SizeBytes,
                        file.ModifiedAt,
                        location,
                        colorHex,
                        file.IsDirectory,
                        file.ContainedFileCount,
                        file.FolderContents));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // Buscar queda vacío si el destino no puede escanearse.
        }

        LoadCategories(GetCurrentCategories());
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
                _selectedCategoryNames.Contains(category.Name),
                category.ColorHex));
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
                .SelectMany(file =>
                    file.IsDirectory
                        ? file.FolderContents.Select(item =>
                            item.ExtensionDisplay.ToLowerInvariant())
                        : [file.ExtensionDisplay.ToLowerInvariant()])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    extension => extension,
                    StringComparer.OrdinalIgnoreCase));

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
                file.Location.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                file.ContentSearchText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }

        if (_extensionFilters.Count > 0)
        {
            query = query.Where(file =>
                file.MatchesExtensionFilters(
                    _extensionFilters));
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
            results.Count == 1
                ? "1 elemento"
                : $"{results.Count} elementos";

        SearchResultsList.Visibility =
            results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyStatePanel.Visibility =
            results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var file in previouslySelected.Where(results.Contains))
        {
            SearchResultsList.SelectedItems.Add(file);
        }

        UpdateSearchSelectionDetails();

        if (results.Count == 0)
        {
            EmptyStateTitle.Text = "No hay elementos que coincidan";
            EmptyStateDescription.Text =
                _selectedCategoryNames.Count > 0 || !string.IsNullOrWhiteSpace(searchText)
                    ? "Probá cambiando las categorías seleccionadas o el texto de búsqueda."
                    : "Todavía no hay elementos para mostrar.";
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

        BuildActiveCategoryFilterVisuals(
            selectedInOrder);

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

    private void BuildActiveCategoryFilterVisuals(
        IReadOnlyList<string> selectedCategoryNames)
    {
        ActiveCategoriesVisualPanel.Children.Clear();

        if (selectedCategoryNames.Count == 0)
        {
            return;
        }

        const int visibleLimit = 4;

        foreach (var categoryName in selectedCategoryNames.Take(visibleLimit))
        {
            var category = _allCategoryCards.FirstOrDefault(item =>
                item.Name.Equals(
                    categoryName,
                    StringComparison.CurrentCultureIgnoreCase));

            if (category is null)
            {
                continue;
            }

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            item.Children.Add(
                new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = category.CategoryBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });

            item.Children.Add(
                new TextBlock
                {
                    Text = category.Name,
                    Foreground = category.CategoryBrush,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });

            ActiveCategoriesVisualPanel.Children.Add(item);
        }

        if (selectedCategoryNames.Count > visibleLimit)
        {
            ActiveCategoriesVisualPanel.Children.Add(
                new TextBlock
                {
                    Text = $"+{selectedCategoryNames.Count - visibleLimit}",
                    Foreground =
                        (Brush)Application.Current.Resources[
                            "BandaMutedStrongBrush"],
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
        }
    }

    private void ApplySearchDetailCategoryVisual(
        SearchFileResult? file,
        bool neutral = false)
    {
        if (file is null || neutral)
        {
            SearchDetailCategoryCard.Background =
                (Brush)Application.Current.Resources[
                    "BandaNavIconBrush"];
            SearchDetailCategoryCard.BorderBrush =
                (Brush)Application.Current.Resources[
                    "BandaBorderBrush"];
            SearchDetailCategoryDot.Fill =
                (Brush)Application.Current.Resources[
                    "BandaMutedBrush"];
            SearchDetailCategoryText.Foreground =
                (Brush)Application.Current.Resources[
                    "BandaMutedStrongBrush"];
            return;
        }

        SearchDetailCategoryCard.Background =
            file.CategorySoftBrush;
        SearchDetailCategoryCard.BorderBrush =
            file.CategoryBrush;
        SearchDetailCategoryDot.Fill =
            file.CategoryBrush;
        SearchDetailCategoryText.Foreground =
            file.CategoryBrush;
    }

    private StackPanel CreateSearchCategoryOptionContent(
        SearchCategorySummary category)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        content.Children.Add(
            new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = category.CategoryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

        content.Children.Add(
            new TextBlock
            {
                Text = category.Name,
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

        return content;
    }

    private void SearchResultsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateSearchSelectionDetails();
    }

    private void SearchResultsList_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;

        while (current is not null && current != SearchResultsList)
        {
            if (current is ListViewItem)
            {
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        SearchResultsList.SelectedItems.Clear();
        UpdateSearchSelectionDetails();
    }

    private void SearchResultsList_DoubleTapped(
        object sender,
        Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var file = GetSearchFileFromEventSource(e.OriginalSource);
        if (file is null)
        {
            return;
        }

        OpenSearchFile(file);
        e.Handled = true;
    }

    private void SearchSelectAllAccelerator_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var file in VisibleSearchResults)
        {
            if (!SearchResultsList.SelectedItems.Contains(file))
            {
                SearchResultsList.SelectedItems.Add(file);
            }
        }

        UpdateSearchSelectionDetails();
        args.Handled = true;
    }

    private SearchFileResult? GetSearchFileFromEventSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null && current != SearchResultsList)
        {
            if (current is FrameworkElement
                {
                    DataContext: SearchFileResult file
                })
            {
                return file;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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

    private void ShowSearchFileDetails(
        SearchFileResult file)
    {
        SearchDetailTitleText.Text =
            file.IsDirectory
                ? "Detalle de la carpeta"
                : "Detalle del archivo";

        SearchDetailFileNameText.Text =
            file.Name;

        SearchDetailCategoryText.Text =
            file.Category;

        ApplySearchDetailCategoryVisual(
            file);

        SearchDetailSizeText.Text =
            file.SizeText;

        SearchDetailExtensionText.Text =
            file.ExtensionDisplay;

        SearchDetailModifiedText.Text =
            file.ModifiedText;

        SearchDetailLocationText.Text =
            file.Location;

        SearchOpenFileButton.Content =
            file.IsDirectory
                ? "Abrir carpeta"
                : "Abrir archivo";

        SearchCopyPathButton.Content =
            "Copiar ruta";

        SearchChangeCategoryButton.Content =
            "Cambiar categoría";

        SearchDeleteButton.Content =
            file.IsDirectory
                ? "Eliminar carpeta"
                : "Eliminar archivo";

        SearchOpenFileButton.IsEnabled =
            true;
        SearchOpenLocationButton.IsEnabled =
            true;
        SearchCopyPathButton.IsEnabled =
            true;
        SearchChangeCategoryButton.IsEnabled =
            true;
        SearchRenameButton.IsEnabled =
            true;
        SearchDeleteButton.IsEnabled =
            true;

        SearchDetailActionStatusText.Text =
            string.Empty;

        SearchDetailActionStatusText.Visibility =
            Visibility.Collapsed;
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
            $"{files.Count} elementos seleccionados";
        SearchDetailCategoryText.Text =
            categoryCount == 1
                ? files[0].Category
                : $"{categoryCount} categorías";

        ApplySearchDetailCategoryVisual(
            categoryCount == 1 ? files[0] : null,
            neutral: categoryCount != 1);

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

        SearchOpenFileButton.Content = "Abrir elemento";
        SearchCopyPathButton.Content = "Copiar rutas";
        SearchChangeCategoryButton.Content = "Cambiar categoría";
        SearchDeleteButton.Content = $"Eliminar {files.Count} elementos";

        SearchOpenFileButton.IsEnabled = false;
        SearchOpenLocationButton.IsEnabled = false;
        SearchCopyPathButton.IsEnabled = true;
        SearchChangeCategoryButton.IsEnabled = true;
        SearchRenameButton.IsEnabled = false;
        SearchDeleteButton.IsEnabled = true;

        SearchDetailActionStatusText.Text =
            "Abrir elemento, abrir ubicación y renombrar requieren una selección individual.";
        SearchDetailActionStatusText.Visibility = Visibility.Visible;
    }

    private void ClearSearchFileDetails()
    {
        SearchDetailTitleText.Text = "Detalle del elemento";
        SearchDetailFileNameText.Text = "Seleccioná uno o varios elementos";
        SearchDetailCategoryText.Text = "—";
        ApplySearchDetailCategoryVisual(null, neutral: true);
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

        OpenSearchFile(file);
    }

    private void OpenSearchFile(
        SearchFileResult file)
    {
        var path =
            file.FilePath;

        if (!OrganizationEntrySafety.Exists(
                path))
        {
            ShowSearchDetailStatus(
                "El elemento ya no existe en la ubicación registrada. Volvé a entrar a Buscar para refrescar los resultados.");

            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        path,
                    UseShellExecute =
                        true
                });

            ShowSearchDetailStatus(
                file.IsDirectory
                    ? "Carpeta abierta."
                    : "Archivo abierto.");
        }
        catch
        {
            ShowSearchDetailStatus(
                file.IsDirectory
                    ? "No se pudo abrir la carpeta."
                    : "No se pudo abrir el archivo.");
        }
    }

    private void SearchFolderContentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: SearchFileResult file
            } ||
            !file.IsDirectory)
        {
            return;
        }

        ShowSearchFolderContents(
            file);
    }

    private void ShowSearchFolderContents(
        SearchFileResult folder)
    {
        SearchFolderContentTitleText.Text =
            folder.Name;

        SearchFolderContentSummaryText.Text =
            folder.ContainedFileCount == 1
                ? $"1 archivo · {folder.SizeText}"
                : $"{folder.ContainedFileCount} archivos · {folder.SizeText}";

        SearchFolderContentPathText.Text =
            folder.FilePath;

        SearchFolderContentList.ItemsSource =
            folder.FolderContents;

        SearchFolderContentOverlay.Visibility =
            Visibility.Visible;
    }

    private void CloseSearchFolderContentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseSearchFolderContents();
    }

    private void SearchFolderContentBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseSearchFolderContents();
    }

    private void CloseSearchFolderContents()
    {
        SearchFolderContentOverlay.Visibility =
            Visibility.Collapsed;

        SearchFolderContentList.ItemsSource =
            null;
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
                "La ubicación ya no existe. Volvé a entrar a Buscar para refrescar los resultados.");
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
                : $"{files.Count} elementos seleccionados";
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

        var currentCategoryVisual =
            _pendingSearchCategoryName is null
                ? null
                : _allCategoryCards.FirstOrDefault(category =>
                    category.Name.Equals(
                        _pendingSearchCategoryName,
                        StringComparison.CurrentCultureIgnoreCase));

        SearchManageCategoryValueText.Foreground =
            currentCategoryVisual?.CategoryBrush ??
            (Brush)Application.Current.Resources[
                "BandaTextBrush"];

        SearchManagePrimaryButton.Content =
            files.Count == 1
                ? "Cambiar categoría"
                : $"Cambiar {files.Count} elementos";
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

        SearchManageTitleText.Text =
            file.IsDirectory
                ? "Renombrar carpeta"
                : "Renombrar archivo";
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

    private async void SearchDeleteButton_Click(object sender, RoutedEventArgs e)
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
            files.Count == 1
                ? files[0].IsDirectory
                    ? "Eliminar carpeta"
                    : "Eliminar archivo"
                : "Eliminar elementos";
        SearchManageSubtitleText.Text =
            files.Count == 1
                ? files[0].Name
                : $"{files.Count} elementos seleccionados";
        SearchManageIconText.Text = "!";
        SearchManageIconBorder.Background =
            (Brush)Application.Current.Resources["BandaDangerSoftBrush"];
        SearchManageIconText.Foreground =
            (Brush)Application.Current.Resources["BandaDangerBrush"];

        SearchManageCategoryPanel.Visibility = Visibility.Collapsed;
        SearchManageRenamePanel.Visibility = Visibility.Collapsed;
        SearchManageDeletePanel.Visibility = Visibility.Visible;

        var settings =
            global::BandaNV.App.App.Settings.Current;

        SearchManageDeleteText.Text =
            files.Count == 1
                ? settings.UseRecycleBin
                    ? $"¿Enviar \"{files[0].Name}\" a la Papelera?"
                    : $"¿Eliminar permanentemente \"{files[0].Name}\"?"
                : settings.UseRecycleBin
                    ? $"¿Enviar los {files.Count} elementos seleccionados a la Papelera?"
                    : $"¿Eliminar permanentemente los {files.Count} elementos seleccionados?";

        if (!settings.UseRecycleBin)
        {
            SearchManageDeleteText.Text +=
                " Esta acción no pasa por la Papelera.";
        }

        SearchManagePrimaryButton.Visibility = Visibility.Collapsed;
        SearchManageDangerButton.Content =
            files.Count == 1
                ? "Eliminar"
                : $"Eliminar {files.Count} elementos";
        SearchManageDangerButton.Visibility = Visibility.Visible;

        SearchManageValidationText.Visibility = Visibility.Collapsed;

        if (!settings.ConfirmDestructiveActions)
        {
            await ExecuteDeleteManagedFilesAsync();
            return;
        }

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
                Content = CreateSearchCategoryOptionContent(category),
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

        var selectedCategoryVisual =
            _allCategoryCards.FirstOrDefault(category =>
                category.Name.Equals(
                    categoryName,
                    StringComparison.CurrentCultureIgnoreCase));

        SearchManageCategoryValueText.Foreground =
            selectedCategoryVisual?.CategoryBrush ??
            (Brush)Application.Current.Resources[
                "BandaTextBrush"];

        SearchManageValidationText.Visibility = Visibility.Collapsed;

        BuildSearchManageCategoryOptions();
        SearchManageCategoryFlyout.Hide();
    }

    private async void SearchManagePrimaryButton_Click(object sender, RoutedEventArgs e)
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

                var targetCategory =
                    global::BandaNV.App.App.Categories.GetAll()
                        .FirstOrDefault(category =>
                            category.Name.Equals(
                                _pendingSearchCategoryName,
                                StringComparison.CurrentCultureIgnoreCase));

                if (targetCategory is null)
                {
                    SearchManageValidationText.Text =
                        "La categoría elegida ya no existe.";
                    SearchManageValidationText.Visibility = Visibility.Visible;
                    return;
                }

                await ExecuteChangeCategoryAsync(targetCategory);
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
                        "Escribí un nombre para el elemento.";
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

                await ExecuteRenameAsync(
                    _managedSearchFiles[0],
                    proposedName);
                break;
        }
    }

    private async void SearchManageDangerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchManageMode != SearchManageMode.Delete ||
            _managedSearchFiles.Count == 0)
        {
            CloseSearchManageOverlay();
            return;
        }

        await ExecuteDeleteManagedFilesAsync();
    }

    private async Task ExecuteChangeCategoryAsync(
        CategorySettings targetCategory)
    {
        SetSearchManageBusyState(true);

        try
        {
            var paths = _managedSearchFiles
                .Select(file => file.FilePath)
                .ToList();

            var result =
                await global::BandaNV.App.App.SearchActions.MoveToCategoryAsync(
                    global::BandaNV.App.App.Settings.Current,
                    paths,
                    targetCategory);

            await ApplySearchHistoryRetentionAsync();

            var completed = result.CompletedCount;
            var issues = result.IssueCount;

            CloseSearchManageOverlay();
            await LoadRealSearchDataAsync();

            ShowSearchDetailStatus(
                issues == 0
                    ? completed == 1
                        ? $"Elemento movido a {targetCategory.Name}."
                        : $"{completed} elementos movidos a {targetCategory.Name}."
                    : $"{completed} movidos · {issues} no se modificaron por conflicto o error.");
        }
        catch (Exception ex)
        {
            SearchManageValidationText.Text =
                $"No se pudo cambiar la categoría: {ex.Message}";
            SearchManageValidationText.Visibility = Visibility.Visible;
        }
        finally
        {
            SetSearchManageBusyState(false);
        }
    }

    private async Task ExecuteRenameAsync(
        SearchFileResult file,
        string proposedName)
    {
        SetSearchManageBusyState(true);

        try
        {
            var result =
                await global::BandaNV.App.App.SearchActions.RenameAsync(
                    global::BandaNV.App.App.Settings.Current,
                    file.FilePath,
                    proposedName);

            await ApplySearchHistoryRetentionAsync();

            var item = result.Items.Single();

            if (item.Status != SearchFileActionStatus.Completed)
            {
                SearchManageValidationText.Text =
                    item.Message ?? "No se pudo renombrar el elemento.";
                SearchManageValidationText.Visibility = Visibility.Visible;
                return;
            }

            CloseSearchManageOverlay();
            await LoadRealSearchDataAsync();
            ShowSearchDetailStatus(
                file.IsDirectory
                    ? "Carpeta renombrada correctamente."
                    : "Archivo renombrado correctamente.");
        }
        catch (Exception ex)
        {
            SearchManageValidationText.Text =
                $"No se pudo renombrar: {ex.Message}";
            SearchManageValidationText.Visibility = Visibility.Visible;
        }
        finally
        {
            SetSearchManageBusyState(false);
        }
    }

    private async Task ExecuteDeleteManagedFilesAsync()
    {
        SetSearchManageBusyState(true);

        try
        {
            var files = _managedSearchFiles.ToList();
            var paths = files
                .Select(file => file.FilePath)
                .ToList();

            var useRecycleBin =
                global::BandaNV.App.App.Settings.Current.UseRecycleBin;

            var result =
                await global::BandaNV.App.App.SearchActions.DeleteAsync(
                    global::BandaNV.App.App.Settings.Current,
                    paths);

            await ApplySearchHistoryRetentionAsync();

            var completed = result.CompletedCount;
            var issues = result.IssueCount;

            CloseSearchManageOverlay();
            await LoadRealSearchDataAsync();

            var destinationText = useRecycleBin
                ? "enviados a la Papelera"
                : "eliminados permanentemente";

            ShowSearchDetailStatus(
                issues == 0
                    ? $"{completed} elemento{(completed == 1 ? string.Empty : "s")} {destinationText}."
                    : $"{completed} {destinationText} · {issues} no pudieron eliminarse.");
        }
        catch (Exception ex)
        {
            SearchManageValidationText.Text =
                $"No se pudo eliminar: {ex.Message}";
            SearchManageValidationText.Visibility = Visibility.Visible;

            if (SearchManageOverlay.Visibility != Visibility.Visible)
            {
                SearchManageOverlay.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            SetSearchManageBusyState(false);
        }
    }

    private static async Task ApplySearchHistoryRetentionAsync()
    {
        try
        {
            await global::BandaNV.App.App.History.ApplyRetentionAsync(
                global::BandaNV.App.App.Settings.Current);
        }
        catch
        {
            // La acción de Buscar ya terminó. Un fallo de mantenimiento
            // no cambia su resultado y podrá reintentarse más adelante.
        }
    }

    private void SetSearchManageBusyState(bool isBusy)
    {
        SearchManagePrimaryButton.IsEnabled = !isBusy;
        SearchManageDangerButton.IsEnabled = !isBusy;
        SearchManageCategorySelectorButton.IsEnabled = !isBusy;
        SearchManageRenameTextBox.IsEnabled = !isBusy;
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
        SetSearchManageBusyState(false);
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

    private static IReadOnlyList<CategoryDefinition> GetCurrentCategories()
    {
        return global::BandaNV.App.App.Categories.GetAll()
            .OrderBy(category => category.Order)
            .Select(category => new CategoryDefinition(
                category.Name,
                category.Extensions,
                category.Order,
                category.ColorHex))
            .ToList();
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
        bool isSelected,
        string colorHex)
    {
        Name = name;
        Order = order;
        ExtensionsText = extensionsText;
        CountText = countText;
        IsSelected = isSelected;
        ColorHex = colorHex;
    }

    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string ExtensionsText { get; set; } = "Sin extensiones";
    public string CountText { get; set; } = "0";
    public string ColorHex { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public Brush CategoryBrush
    {
        get
        {
            if (!CategoryColorPalette.TryNormalizeHex(
                    ColorHex,
                    out var normalized))
            {
                return (Brush)Application.Current.Resources[
                    "BandaAccentBrush"];
            }

            return new SolidColorBrush(
                Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(normalized.Substring(1, 2), 16),
                    Convert.ToByte(normalized.Substring(3, 2), 16),
                    Convert.ToByte(normalized.Substring(5, 2), 16)));
        }
    }

    public Visibility SelectedVisibility =>
        IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush CategorySoftBrush =>
        CreateCategoryBrush(
            ColorHex,
            alpha: 0x22);

    public Brush CardBackground =>
        IsSelected
            ? CategorySoftBrush
            : (Brush)Application.Current.Resources[
                "BandaCardBrush"];

    public Brush CardBorderBrush =>
        IsSelected
            ? CategoryBrush
            : (Brush)Application.Current.Resources[
                "BandaBorderBrush"];

    private static Brush CreateCategoryBrush(
        string colorHex,
        byte alpha)
    {
        if (!CategoryColorPalette.TryNormalizeHex(
                colorHex,
                out var normalized))
        {
            return (Brush)Application.Current.Resources[
                "BandaAccentSoftBrush"];
        }

        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(
                alpha,
                Convert.ToByte(normalized.Substring(1, 2), 16),
                Convert.ToByte(normalized.Substring(3, 2), 16),
                Convert.ToByte(normalized.Substring(5, 2), 16)));
    }
}

public sealed class SearchFolderContentItem
{
    public SearchFolderContentItem(
        string relativePath,
        string name,
        string extension,
        long sizeBytes,
        DateTime modifiedAt)
    {
        RelativePath =
            relativePath;

        Name =
            name;

        Extension =
            extension;

        SizeBytes =
            sizeBytes;

        ModifiedAt =
            modifiedAt;
    }

    public string RelativePath { get; }
    public string Name { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }

    public string ExtensionDisplay =>
        Extension.ToUpperInvariant();

    public string SizeText =>
        SearchFileResult.FormatBytes(
            SizeBytes);

    public string ModifiedText =>
        ModifiedAt.ToString(
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.GetCultureInfo(
                "es-AR"));
}

public sealed class SearchFileResult
{
    public SearchFileResult(
        string name,
        string category,
        long sizeBytes,
        DateTime modifiedAt,
        string location,
        string colorHex,
        bool isDirectory = false,
        int containedFileCount = 1,
        IReadOnlyList<IndexedSearchChild>? contents = null)
    {
        Name =
            name;

        Category =
            category;

        SizeBytes =
            sizeBytes;

        ModifiedAt =
            modifiedAt;

        Location =
            location;

        ColorHex =
            colorHex;

        IsDirectory =
            isDirectory;

        ContainedFileCount =
            containedFileCount;

        FolderContents =
            (contents ?? [])
                .Select(item =>
                    new SearchFolderContentItem(
                        item.RelativePath,
                        item.Name,
                        item.Extension,
                        item.SizeBytes,
                        item.ModifiedAt))
                .ToList();

        ContentSearchText =
            IsDirectory
                ? string.Join(
                    " ",
                    FolderContents.Select(item =>
                        $"{item.RelativePath} {item.Name} {item.ExtensionDisplay}"))
                : string.Empty;
    }

    public string Name { get; private set; }
    public string Category { get; private set; }
    public string ColorHex { get; private set; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string Location { get; private set; }
    public bool IsDirectory { get; }
    public int ContainedFileCount { get; }
    public IReadOnlyList<SearchFolderContentItem> FolderContents { get; }
    public string ContentSearchText { get; }

    public Brush CategoryBrush =>
        CreateCategoryBrush(
            0xFF);

    public Brush CategorySoftBrush =>
        CreateCategoryBrush(
            0x20);

    public string FilePath =>
        System.IO.Path.Combine(
            Location,
            Name);

    public Visibility FolderContentVisibility =>
        IsDirectory
            ? Visibility.Visible
            : Visibility.Collapsed;

    public void Rename(
        string proposedName)
    {
        var value =
            proposedName.Trim();

        if (!IsDirectory &&
            string.IsNullOrWhiteSpace(
                System.IO.Path.GetExtension(
                    value)))
        {
            value +=
                System.IO.Path.GetExtension(
                    Name);
        }

        Name =
            value;
    }

    public void ChangeCategory(
        string categoryName,
        string? colorHex = null)
    {
        Category =
            categoryName;

        if (!string.IsNullOrWhiteSpace(
                colorHex))
        {
            ColorHex =
                colorHex;
        }
    }

    public bool MatchesExtensionFilters(
        IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0)
        {
            return true;
        }

        if (!IsDirectory)
        {
            return extensions.Contains(
                ExtensionDisplay);
        }

        return FolderContents.Any(item =>
            extensions.Contains(
                item.ExtensionDisplay));
    }

    private Brush CreateCategoryBrush(
        byte alpha)
    {
        if (!CategoryColorPalette.TryNormalizeHex(
                ColorHex,
                out var normalized))
        {
            return (Brush)Application.Current.Resources[
                alpha == 0xFF
                    ? "BandaAccentBrush"
                    : "BandaAccentSoftBrush"];
        }

        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(
                alpha,
                Convert.ToByte(
                    normalized.Substring(
                        1,
                        2),
                    16),
                Convert.ToByte(
                    normalized.Substring(
                        3,
                        2),
                    16),
                Convert.ToByte(
                    normalized.Substring(
                        5,
                        2),
                    16)));
    }

    public string ExtensionDisplay =>
        IsDirectory
            ? $"CARPETA · {ContainedFileCount} archivo{(ContainedFileCount == 1 ? string.Empty : "s")}"
            : System.IO.Path.GetExtension(
                    Name)
                .ToUpperInvariant();

    public string SizeText =>
        FormatBytes(
            SizeBytes);

    public string ModifiedText =>
        ModifiedAt.ToString(
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.GetCultureInfo(
                "es-AR"));

    public static string FormatBytes(
        long bytes)
    {
        string[] units =
            ["B", "KB", "MB", "GB", "TB"];

        var value =
            (double)Math.Max(
                0,
                bytes);

        var unitIndex =
            0;

        while (value >= 1024 &&
               unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }
}
