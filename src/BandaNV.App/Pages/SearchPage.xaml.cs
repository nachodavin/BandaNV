using BandaNV.App.Services;
using BandaNV.Core.Models;
using BandaNV.Core.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
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
    public ObservableCollection<SearchResultGroup> GroupedSearchResults { get; } = new();

    private readonly CollectionViewSource _searchResultsViewSource = new()
    {
        ItemsPath = new PropertyPath(nameof(SearchResultGroup.Items))
    };

    private SearchFileResult? _selectedSearchFile;
    private SearchFileResult? _selectedSearchFolder;
    private bool _syncingFolderContentSelection;
    private string _searchFolderCurrentRelativePath = string.Empty;
    private readonly Stack<string> _searchFolderHistory = new();
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
    private SearchSortField _sortField = SearchSortField.DateModified;
    private SearchSortDirection _sortDirection = SearchSortDirection.Descending;
    private SearchGroupField _groupField = SearchGroupField.DateModified;

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

        _searchResultsViewSource.Source = VisibleSearchResults;
        SearchResultsList.ItemsSource = _searchResultsViewSource.View;

        LoadSavedViewPreferences();

        UpdateSortAndGroupSelectorText();
        UpdateSortAndGroupOptionHighlights();
        InitializeSpecificDateWheels();

        Loaded += SearchPage_Loaded;
    }

    private void LoadSavedViewPreferences()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        _dateFilter =
            Enum.TryParse<SearchDateFilter>(
                settings.SearchDateFilter,
                ignoreCase: true,
                out var dateFilter)
                ? dateFilter
                : SearchDateFilter.All;

        _specificDateFilter =
            _dateFilter == SearchDateFilter.SpecificDate
                ? settings.SearchSpecificDateFilter?.Date
                : null;

        _sizeFilter =
            Enum.TryParse<SearchSizeFilter>(
                settings.SearchSizeFilter,
                ignoreCase: true,
                out var sizeFilter)
                ? sizeFilter
                : SearchSizeFilter.All;

        _extensionFilters.Clear();
        _extensionFilters.UnionWith(
            settings.SearchExtensionFilters ?? []);

        _sortField =
            Enum.TryParse<SearchSortField>(
                settings.SearchSortField,
                ignoreCase: true,
                out var sortField)
                ? sortField
                : SearchSortField.DateModified;

        _sortDirection =
            Enum.TryParse<SearchSortDirection>(
                settings.SearchSortDirection,
                ignoreCase: true,
                out var sortDirection)
                ? sortDirection
                : SearchSortDirection.Descending;

        _groupField =
            Enum.TryParse<SearchGroupField>(
                settings.SearchGroupField,
                ignoreCase: true,
                out var groupField)
                ? groupField
                : SearchGroupField.DateModified;
    }

    private async Task PersistViewPreferencesAsync()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        settings.SearchDateFilter =
            _dateFilter.ToString();
        settings.SearchSpecificDateFilter =
            _dateFilter == SearchDateFilter.SpecificDate
                ? _specificDateFilter?.Date
                : null;
        settings.SearchSizeFilter =
            _sizeFilter.ToString();
        settings.SearchExtensionFilters =
            _extensionFilters
                .OrderBy(
                    extension => extension,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        settings.SearchSortField =
            _sortField.ToString();
        settings.SearchSortDirection =
            _sortDirection.ToString();
        settings.SearchGroupField =
            _groupField.ToString();

        await global::BandaNV.App.App.Settings.SaveAsync(
            settings);
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

        ResetSearchFolderNavigationState();

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

        var destinationRoot =
            global::BandaNV.App.App.Settings.Current.DestinationFolder;

        foreach (var category in orderedCategories)
        {
            var fileCount =
                CategoryService.CountExistingFiles(
                    destinationRoot,
                    category.Order,
                    category.Name);

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

    private async void ApplyFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
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
        await PersistViewPreferencesAsync();
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

    private async void SortFieldOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sortKey })
        {
            return;
        }

        _sortField = sortKey switch
        {
            "Name" => SearchSortField.Name,
            "Size" => SearchSortField.Size,
            "Category" => SearchSortField.Category,
            "Extension" => SearchSortField.Extension,
            _ => SearchSortField.DateModified
        };

        UpdateSortAndGroupSelectorText();
        UpdateSortAndGroupOptionHighlights();
        SortFlyout.Hide();
        RefreshSearchResults();
        await PersistViewPreferencesAsync();
    }

    private async void SortDirectionOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directionKey })
        {
            return;
        }

        _sortDirection = directionKey.Equals(
            "Ascending",
            StringComparison.OrdinalIgnoreCase)
                ? SearchSortDirection.Ascending
                : SearchSortDirection.Descending;

        UpdateSortAndGroupSelectorText();
        UpdateSortAndGroupOptionHighlights();
        SortFlyout.Hide();
        RefreshSearchResults();
        await PersistViewPreferencesAsync();
    }

    private async void GroupFieldOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string groupKey })
        {
            return;
        }

        _groupField = groupKey switch
        {
            "Name" => SearchGroupField.Name,
            "DateModified" => SearchGroupField.DateModified,
            "Size" => SearchGroupField.Size,
            "Category" => SearchGroupField.Category,
            "Extension" => SearchGroupField.Extension,
            _ => SearchGroupField.None
        };

        UpdateSortAndGroupSelectorText();
        UpdateSortAndGroupOptionHighlights();
        GroupFlyout.Hide();
        RefreshSearchResults();
        await PersistViewPreferencesAsync();
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
                        ? file.FolderContents
                            .Where(item =>
                                !item.IsDirectory)
                            .Select(item =>
                                item.ExtensionDisplay.ToLowerInvariant())
                        : new[]
                        {
                            file.ExtensionDisplay.ToLowerInvariant()
                        })
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

    private void UpdateSortAndGroupSelectorText()
    {
        SortValueText.Text =
            $"{GetSortFieldDisplayName(_sortField)} · {GetDirectionShortDisplayName(_sortDirection)}";

        GroupValueText.Text =
            _groupField == SearchGroupField.None
                ? "Ninguno"
                : GetGroupFieldDisplayName(_groupField);
    }

    private void UpdateSortAndGroupOptionHighlights()
    {
        SetPopupOptionSelected(
            SortNameOptionButton,
            _sortField == SearchSortField.Name);
        SetPopupOptionSelected(
            SortDateOptionButton,
            _sortField == SearchSortField.DateModified);
        SetPopupOptionSelected(
            SortSizeOptionButton,
            _sortField == SearchSortField.Size);
        SetPopupOptionSelected(
            SortCategoryOptionButton,
            _sortField == SearchSortField.Category);
        SetPopupOptionSelected(
            SortExtensionOptionButton,
            _sortField == SearchSortField.Extension);

        SetPopupOptionSelected(
            SortAscendingOptionButton,
            _sortDirection == SearchSortDirection.Ascending);
        SetPopupOptionSelected(
            SortDescendingOptionButton,
            _sortDirection == SearchSortDirection.Descending);

        SetPopupOptionSelected(
            GroupNoneOptionButton,
            _groupField == SearchGroupField.None);
        SetPopupOptionSelected(
            GroupNameOptionButton,
            _groupField == SearchGroupField.Name);
        SetPopupOptionSelected(
            GroupDateOptionButton,
            _groupField == SearchGroupField.DateModified);
        SetPopupOptionSelected(
            GroupSizeOptionButton,
            _groupField == SearchGroupField.Size);
        SetPopupOptionSelected(
            GroupCategoryOptionButton,
            _groupField == SearchGroupField.Category);
        SetPopupOptionSelected(
            GroupExtensionOptionButton,
            _groupField == SearchGroupField.Extension);
    }

    private static void SetPopupOptionSelected(
        Button button,
        bool isSelected)
    {
        button.Background =
            isSelected
                ? (Brush)Application.Current.Resources["BandaAccentSoftBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        button.Foreground =
            (Brush)Application.Current.Resources[
                isSelected
                    ? "BandaAccentBrush"
                    : "BandaTextBrush"];
    }

    private static string GetSortFieldDisplayName(SearchSortField field) =>
        field switch
        {
            SearchSortField.Name => "Nombre",
            SearchSortField.Size => "Tamaño",
            SearchSortField.Category => "Categoría",
            SearchSortField.Extension => "Extensión",
            _ => "Fecha de modificación"
        };

    private static string GetGroupFieldDisplayName(SearchGroupField field) =>
        field switch
        {
            SearchGroupField.Name => "Nombre",
            SearchGroupField.DateModified => "Fecha de modificación",
            SearchGroupField.Size => "Tamaño",
            SearchGroupField.Category => "Categoría",
            SearchGroupField.Extension => "Extensión",
            _ => "Ninguno"
        };

    private static string GetDirectionShortDisplayName(
        SearchSortDirection direction) =>
        direction == SearchSortDirection.Ascending
            ? "Asc."
            : "Desc.";

    private async void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
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

        await PersistViewPreferencesAsync();
    }

    private void RefreshSearchResults()
    {
        IEnumerable<SearchFileResult> query =
            IsSearchFolderNavigationActive
                ? GetCurrentSearchFolderItems()
                    .Select(item => item.ActionTarget)
                : _allFiles;

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

        var results = SortSearchResults(
            query,
            categoryOrder);

        var previouslySelected = SearchResultsList.SelectedItems
            .OfType<SearchFileResult>()
            .ToList();

        VisibleSearchResults.Clear();
        foreach (var result in results)
        {
            VisibleSearchResults.Add(result);
        }

        ApplySearchResultsView(
            results,
            categoryOrder);

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
            var hasDestination =
                !string.IsNullOrWhiteSpace(
                    global::BandaNV.App.App.Settings.Current
                        .DestinationFolder);

            if (!hasDestination)
            {
                EmptyStateTitle.Text =
                    "Configurá una carpeta de destino";
                EmptyStateDescription.Text =
                    "Buscar mostrará acá los elementos organizados cuando definas el destino en Configuración.";
            }
            else
            {
                EmptyStateTitle.Text = "No hay elementos que coincidan";
                EmptyStateDescription.Text =
                    _selectedCategoryNames.Count > 0 || !string.IsNullOrWhiteSpace(searchText)
                        ? "Probá cambiando las categorías seleccionadas o el texto de búsqueda."
                        : "Todavía no hay elementos para mostrar.";
            }
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
                ? string.Empty
                : string.Join(" · ", statusParts);

        NoFiltersText.Visibility =
            statusParts.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

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
        ClearFiltersButton.Visibility =
            hasAnyFilter
                ? Visibility.Visible
                : Visibility.Collapsed;
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


    private void SearchResultsList_RightTapped(
        object sender,
        RightTappedRoutedEventArgs e)
    {
        // Se descarta primero la selección lateral para que cualquier
        // cambio de detalle posterior corresponda al listado principal.
        if (SearchFolderContentList.SelectedItems.Count > 0)
        {
            SearchFolderContentList.SelectedItems.Clear();
        }

        if (!BandaContextMenu.SelectForRightClick<SearchFileResult>(
                SearchResultsList, e.OriginalSource))
        {
            return;
        }

        UpdateSearchSelectionDetails();
        ShowSearchContextMenu(SearchResultsList, e);
    }

    private void SearchFolderContentList_RightTapped(
        object sender,
        RightTappedRoutedEventArgs e)
    {
        if (BandaContextMenu.SelectForRightClick<SearchFolderContentItem>(
                SearchFolderContentList, e.OriginalSource))
        {
            ShowSearchContextMenu(SearchFolderContentList, e);
        }
    }

    private void ShowSearchContextMenu(
        ListView list,
        RightTappedRoutedEventArgs e)
    {
        var files = GetSelectedSearchFiles();
        if (files.Count == 0)
        {
            return;
        }

        var menu = BandaContextMenu.Create();
        var one = files.Count == 1;
        var only = one ? files[0] : null;

        if (one)
        {
            BandaContextMenu.Add(
                menu,
                only!.IsDirectory ? "Explorar contenido" : "Abrir archivo",
                "\uE8E5",
                SearchOpenFileButton_Click,
                enabled: SearchOpenFileButton.IsEnabled);
            BandaContextMenu.Add(
                menu,
                "Abrir ubicación",
                "\uE8B7",
                SearchOpenLocationButton_Click,
                enabled: SearchOpenLocationButton.IsEnabled);
            BandaContextMenu.Separator(menu);
        }

        BandaContextMenu.Add(
            menu,
            one ? "Copiar ruta" : $"Copiar {files.Count} rutas",
            "\uE8C8",
            SearchCopyPathButton_Click,
            enabled: SearchCopyPathButton.IsEnabled);
        BandaContextMenu.Add(
            menu,
            "Cambiar categoría",
            "\uE8EC",
            SearchChangeCategoryButton_Click,
            enabled: SearchChangeCategoryButton.IsEnabled);

        if (one)
        {
            BandaContextMenu.Add(
                menu,
                only!.IsDirectory ? "Renombrar carpeta" : "Renombrar archivo",
                "\uE8AC",
                SearchRenameButton_Click,
                enabled: SearchRenameButton.IsEnabled);
        }

        BandaContextMenu.Separator(menu);
        BandaContextMenu.Add(
            menu,
            one
                ? only!.IsDirectory ? "Eliminar carpeta" : "Eliminar archivo"
                : $"Eliminar {files.Count} elementos",
            "\uE74D",
            SearchDeleteButton_Click,
            enabled: SearchDeleteButton.IsEnabled,
            danger: true);

        BandaContextMenu.Show(menu, list, e);
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

        if (file.IsDirectory)
        {
            NavigateIntoSearchFolder(file);
        }
        else
        {
            OpenSearchFile(file);
        }

        e.Handled = true;
    }

    private List<SearchFileResult> SortSearchResults(
        IEnumerable<SearchFileResult> query,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        IOrderedEnumerable<SearchFileResult> ordered =
            _sortField switch
            {
                SearchSortField.Name =>
                    _sortDirection == SearchSortDirection.Ascending
                        ? query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                        : query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenByDescending(file => file.Name, StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Size =>
                    _sortDirection == SearchSortDirection.Ascending
                        ? query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenBy(file => file.SizeBytes)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                        : query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenByDescending(file => file.SizeBytes)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Category =>
                    _sortDirection == SearchSortDirection.Ascending
                        ? query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenBy(file =>
                                categoryOrder.TryGetValue(file.Category, out var order)
                                    ? order
                                    : int.MaxValue)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                        : query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenByDescending(file =>
                                categoryOrder.TryGetValue(file.Category, out var order)
                                    ? order
                                    : int.MinValue)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Extension =>
                    _sortDirection == SearchSortDirection.Ascending
                        ? query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenBy(
                                file => GetSortableExtension(file),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                        : query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenByDescending(
                                file => GetSortableExtension(file),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase),

                _ =>
                    _sortDirection == SearchSortDirection.Ascending
                        ? query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenBy(file => file.ModifiedAt)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                        : query
                            .OrderByDescending(file => file.IsDirectory)
                            .ThenByDescending(file => file.ModifiedAt)
                            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            };

        return ordered.ToList();
    }

    private void ApplySearchResultsView(
        IReadOnlyList<SearchFileResult> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        _searchResultsViewSource.Source =
            null;

        if (_groupField == SearchGroupField.None)
        {
            GroupedSearchResults.Clear();
            _searchResultsViewSource.IsSourceGrouped =
                false;
            _searchResultsViewSource.Source =
                VisibleSearchResults;
        }
        else
        {
            BuildSearchResultGroups(
                results,
                categoryOrder);

            _searchResultsViewSource.IsSourceGrouped =
                true;
            _searchResultsViewSource.Source =
                GroupedSearchResults;
        }

        SearchResultsList.ItemsSource =
            _searchResultsViewSource.View;
    }

    private void BuildSearchResultGroups(
        IReadOnlyList<SearchFileResult> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        GroupedSearchResults.Clear();

        var now =
            DateTime.Now;

        var groups =
            results.GroupBy(file =>
                GetSearchGroupDescriptor(
                    file,
                    now,
                    categoryOrder));

        var orderedGroups =
            _sortDirection == SearchSortDirection.Ascending
                ? groups
                    .OrderBy(group => group.Key.Order)
                    .ThenBy(
                        group => group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase)
                : groups
                    .OrderByDescending(group => group.Key.Order)
                    .ThenByDescending(
                        group => group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in orderedGroups)
        {
            GroupedSearchResults.Add(
                new SearchResultGroup(
                    group.Key.Label,
                    group.ToList()));
        }
    }

    private SearchGroupDescriptor GetSearchGroupDescriptor(
        SearchFileResult file,
        DateTime now,
        IReadOnlyDictionary<string, int> categoryOrder) =>
        _groupField switch
        {
            SearchGroupField.Name =>
                GetNameGroupDescriptor(
                    file.Name),

            SearchGroupField.DateModified =>
                GetDateGroupDescriptor(
                    file.ModifiedAt,
                    now),

            SearchGroupField.Size =>
                GetSizeGroupDescriptor(
                    file),

            SearchGroupField.Category =>
                new SearchGroupDescriptor(
                    file.Category,
                    categoryOrder.TryGetValue(file.Category, out var order)
                        ? order
                        : int.MaxValue),

            SearchGroupField.Extension =>
                file.IsDirectory
                    ? new SearchGroupDescriptor(
                        "Carpeta de archivos",
                        0)
                    : new SearchGroupDescriptor(
                        string.IsNullOrWhiteSpace(
                            System.IO.Path.GetExtension(file.Name))
                            ? "Sin extensión"
                            : System.IO.Path.GetExtension(file.Name).ToUpperInvariant(),
                        1),

            _ =>
                new SearchGroupDescriptor(
                    "Elementos",
                    0)
        };

    private static SearchGroupDescriptor GetNameGroupDescriptor(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new SearchGroupDescriptor(
                "Otros",
                4);
        }

        var normalized =
            name.Trim()
                .Normalize(
                    System.Text.NormalizationForm.FormD);

        var first =
            char.ToUpperInvariant(
                normalized[0]);

        if (char.IsDigit(first))
        {
            return new SearchGroupDescriptor(
                "0–9",
                0);
        }

        if (first is >= 'A' and <= 'H')
        {
            return new SearchGroupDescriptor(
                "A–H",
                1);
        }

        if (first is >= 'I' and <= 'P')
        {
            return new SearchGroupDescriptor(
                "I–P",
                2);
        }

        if (first is >= 'Q' and <= 'Z')
        {
            return new SearchGroupDescriptor(
                "Q–Z",
                3);
        }

        return new SearchGroupDescriptor(
            "Otros",
            4);
    }

    private static SearchGroupDescriptor GetDateGroupDescriptor(
        DateTime modifiedAt,
        DateTime now)
    {
        var date =
            modifiedAt.Date;
        var today =
            now.Date;

        if (date >= today)
        {
            return new SearchGroupDescriptor(
                "Hoy",
                7);
        }

        if (date >= today.AddDays(-1))
        {
            return new SearchGroupDescriptor(
                "Ayer",
                6);
        }

        var weekStart =
            GetStartOfWeek(
                today);

        if (date >= weekStart)
        {
            return new SearchGroupDescriptor(
                "A principios de esta semana",
                5);
        }

        var lastWeekStart =
            weekStart.AddDays(-7);

        if (date >= lastWeekStart)
        {
            return new SearchGroupDescriptor(
                "La semana pasada",
                4);
        }

        var monthStart =
            new DateTime(
                today.Year,
                today.Month,
                1);

        if (date >= monthStart)
        {
            return new SearchGroupDescriptor(
                "A principios de este mes",
                3);
        }

        var lastMonthStart =
            monthStart.AddMonths(-1);

        if (date >= lastMonthStart)
        {
            return new SearchGroupDescriptor(
                "El mes pasado",
                2);
        }

        var yearStart =
            new DateTime(
                today.Year,
                1,
                1);

        if (date >= yearStart)
        {
            return new SearchGroupDescriptor(
                "A principios de este año",
                1);
        }

        return new SearchGroupDescriptor(
            "Hace mucho tiempo",
            0);
    }

    private static DateTime GetStartOfWeek(
        DateTime date)
    {
        var firstDayOfWeek =
            CultureInfo.CurrentCulture
                .DateTimeFormat
                .FirstDayOfWeek;

        var difference =
            (7 +
             ((int)date.DayOfWeek -
              (int)firstDayOfWeek)) %
            7;

        return date.AddDays(
            -difference);
    }

    private static SearchGroupDescriptor GetSizeGroupDescriptor(
        SearchFileResult file)
    {
        if (file.IsDirectory)
        {
            return new SearchGroupDescriptor(
                "Sin especificar",
                0);
        }

        var size =
            file.SizeBytes;

        if (size == 0)
        {
            return new SearchGroupDescriptor(
                "Vacío",
                1);
        }

        const long kilobyte =
            1024L;
        const long megabyte =
            1024L * kilobyte;
        const long gigabyte =
            1024L * megabyte;

        if (size < 16 * kilobyte)
        {
            return new SearchGroupDescriptor(
                "Muy pequeño",
                2);
        }

        if (size < megabyte)
        {
            return new SearchGroupDescriptor(
                "Pequeño",
                3);
        }

        if (size < 128 * megabyte)
        {
            return new SearchGroupDescriptor(
                "Mediano",
                4);
        }

        if (size < gigabyte)
        {
            return new SearchGroupDescriptor(
                "Grande",
                5);
        }

        if (size < 4 * gigabyte)
        {
            return new SearchGroupDescriptor(
                "Muy grande",
                6);
        }

        return new SearchGroupDescriptor(
            "Gigantesco",
            7);
    }

    private static string GetSortableExtension(
        SearchFileResult file) =>
        file.IsDirectory
            ? string.Empty
            : System.IO.Path.GetExtension(
                file.Name);

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

    private List<SearchFileResult> GetSelectedMainSearchFiles() =>
        SearchResultsList.SelectedItems
            .OfType<SearchFileResult>()
            .ToList();

    private List<SearchFileResult> GetSelectedFolderContentFiles() =>
        SearchFolderContentList.SelectedItems
            .OfType<SearchFolderContentItem>()
            .Select(item =>
                item.ActionTarget)
            .ToList();

    private List<SearchFileResult> GetSelectedSearchFiles()
    {
        if (SearchFolderContentsPanel.Visibility == Visibility.Visible)
        {
            var nestedSelection = GetSelectedFolderContentFiles();
            if (nestedSelection.Count > 0)
            {
                return nestedSelection;
            }
        }

        var selected =
            GetSelectedMainSearchFiles();

        if (selected.Count > 0)
        {
            return selected;
        }

        if (IsSearchFolderNavigationActive &&
            _selectedSearchFolder is not null)
        {
            return
            [
                GetCurrentSearchFolderTarget()
            ];
        }

        return [];
    }

    private bool IsCurrentSearchFolder(
        SearchFileResult file) =>
        IsSearchFolderNavigationActive &&
        _selectedSearchFolder is not null &&
        GetCurrentSearchFolderTarget()
            .FilePath.Equals(
                file.FilePath,
                StringComparison.OrdinalIgnoreCase);

    private void UpdateSearchSelectionDetails()
    {
        var selectedFiles =
            GetSelectedMainSearchFiles();

        if (selectedFiles.Count == 0)
        {
            if (IsSearchFolderNavigationActive)
            {
                var currentFolder =
                    GetCurrentSearchFolderTarget();

                _selectedSearchFile =
                    currentFolder;

                ShowSearchFileDetails(
                    currentFolder,
                    preserveFolderContext:
                        true);
                return;
            }

            _selectedSearchFile =
                null;

            ClearSearchFileDetails();
            return;
        }

        if (selectedFiles.Count == 1)
        {
            _selectedSearchFile =
                selectedFiles[0];

            ShowSearchFileDetails(
                selectedFiles[0]);

            return;
        }

        _selectedSearchFile =
            null;

        ShowMultipleSearchFileDetails(
            selectedFiles);
    }

    private void ShowSearchFileDetails(
        SearchFileResult file,
        bool preserveFolderContext = false)
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
                ? IsCurrentSearchFolder(
                    file)
                    ? "Abrir carpeta"
                    : "Abrir contenido"
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

    private void ShowMultipleSearchFileDetails(
        IReadOnlyList<SearchFileResult> files,
        bool preserveFolderContext = false)
    {
        var categoryCount =
            files
                .Select(file =>
                    file.Category)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .Count();

        var extensionCount =
            files
                .Select(file =>
                    file.ExtensionDisplay)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        var locations =
            files
                .Select(file =>
                    file.Location)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        SearchDetailTitleText.Text =
            preserveFolderContext
                ? "Selección dentro de la carpeta"
                : "Selección múltiple";

        SearchDetailFileNameText.Text =
            $"{files.Count} elementos seleccionados";

        SearchDetailCategoryText.Text =
            categoryCount == 1
                ? files[0].Category
                : $"{categoryCount} categorías";

        ApplySearchDetailCategoryVisual(
            categoryCount == 1
                ? files[0]
                : null,
            neutral:
                categoryCount != 1);

        SearchDetailSizeText.Text =
            FormatSearchBytes(
                files.Sum(file =>
                    file.SizeBytes));

        SearchDetailExtensionText.Text =
            extensionCount == 1
                ? files[0].ExtensionDisplay
                : $"{extensionCount} tipos";

        SearchDetailModifiedText.Text =
            "Varias fechas";

        SearchDetailLocationText.Text =
            locations.Count == 1
                ? locations[0]
                : $"{locations.Count} ubicaciones";

        SearchOpenFileButton.Content =
            "Abrir elemento";

        SearchCopyPathButton.Content =
            "Copiar rutas";

        SearchChangeCategoryButton.Content =
            "Cambiar categoría";

        SearchDeleteButton.Content =
            $"Eliminar {files.Count} elementos";

        SearchOpenFileButton.IsEnabled =
            false;
        SearchOpenLocationButton.IsEnabled =
            false;
        SearchCopyPathButton.IsEnabled =
            true;
        SearchChangeCategoryButton.IsEnabled =
            true;
        SearchRenameButton.IsEnabled =
            false;
        SearchDeleteButton.IsEnabled =
            true;

        SearchDetailActionStatusText.Text =
            "Abrir elemento, abrir ubicación y renombrar requieren una selección individual.";

        SearchDetailActionStatusText.Visibility =
            Visibility.Visible;
    }

    private bool IsSearchFolderNavigationActive =>
        _selectedSearchFolder is not null &&
        SearchFolderNavigationBar.Visibility ==
            Visibility.Visible;

    private IEnumerable<SearchFolderContentItem> GetCurrentSearchFolderItems()
    {
        if (_selectedSearchFolder is null)
        {
            return [];
        }

        var currentPath =
            _searchFolderCurrentRelativePath;

        return _selectedSearchFolder.FolderContents
            .Where(item =>
                GetSearchFolderParentRelativePath(
                    item.RelativePath)
                    .Equals(
                        currentPath,
                        StringComparison.OrdinalIgnoreCase));
    }

    private void ShowFolderContentsInPanel(
        SearchFileResult folder)
    {
        _selectedSearchFolder =
            folder;

        _searchFolderCurrentRelativePath =
            string.Empty;
        _searchFolderHistory.Clear();

        SearchFolderNavigationBar.Visibility =
            Visibility.Visible;

        SearchFolderContentsPanel.Visibility =
            Visibility.Collapsed;

        RefreshSearchFolderContentView();
    }

    private void NavigateIntoSearchFolder(
        SearchFileResult folder)
    {
        if (!folder.IsDirectory)
        {
            OpenSearchFile(
                folder);
            return;
        }

        if (!IsSearchFolderNavigationActive ||
            _selectedSearchFolder is null)
        {
            ShowFolderContentsInPanel(
                folder);
            return;
        }

        var currentFolder =
            GetCurrentSearchFolderTarget();

        if (currentFolder.FilePath.Equals(
                folder.FilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nestedFolder =
            _selectedSearchFolder.FolderContents
                .FirstOrDefault(item =>
                    item.IsDirectory &&
                    item.ActionTarget.FilePath.Equals(
                        folder.FilePath,
                        StringComparison.OrdinalIgnoreCase));

        if (nestedFolder is null)
        {
            return;
        }

        _searchFolderCurrentRelativePath =
            nestedFolder.RelativePath;

        RefreshSearchFolderContentView();
    }

    private void RefreshSearchFolderContentView()
    {
        if (_selectedSearchFolder is null)
        {
            return;
        }

        SearchFolderNavigationBar.Visibility =
            Visibility.Visible;

        var pathParts =
            new List<string>
            {
                "Buscar",
                _selectedSearchFolder.Name
            };

        if (!string.IsNullOrWhiteSpace(
                _searchFolderCurrentRelativePath))
        {
            pathParts.AddRange(
                _searchFolderCurrentRelativePath.Split(
                    [
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar
                    ],
                    StringSplitOptions.RemoveEmptyEntries));
        }

        SearchFolderNavigationPathText.Text =
            string.Join(
                "  ›  ",
                pathParts);

        RefreshSearchResults();
    }

    private void ResetSearchFolderNavigationState()
    {
        _selectedSearchFolder =
            null;
        _searchFolderCurrentRelativePath =
            string.Empty;
        _searchFolderHistory.Clear();

        SearchFolderNavigationBar.Visibility =
            Visibility.Collapsed;
        SearchFolderNavigationPathText.Text =
            "Buscar";

        SearchFolderContentsPanel.Visibility =
            Visibility.Collapsed;
    }

    private void ExitSearchFolderNavigation(
        SearchFileResult? folderToReselect = null)
    {
        var targetPath =
            folderToReselect?.FilePath;

        ResetSearchFolderNavigationState();
        RefreshSearchResults();

        if (string.IsNullOrWhiteSpace(
                targetPath))
        {
            return;
        }

        var target =
            VisibleSearchResults.FirstOrDefault(file =>
                file.FilePath.Equals(
                    targetPath,
                    StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            SearchResultsList.SelectedItem =
                target;
        }
    }

    private void ResetFolderContentSelection(
        bool hidePanel)
    {
        _syncingFolderContentSelection =
            true;

        try
        {
            SearchFolderContentList.SelectedItems.Clear();

            if (hidePanel)
            {
                SearchFolderContentList.ItemsSource =
                    null;
            }
        }
        finally
        {
            _syncingFolderContentSelection =
                false;
        }

        if (hidePanel)
        {
            SearchFolderContentsPanel.Visibility =
                Visibility.Collapsed;
        }
    }

    private void SearchFolderContentList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncingFolderContentSelection ||
            _selectedSearchFolder is null)
        {
            return;
        }

        var selectedFiles =
            GetSelectedFolderContentFiles();

        SearchFolderContentsSelectionText.Text =
            selectedFiles.Count switch
            {
                0 =>
                    "Seleccioná para gestionar",
                1 =>
                    "1 seleccionado",
                _ =>
                    $"{selectedFiles.Count} seleccionados"
            };

        if (selectedFiles.Count == 0)
        {
            var currentFolder =
                GetCurrentSearchFolderTarget();

            _selectedSearchFile =
                currentFolder;

            ShowSearchFileDetails(
                currentFolder,
                preserveFolderContext:
                    true);

            return;
        }

        if (selectedFiles.Count == 1)
        {
            _selectedSearchFile =
                selectedFiles[0];

            ShowSearchFileDetails(
                selectedFiles[0],
                preserveFolderContext:
                    true);

            return;
        }

        _selectedSearchFile =
            null;

        ShowMultipleSearchFileDetails(
            selectedFiles,
            preserveFolderContext:
                true);
    }

    private void SearchFolderContentList_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var current =
            e.OriginalSource as DependencyObject;

        while (current is not null &&
               current != SearchFolderContentList)
        {
            if (current is ListViewItem)
            {
                return;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        SearchFolderContentList.SelectedItems.Clear();
    }

    private void SearchFolderContentList_DoubleTapped(
        object sender,
        Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var item =
            GetFolderContentItemFromEventSource(
                e.OriginalSource);

        if (item is null ||
            _selectedSearchFolder is null)
        {
            return;
        }

        if (!item.IsDirectory)
        {
            OpenSearchFile(
                item.ActionTarget);

            e.Handled =
                true;
            return;
        }

        _searchFolderHistory.Push(
            _searchFolderCurrentRelativePath);

        _searchFolderCurrentRelativePath =
            item.RelativePath;

        RefreshSearchFolderContentView();

        _selectedSearchFile =
            item.ActionTarget;

        ShowSearchFileDetails(
            item.ActionTarget,
            preserveFolderContext:
                true);

        e.Handled =
            true;
    }

    private void SearchFolderContentsBackButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsSearchFolderNavigationActive ||
            _selectedSearchFolder is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                _searchFolderCurrentRelativePath))
        {
            ExitSearchFolderNavigation(
                _selectedSearchFolder);
            return;
        }

        _searchFolderCurrentRelativePath =
            GetSearchFolderParentRelativePath(
                _searchFolderCurrentRelativePath);

        RefreshSearchFolderContentView();
    }

    private void SearchFolderContentSelectAllAccelerator_Invoked(
        KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var item in SearchFolderContentList.Items
                     .OfType<SearchFolderContentItem>())
        {
            if (!SearchFolderContentList.SelectedItems.Contains(
                    item))
            {
                SearchFolderContentList.SelectedItems.Add(
                    item);
            }
        }

        args.Handled =
            true;
    }

    private SearchFileResult GetCurrentSearchFolderTarget()
    {
        if (_selectedSearchFolder is null ||
            string.IsNullOrWhiteSpace(
                _searchFolderCurrentRelativePath))
        {
            return _selectedSearchFolder!;
        }

        return _selectedSearchFolder.FolderContents
                   .FirstOrDefault(item =>
                       item.IsDirectory &&
                       item.RelativePath.Equals(
                           _searchFolderCurrentRelativePath,
                           StringComparison.OrdinalIgnoreCase))
                   ?.ActionTarget ??
               _selectedSearchFolder;
    }

    private static string GetSearchFolderParentRelativePath(
        string relativePath)
    {
        var parent =
            System.IO.Path.GetDirectoryName(
                relativePath);

        return string.IsNullOrWhiteSpace(
                parent)
            ? string.Empty
            : parent;
    }

    private SearchFolderContentItem? GetFolderContentItemFromEventSource(
        object? source)
    {
        var current =
            source as DependencyObject;

        while (current is not null &&
               current != SearchFolderContentList)
        {
            if (current is FrameworkElement
                {
                    DataContext:
                        SearchFolderContentItem item
                })
            {
                return item;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return null;
    }

    private void ClearSearchFileDetails()
    {
        SearchDetailTitleText.Text =
            "Detalle del elemento";

        SearchDetailFileNameText.Text =
            "Seleccioná uno o varios elementos";

        SearchDetailCategoryText.Text =
            "—";

        ApplySearchDetailCategoryVisual(
            null,
            neutral:
                true);

        SearchDetailSizeText.Text =
            "—";

        SearchDetailExtensionText.Text =
            "—";

        SearchDetailModifiedText.Text =
            "—";

        SearchDetailLocationText.Text =
            "—";

        SearchDetailActionStatusText.Text =
            string.Empty;

        SearchDetailActionStatusText.Visibility =
            Visibility.Collapsed;

        SearchOpenFileButton.Content =
            "Abrir archivo";

        SearchCopyPathButton.Content =
            "Copiar ruta";

        SearchChangeCategoryButton.Content =
            "Cambiar categoría";

        SearchDeleteButton.Content =
            "Eliminar archivo";

        SearchOpenFileButton.IsEnabled =
            false;
        SearchOpenLocationButton.IsEnabled =
            false;
        SearchCopyPathButton.IsEnabled =
            false;
        SearchChangeCategoryButton.IsEnabled =
            false;
        SearchRenameButton.IsEnabled =
            false;
        SearchDeleteButton.IsEnabled =
            false;
    }

    private void SearchOpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSearchFile is not { } file)
        {
            return;
        }

        if (file.IsDirectory &&
            !IsCurrentSearchFolder(
                file))
        {
            NavigateIntoSearchFolder(
                file);
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
            settings.UseRecycleBin
                ? "Enviar a Papelera"
                : files.Count == 1
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

    private sealed record SearchGroupDescriptor(
        string Label,
        int Order);

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

public enum SearchSortField
{
    Name,
    DateModified,
    Size,
    Category,
    Extension
}

public enum SearchSortDirection
{
    Ascending,
    Descending
}

public enum SearchGroupField
{
    None,
    Name,
    DateModified,
    Size,
    Category,
    Extension
}

public sealed class SearchResultGroup
{
    public SearchResultGroup(
        string name,
        IReadOnlyList<SearchFileResult> items)
    {
        Name =
            name;

        Items =
            new ObservableCollection<SearchFileResult>(
                items);
    }

    public string Name { get; }
    public ObservableCollection<SearchFileResult> Items { get; }

    public string HeaderText =>
        $"{Name} ({Items.Count})";
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
        DateTime modifiedAt,
        bool isDirectory,
        int containedFileCount,
        SearchFileResult actionTarget)
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

        IsDirectory =
            isDirectory;

        ContainedFileCount =
            containedFileCount;

        ActionTarget =
            actionTarget;
    }

    public string RelativePath { get; }
    public string Name { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public bool IsDirectory { get; }
    public int ContainedFileCount { get; }
    public SearchFileResult ActionTarget { get; }

    public string ExtensionDisplay =>
        IsDirectory
            ? "CARPETA"
            : Extension.ToUpperInvariant();

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

        var folderPath =
            System.IO.Path.Combine(
                location,
                name);

        FolderContents =
            (contents ?? [])
                .Select(item =>
                {
                    var fullPath =
                        System.IO.Path.GetFullPath(
                            System.IO.Path.Combine(
                                folderPath,
                                item.RelativePath));

                    var childLocation =
                        System.IO.Path.GetDirectoryName(
                            fullPath) ??
                        folderPath;

                    var actionTarget =
                        new SearchFileResult(
                            item.Name,
                            category,
                            item.SizeBytes,
                            item.ModifiedAt,
                            childLocation,
                            colorHex,
                            isDirectory:
                                item.IsDirectory,
                            containedFileCount:
                                item.ContainedFileCount);

                    return new SearchFolderContentItem(
                        item.RelativePath,
                        item.Name,
                        item.Extension,
                        item.SizeBytes,
                        item.ModifiedAt,
                        item.IsDirectory,
                        item.ContainedFileCount,
                        actionTarget);
                })
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
            return extensions.Any(extension =>
                extension.Equals(
                    ExtensionDisplay,
                    StringComparison.OrdinalIgnoreCase));
        }

        return FolderContents.Any(item =>
            extensions.Any(extension =>
                extension.Equals(
                    item.ExtensionDisplay,
                    StringComparison.OrdinalIgnoreCase)));
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
