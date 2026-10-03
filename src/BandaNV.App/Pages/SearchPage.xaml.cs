using System.Collections.ObjectModel;
using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BandaNV.App.Pages;

public sealed partial class SearchPage : Page
{
    public ObservableCollection<SearchCategorySummary> CategoryCards { get; } = new();

    public SearchPage()
    {
        InitializeComponent();

        // Datos de maqueta hasta conectar bandanv_config.json.
        // La UI ya funciona con una colección dinámica y respeta CategoryDefinition.Order.
        LoadCategories(GetPreviewCategories());
    }

    public void LoadCategories(IEnumerable<CategoryDefinition> categories)
    {
        CategoryCards.Clear();

        foreach (var category in categories
                     .OrderBy(category => category.Order)
                     .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            CategoryCards.Add(new SearchCategorySummary(
                category.Name,
                category.Order,
                BuildExtensionsText(category.Extensions),
                "—"));
        }
    }

    private static IReadOnlyList<CategoryDefinition> GetPreviewCategories()
    {
        // Mismo concepto de orden global que usará la configuración real.
        // Cuando migremos el motor, esta lista desaparece y se reemplaza por las
        // categorías efectivamente configuradas por el usuario.
        return
        [
            new("RAR", [".zip", ".rar", ".7z"], 1),
            new("INSTALLERS", [".exe", ".msi", ".bat"], 2),
            new("DOCUMENTS", [".pdf", ".docx", ".xlsx", ".txt"], 3),
            new("IMAGES", [".jpg", ".jpeg", ".png", ".webp", ".avif"], 4),
            new("GIF", [".gif"], 5),
            new("VIDEOS", [".mp4", ".mkv", ".mov", ".avi"], 6),
            new("AUDIO", [".mp3", ".wav", ".flac", ".aac", ".ogg"], 7)
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
        // La tarjeta ya entrega el nombre exacto de la categoría seleccionada.
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
