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

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategoryNames.Clear();

        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Text = string.Empty;
        }
        else
        {
            UpdateCategoryPage();
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

        var results = query
            .OrderByDescending(file => file.ModifiedAt)
            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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
        var hasAnyFilter = hasCategoryFilters || hasSearchFilter;

        AllCategoriesChip.Visibility =
            hasCategoryFilters ? Visibility.Collapsed : Visibility.Visible;

        ActiveCategoriesChip.Visibility =
            hasCategoryFilters ? Visibility.Visible : Visibility.Collapsed;

        ActiveCategoriesText.Text =
            hasCategoryFilters ? string.Join(" · ", selectedInOrder) : string.Empty;

        NoFiltersText.Text =
            !hasAnyFilter
                ? "Sin filtros"
                : hasCategoryFilters && hasSearchFilter
                    ? $"Categorías seleccionadas · búsqueda activa · {resultText}"
                    : hasCategoryFilters
                        ? $"{selectedInOrder.Count} categoría{(selectedInOrder.Count == 1 ? string.Empty : "s")} seleccionada{(selectedInOrder.Count == 1 ? string.Empty : "s")}"
                        : $"Búsqueda activa · {resultText}";

        ClearFiltersButton.IsEnabled = hasAnyFilter;
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
        var anchor = new DateTime(2026, 10, 4, 8, 45, 31);

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

                var sizeBytes =
                    (category.Order * 6_750_000L) +
                    (index * 1_340_000L);

                var modifiedAt = anchor
                    .AddHours(-category.Order)
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
        Path.GetExtension(Name).ToUpperInvariant();

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
