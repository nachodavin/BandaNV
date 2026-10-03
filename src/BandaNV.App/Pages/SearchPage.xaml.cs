using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BandaNV.App.Pages;

public sealed partial class SearchPage : Page
{
    private const int CategoriesPerPage = 10;

    private readonly List<SearchCategorySummary> _allCategoryCards = new();
    private int _currentCategoryPage;

    public SearchPage()
    {
        InitializeComponent();

        // Datos de maqueta hasta conectar bandanv_config.json.
        // La UI ya usa el mismo orden global que usarán las categorías reales.
        LoadCategories(GetPreviewCategories());
    }

    public void LoadCategories(IEnumerable<CategoryDefinition> categories)
    {
        _allCategoryCards.Clear();

        foreach (var category in categories
                     .OrderBy(category => category.Order)
                     .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _allCategoryCards.Add(new SearchCategorySummary(
                category.Name,
                category.Order,
                BuildExtensionsText(category.Extensions),
                "—"));
        }

        _currentCategoryPage = 0;
        UpdateCategoryPage();
    }

    private int CategoryPageCount =>
        Math.Max(1, (int)Math.Ceiling(_allCategoryCards.Count / (double)CategoriesPerPage));

    private void UpdateCategoryPage()
    {
        CategoryCardsGrid.Children.Clear();
        CategoryCardsGrid.ColumnDefinitions.Clear();

        // Diez columnas iguales hacen que la tanda ocupe todo el ancho disponible
        // sin achicar de más las tarjetas ni dejar espacio muerto a la derecha.
        for (var column = 0; column < CategoriesPerPage; column++)
        {
            CategoryCardsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
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

    private static IReadOnlyList<CategoryDefinition> GetPreviewCategories()
    {
        // Maqueta con 14 categorías para poder probar visualmente el carrusel.
        // Al conectar la configuración real, esta lista se reemplaza por las
        // categorías efectivamente asignadas por el usuario.
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

    private void CategoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string categoryName })
        {
            return;
        }

        // El filtrado real se conecta junto con el motor de búsqueda.
        _ = categoryName;
    }
}

public sealed class SearchCategorySummary
{
    public SearchCategorySummary()
    {
    }

    public SearchCategorySummary(string name, int order, string extensionsText, string countText)
    {
        Name = name;
        Order = order;
        ExtensionsText = extensionsText;
        CountText = countText;
    }

    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string ExtensionsText { get; set; } = "Sin extensiones";
    public string CountText { get; set; } = "—";
}
