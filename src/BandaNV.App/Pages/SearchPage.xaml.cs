using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;

namespace BandaNV.App.Pages;

public sealed partial class SearchPage : Page
{
    private const int CategoriesPerPage = 10;

    private readonly List<SearchCategorySummary> _allCategoryCards = new();
    private readonly List<SearchFileResult> _allFiles = new();
    private readonly HashSet<string> _selectedCategoryNames =
        new(StringComparer.CurrentCultureIgnoreCase);

    private int _currentCategoryPage;
    private SearchDateFilter _dateFilter = SearchDateFilter.All;
    private SearchSizeFilter _sizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _extensionFilters =
        new(StringComparer.OrdinalIgnoreCase);
    private SearchSortMode _sortMode = SearchSortMode.Newest;

    private SearchDateFilter _pendingDateFilter = SearchDateFilter.All;
    private SearchSizeFilter _pendingSizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _pendingExtensionFilters =
        new(StringComparer.OrdinalIgnoreCase);

    public SearchPage()
    {
        InitializeComponent();

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
        _pendingSizeFilter = SearchSizeFilter.All;
        _pendingExtensionFilters.Clear();

        UpdatePendingFilterLabels();
        BuildExtensionFilterOptions();
    }

    private void ApplyFiltersOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _dateFilter = _pendingDateFilter;
        _sizeFilter = _pendingSizeFilter;

        _extensionFilters.Clear();
        _extensionFilters.UnionWith(_pendingExtensionFilters);

        FiltersOverlay.Visibility = Visibility.Collapsed;
        RefreshSearchResults();
    }

    private void PopulateFilterOverlayControls()
    {
        _pendingDateFilter = _dateFilter;
        _pendingSizeFilter = _sizeFilter;

        _pendingExtensionFilters.Clear();
        _pendingExtensionFilters.UnionWith(_extensionFilters);

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
        DateFilterValueText.Text = GetDateFilterDisplayName(parsedFilter);
        DateFilterFlyout.Hide();
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
        DateFilterValueText.Text = GetDateFilterDisplayName(_pendingDateFilter);
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
                Style = (Style)Resources["BandaPopupOptionButtonStyle"]
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

    private static string GetDateFilterDisplayName(SearchDateFilter filter) =>
        filter switch
        {
            SearchDateFilter.Last24Hours => "Últimas 24 horas",
            SearchDateFilter.Last7Days => "Últimos 7 días",
            SearchDateFilter.Last30Days => "Últimos 30 días",
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
            SearchDateFilter.Last24Hours =>
                query.Where(file => file.ModifiedAt >= now.AddHours(-24)),
            SearchDateFilter.Last7Days =>
                query.Where(file => file.ModifiedAt >= now.AddDays(-7)),
            SearchDateFilter.Last30Days =>
                query.Where(file => file.ModifiedAt >= now.AddDays(-30)),
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

        SearchResultsList.ItemsSource = null;
        SearchResultsList.ItemsSource = results;

        var resultText = results.Count == 1 ? "1 archivo" : $"{results.Count} archivos";
        SearchResultCountText.Text = results.Count.ToString(CultureInfo.CurrentCulture);
        SearchResultsFooterText.Text =
            results.Count == 1 ? "1 resultado" : $"{results.Count} resultados";

        SearchResultsScrollViewer.Visibility =
            results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyStatePanel.Visibility =
            results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

    private List<string> BuildAdvancedFilterDescriptions()
    {
        var descriptions = new List<string>();

        switch (_dateFilter)
        {
            case SearchDateFilter.Last24Hours:
                descriptions.Add("últimas 24 h");
                break;
            case SearchDateFilter.Last7Days:
                descriptions.Add("últimos 7 días");
                break;
            case SearchDateFilter.Last30Days:
                descriptions.Add("últimos 30 días");
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

public enum SearchDateFilter
{
    All,
    Last24Hours,
    Last7Days,
    Last30Days
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

    public string Name { get; }
    public string Category { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string Location { get; }

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
