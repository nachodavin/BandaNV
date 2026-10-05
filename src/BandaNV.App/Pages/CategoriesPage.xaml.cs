using System.Collections.ObjectModel;
using System.Globalization;
using BandaNV.Core.Models;
using BandaNV.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class CategoriesPage : Page
{
    private readonly List<CategoryAdminItem> _allCategories = new();
    private readonly List<string> _editorExtensions = new();

    public ObservableCollection<CategoryAdminItem> VisibleCategories { get; } = new();

    private CategoryAdminItem? _selectedCategory;
    private CategoryAdminItem? _editorCategory;
    private CategoryAdminItem? _pendingDeleteCategory;
    private CategoryEditorMode _editorMode = CategoryEditorMode.Create;

    public CategoriesPage()
    {
        InitializeComponent();

        LoadPersistentCategories();
        RefreshCategoryList();
        UpdateCategoryMetrics();
        ClearCategoryDetails();
    }

    private void LoadPersistentCategories()
    {
        _allCategories.Clear();

        var destinationRoot =
            global::BandaNV.App.App.Settings.Current.DestinationFolder;

        foreach (var category in global::BandaNV.App.App.Categories.GetAll())
        {
            _allCategories.Add(new CategoryAdminItem(
                category.Id,
                category.Name,
                category.Extensions,
                category.Order,
                CategoryService.CountExistingFiles(
                    destinationRoot,
                    category.Order,
                    category.Name),
                CategoryService.GetFolderPath(
                    destinationRoot,
                    category.Order,
                    category.Name)));
        }
    }

    private void CategorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshCategoryList();
    }

    private void RefreshCategoryList(CategoryAdminItem? preferredSelection = null)
    {
        var selected = preferredSelection ?? _selectedCategory;
        var searchText = CategorySearchBox?.Text?.Trim() ?? string.Empty;

        IEnumerable<CategoryAdminItem> query = _allCategories
            .OrderBy(category => category.Order);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(category =>
                category.Name.Contains(
                    searchText,
                    StringComparison.CurrentCultureIgnoreCase) ||
                category.Extensions.Any(extension =>
                    extension.Contains(
                        searchText,
                        StringComparison.CurrentCultureIgnoreCase)));
        }

        var results = query.ToList();

        VisibleCategories.Clear();
        foreach (var category in results)
        {
            VisibleCategories.Add(category);
        }

        var canReorder = string.IsNullOrWhiteSpace(searchText);
        CategoryList.CanDragItems = canReorder;
        CategoryList.CanReorderItems = canReorder;
        CategoryList.AllowDrop = canReorder;

        CategoryResultCountText.Text =
            results.Count.ToString(CultureInfo.CurrentCulture);
        CategoryResultCountLabel.Text =
            results.Count == 1 ? "categoría" : "categorías";
        CategoryFooterCountText.Text =
            results.Count == 1
                ? "1 categoría"
                : $"{results.Count} categorías";

        CategoryList.Visibility =
            results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CategoriesEmptyStatePanel.Visibility =
            results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selected is not null && results.Contains(selected))
        {
            CategoryList.SelectedItem = selected;
        }
        else
        {
            CategoryList.SelectedItem = null;
            _selectedCategory = null;
            ClearCategoryDetails();
        }
    }

    private void CategoryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CategoryAdminItem category)
        {
            _selectedCategory = null;
            ClearCategoryDetails();
            return;
        }

        _selectedCategory = category;
        ShowCategoryDetails(category);
    }

    private async void CategoryList_DragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(CategorySearchBox.Text))
        {
            return;
        }

        var reordered = VisibleCategories.ToList();

        _allCategories.Clear();
        _allCategories.AddRange(reordered);
        NormalizeCategoryOrder();

        if (!await PersistCategoriesAsync())
        {
            ReloadCategoriesAfterSyncFailure();
            return;
        }

        RefreshDerivedCategoryData();
        RefreshCategoryList(_selectedCategory);
        UpdateCategoryMetrics();
    }

    private void NormalizeCategoryOrder()
    {
        for (var index = 0; index < _allCategories.Count; index++)
        {
            _allCategories[index].Order = index + 1;
        }
    }

    private void ShowCategoryDetails(CategoryAdminItem category)
    {
        CategoryDetailNameText.Text = category.Name;
        CategoryDetailOrderText.Text = $"#{category.Order:00}";
        CategoryDetailExtensionCountText.Text =
            category.Extensions.Count.ToString(CultureInfo.CurrentCulture);
        CategoryDetailFileCountText.Text =
            category.FileCount.ToString(CultureInfo.CurrentCulture);
        CategoryDetailFolderText.Text = category.FolderPath;

        BuildCategoryDetailExtensionBadges(category);

        EditCategoryButton.IsEnabled = true;
        ManageExtensionsButton.IsEnabled = true;
        DuplicateCategoryButton.IsEnabled = true;
        DeleteCategoryButton.IsEnabled = true;

        CategoryDetailStatusText.Text =
            "Arrastrá una fila desde el indicador ⋮⋮ para cambiar el orden global.";
        CategoryDetailStatusText.Foreground =
            (Brush)Application.Current.Resources["BandaMutedBrush"];
        CategoryDetailStatusText.Visibility = Visibility.Visible;
    }

    private void ClearCategoryDetails()
    {
        CategoryDetailNameText.Text = "Seleccioná una categoría";
        CategoryDetailOrderText.Text = "—";
        CategoryDetailExtensionCountText.Text = "—";
        CategoryDetailFileCountText.Text = "—";
        CategoryDetailFolderText.Text = "—";
        CategoryDetailExtensionsPanel.Children.Clear();

        EditCategoryButton.IsEnabled = false;
        ManageExtensionsButton.IsEnabled = false;
        DuplicateCategoryButton.IsEnabled = false;
        DeleteCategoryButton.IsEnabled = false;

        CategoryDetailStatusText.Text =
            "Seleccioná una categoría para ver y administrar su configuración.";
        CategoryDetailStatusText.Visibility = Visibility.Visible;
    }

    private void BuildCategoryDetailExtensionBadges(CategoryAdminItem category)
    {
        CategoryDetailExtensionsPanel.Children.Clear();

        foreach (var extension in category.Extensions)
        {
            CategoryDetailExtensionsPanel.Children.Add(
                CreateExtensionBadge(extension));
        }
    }

    private Border CreateExtensionBadge(string extension)
    {
        return new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(9),
            Background =
                (Brush)Application.Current.Resources["BandaAccentSoftBrush"],
            Child = new TextBlock
            {
                Text = extension,
                Foreground =
                    (Brush)Application.Current.Resources["BandaAccentBrush"],
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void UpdateCategoryMetrics()
    {
        TotalCategoriesText.Text =
            _allCategories.Count.ToString(CultureInfo.CurrentCulture);

        var assignedExtensionCount = _allCategories
            .SelectMany(category => category.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        TotalExtensionsText.Text =
            assignedExtensionCount.ToString(CultureInfo.CurrentCulture);

        TotalCategoryFilesText.Text =
            _allCategories
                .Sum(category => category.FileCount)
                .ToString(CultureInfo.CurrentCulture);

        // Este valor será alimentado por AnalyzeAsync cuando conectemos
        // el escaneo real del origen. Categorías ya no inventa extensiones.
        UnassignedExtensionsText.Text = "0";
    }

    private void NewCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenCategoryEditor(
            CategoryEditorMode.Create,
            null,
            string.Empty,
            []);
    }

    private void EditCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is not { } category)
        {
            return;
        }

        OpenCategoryEditor(
            CategoryEditorMode.Edit,
            category,
            category.Name,
            category.Extensions);
    }

    private void ManageExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is not { } category)
        {
            return;
        }

        OpenCategoryEditor(
            CategoryEditorMode.Extensions,
            category,
            category.Name,
            category.Extensions);
    }

    private void DuplicateCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is not { } category)
        {
            return;
        }

        var copyName = BuildUniqueCategoryName($"{category.Name} COPIA");

        OpenCategoryEditor(
            CategoryEditorMode.Duplicate,
            null,
            copyName,
            category.Extensions);
    }

    private string BuildUniqueCategoryName(string baseName)
    {
        var candidate = baseName;
        var suffix = 2;

        while (_allCategories.Any(category =>
                   category.Name.Equals(
                       candidate,
                       StringComparison.CurrentCultureIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private void OpenCategoryEditor(
        CategoryEditorMode mode,
        CategoryAdminItem? category,
        string name,
        IEnumerable<string> extensions)
    {
        _editorMode = mode;
        _editorCategory = category;

        _editorExtensions.Clear();
        _editorExtensions.AddRange(
            extensions
                .Select(NormalizeExtension)
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        CategoryEditorValidationText.Text = string.Empty;
        CategoryEditorValidationText.Visibility = Visibility.Collapsed;
        CategoryExtensionInputTextBox.Text = string.Empty;
        CategoryEditorNameTextBox.Text = name;
        CategoryEditorNameTextBox.IsReadOnly =
            mode == CategoryEditorMode.Extensions;

        switch (mode)
        {
            case CategoryEditorMode.Edit:
                CategoryEditorIconText.Text = "✎";
                CategoryEditorTitleText.Text = "Editar categoría";
                CategoryEditorSubtitleText.Text =
                    "Actualizá el nombre o las extensiones de esta categoría.";
                SaveCategoryEditorButton.Content = "Guardar cambios";
                break;

            case CategoryEditorMode.Extensions:
                CategoryEditorIconText.Text = "·";
                CategoryEditorTitleText.Text = "Administrar extensiones";
                CategoryEditorSubtitleText.Text =
                    "Agregá o quitá extensiones sin modificar el nombre de la categoría.";
                SaveCategoryEditorButton.Content = "Guardar extensiones";
                break;

            case CategoryEditorMode.Duplicate:
                CategoryEditorIconText.Text = "⧉";
                CategoryEditorTitleText.Text = "Duplicar categoría";
                CategoryEditorSubtitleText.Text =
                    "La copia conserva las extensiones como punto de partida. Ajustalas para evitar conflictos.";
                SaveCategoryEditorButton.Content = "Crear copia";
                break;

            default:
                CategoryEditorIconText.Text = "+";
                CategoryEditorTitleText.Text = "Nueva categoría";
                CategoryEditorSubtitleText.Text =
                    "Definí el nombre y las extensiones que va a reconocer BandaNV.";
                SaveCategoryEditorButton.Content = "Crear categoría";
                break;
        }

        BuildCategoryEditorExtensionBadges();

        CategoryEditorOverlay.Visibility = Visibility.Visible;

        if (mode == CategoryEditorMode.Extensions)
        {
            CategoryExtensionInputTextBox.Focus(FocusState.Programmatic);
        }
        else
        {
            CategoryEditorNameTextBox.Focus(FocusState.Programmatic);
            CategoryEditorNameTextBox.SelectAll();
        }
    }

    private void BuildCategoryEditorExtensionBadges()
    {
        CategoryEditorExtensionsPanel.Children.Clear();

        if (_editorExtensions.Count == 0)
        {
            CategoryEditorExtensionsPanel.Children.Add(new TextBlock
            {
                Text = "Sin extensiones todavía",
                Foreground =
                    (Brush)Application.Current.Resources["BandaMutedBrush"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            });
            return;
        }

        foreach (var extension in _editorExtensions
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var container = new Border
            {
                Padding = new Thickness(8, 4, 4, 4),
                CornerRadius = new CornerRadius(9),
                Background =
                    (Brush)Application.Current.Resources["BandaAccentSoftBrush"]
            };

            var grid = new Grid
            {
                ColumnSpacing = 5
            };

            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = extension,
                Foreground =
                    (Brush)Application.Current.Resources["BandaAccentBrush"],
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var removeButton = new Button
            {
                Tag = extension,
                Content = "×",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground =
                    (Brush)Application.Current.Resources["BandaMutedStrongBrush"],
                FontSize = 15
            };

            removeButton.Click += RemoveCategoryExtensionButton_Click;
            Grid.SetColumn(removeButton, 1);

            grid.Children.Add(label);
            grid.Children.Add(removeButton);
            container.Child = grid;

            CategoryEditorExtensionsPanel.Children.Add(container);
        }
    }

    private void AddCategoryExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        AddExtensionsFromEditorInput();
    }

    private void CategoryExtensionInputTextBox_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        AddExtensionsFromEditorInput();
        e.Handled = true;
    }

    private void AddExtensionsFromEditorInput()
    {
        var input = CategoryExtensionInputTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var values = input.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        var addedAny = false;

        foreach (var value in values)
        {
            var extension = NormalizeExtension(value);

            if (!IsValidExtension(extension))
            {
                CategoryEditorValidationText.Text =
                    $"“{value}” no parece una extensión válida.";
                CategoryEditorValidationText.Visibility = Visibility.Visible;
                return;
            }

            if (_editorExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _editorExtensions.Add(extension);
            addedAny = true;
        }

        if (addedAny)
        {
            CategoryEditorValidationText.Visibility = Visibility.Collapsed;
            CategoryExtensionInputTextBox.Text = string.Empty;
            BuildCategoryEditorExtensionBadges();
        }
    }

    private static string NormalizeExtension(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.StartsWith('.')
            ? normalized
            : $".{normalized}";
    }

    private static bool IsValidExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length < 2 ||
            !extension.StartsWith('.'))
        {
            return false;
        }

        return !extension.Any(character =>
            char.IsWhiteSpace(character) ||
            character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|');
    }

    private void RemoveCategoryExtensionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string extension })
        {
            return;
        }

        _editorExtensions.RemoveAll(value =>
            value.Equals(extension, StringComparison.OrdinalIgnoreCase));

        BuildCategoryEditorExtensionBadges();
    }

    private async void SaveCategoryEditorButton_Click(object sender, RoutedEventArgs e)
    {
        CategoryEditorValidationText.Visibility = Visibility.Collapsed;

        var name = CategoryEditorNameTextBox.Text?.Trim().ToUpperInvariant() ??
                   string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowCategoryEditorValidation("Escribí un nombre para la categoría.");
            return;
        }

        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowCategoryEditorValidation(
                "El nombre contiene caracteres que no pueden usarse en una carpeta.");
            return;
        }

        var existingWithSameName = _allCategories.FirstOrDefault(category =>
            !ReferenceEquals(category, _editorCategory) &&
            category.Name.Equals(
                name,
                StringComparison.CurrentCultureIgnoreCase));

        if (existingWithSameName is not null)
        {
            ShowCategoryEditorValidation(
                $"Ya existe una categoría llamada {existingWithSameName.Name}.");
            return;
        }

        if (_editorExtensions.Count == 0)
        {
            ShowCategoryEditorValidation(
                "Agregá al menos una extensión antes de guardar.");
            return;
        }

        var conflicts = _editorExtensions
            .Select(extension => new
            {
                Extension = extension,
                Owner = _allCategories.FirstOrDefault(category =>
                    !ReferenceEquals(category, _editorCategory) &&
                    category.Extensions.Contains(
                        extension,
                        StringComparer.OrdinalIgnoreCase))
            })
            .Where(item => item.Owner is not null)
            .ToList();

        if (conflicts.Count > 0)
        {
            var first = conflicts[0];
            ShowCategoryEditorValidation(
                $"{first.Extension} ya pertenece a {first.Owner!.Name}. " +
                "Una extensión solo puede estar asignada a una categoría.");
            return;
        }

        CategoryAdminItem savedCategory;

        if (_editorMode is CategoryEditorMode.Create or CategoryEditorMode.Duplicate)
        {
            var order = _allCategories.Count + 1;
            var destinationRoot =
                global::BandaNV.App.App.Settings.Current.DestinationFolder;

            savedCategory = new CategoryAdminItem(
                Guid.NewGuid().ToString("D"),
                name,
                _editorExtensions,
                order,
                0,
                CategoryService.GetFolderPath(
                    destinationRoot,
                    order,
                    name));

            _allCategories.Add(savedCategory);
        }
        else
        {
            if (_editorCategory is null)
            {
                CloseCategoryEditor();
                return;
            }

            _editorCategory.Name = name;
            _editorCategory.ReplaceExtensions(_editorExtensions);
            savedCategory = _editorCategory;
        }

        NormalizeCategoryOrder();

        if (!await PersistCategoriesAsync())
        {
            ReloadCategoriesAfterSyncFailure();
            CloseCategoryEditor();
            return;
        }

        RefreshDerivedCategoryData();

        CloseCategoryEditor();
        RefreshCategoryList(savedCategory);
        UpdateCategoryMetrics();

        _selectedCategory = savedCategory;
        CategoryList.SelectedItem = savedCategory;
        ShowCategoryDetails(savedCategory);
    }

    private void ShowCategoryEditorValidation(string message)
    {
        CategoryEditorValidationText.Text = message;
        CategoryEditorValidationText.Visibility = Visibility.Visible;
    }

    private void CategoryEditorBackdrop_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        CloseCategoryEditor();
    }

    private void CloseCategoryEditorButton_Click(object sender, RoutedEventArgs e)
    {
        CloseCategoryEditor();
    }

    private void CloseCategoryEditor()
    {
        CategoryEditorOverlay.Visibility = Visibility.Collapsed;
        CategoryEditorValidationText.Visibility = Visibility.Collapsed;
        _editorExtensions.Clear();
        _editorCategory = null;
        _editorMode = CategoryEditorMode.Create;
    }

    private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is not { } category)
        {
            return;
        }

        _pendingDeleteCategory = category;

        DeleteCategorySubtitleText.Text = category.Name;
        DeleteCategoryBodyText.Text =
            $"¿Eliminar la categoría {category.Name}? " +
            $"Sus {category.Extensions.Count} extensiones pasarán a quedar sin categoría. " +
            $"Los {category.FileCount} archivos ya organizados no se eliminan de la PC.";

        DeleteCategoryOverlay.Visibility = Visibility.Visible;
    }

    private async void ConfirmDeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteCategory is not { } category)
        {
            CloseDeleteCategoryOverlay();
            return;
        }

        _allCategories.Remove(category);
        _selectedCategory = null;

        NormalizeCategoryOrder();

        if (!await PersistCategoriesAsync())
        {
            ReloadCategoriesAfterSyncFailure();
            CloseDeleteCategoryOverlay();
            return;
        }

        RefreshDerivedCategoryData();

        CloseDeleteCategoryOverlay();
        RefreshCategoryList();
        UpdateCategoryMetrics();
        ClearCategoryDetails();
    }

    private void DeleteCategoryBackdrop_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        CloseDeleteCategoryOverlay();
    }

    private void CloseDeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDeleteCategoryOverlay();
    }

    private void CloseDeleteCategoryOverlay()
    {
        DeleteCategoryOverlay.Visibility = Visibility.Collapsed;
        _pendingDeleteCategory = null;
    }

    private void RefreshDerivedCategoryData()
    {
        var destinationRoot =
            global::BandaNV.App.App.Settings.Current.DestinationFolder;

        foreach (var category in _allCategories)
        {
            category.UpdateDerivedState(destinationRoot);
        }
    }

    private async Task<bool> PersistCategoriesAsync()
    {
        var previousCategories =
            global::BandaNV.App.App.Categories.GetAll();

        var nextCategories = _allCategories
            .OrderBy(category => category.Order)
            .Select(category => category.ToSettings())
            .ToList();

        try
        {
            var sync =
                await global::BandaNV.App.App.CategoryFolders.SynchronizeAsync(
                    global::BandaNV.App.App.Settings.Current,
                    previousCategories,
                    nextCategories);

            if (!sync.Success)
            {
                CategoryDetailStatusText.Text =
                    sync.ErrorMessage ??
                    "No se pudieron sincronizar las carpetas físicas.";
                CategoryDetailStatusText.Foreground =
                    (Brush)Application.Current.Resources["BandaDangerBrush"];
                CategoryDetailStatusText.Visibility = Visibility.Visible;
                return false;
            }

            await global::BandaNV.App.App.Categories.SaveAllAsync(
                nextCategories);

            CategoryDetailStatusText.Foreground =
                (Brush)Application.Current.Resources["BandaMutedBrush"];

            if (sync.PreservedDeletedFolders > 0)
            {
                CategoryDetailStatusText.Text =
                    sync.PreservedDeletedFolders == 1
                        ? "La categoría se eliminó, pero su carpeta con archivos se conservó intacta."
                        : $"{sync.PreservedDeletedFolders} carpetas eliminadas lógicamente se conservaron porque contienen archivos.";
                CategoryDetailStatusText.Visibility = Visibility.Visible;
            }
            else if (sync.Deferred)
            {
                CategoryDetailStatusText.Text =
                    "Configuración guardada. La sincronización física queda pendiente hasta que exista la carpeta destino.";
                CategoryDetailStatusText.Visibility = Visibility.Visible;
            }
            else
            {
                CategoryDetailStatusText.Text =
                    "Configuración y carpetas físicas sincronizadas.";
                CategoryDetailStatusText.Visibility = Visibility.Visible;
            }

            return true;
        }
        catch (Exception ex)
        {
            CategoryDetailStatusText.Text =
                $"No se pudo completar la sincronización: {ex.Message}";
            CategoryDetailStatusText.Foreground =
                (Brush)Application.Current.Resources["BandaDangerBrush"];
            CategoryDetailStatusText.Visibility = Visibility.Visible;
            return false;
        }
    }

    private void ReloadCategoriesAfterSyncFailure()
    {
        LoadPersistentCategories();
        RefreshCategoryList();
        UpdateCategoryMetrics();
        ClearCategoryDetails();

        CategoryDetailStatusText.Text =
            "No se aplicaron los cambios. La configuración anterior se mantuvo.";
        CategoryDetailStatusText.Foreground =
            (Brush)Application.Current.Resources["BandaDangerBrush"];
        CategoryDetailStatusText.Visibility = Visibility.Visible;
    }
}

public enum CategoryEditorMode
{
    Create,
    Edit,
    Extensions,
    Duplicate
}

public sealed class CategoryAdminItem
{
    public CategoryAdminItem(
        string id,
        string name,
        IEnumerable<string> extensions,
        int order,
        int fileCount,
        string folderPath)
    {
        Id = id;
        Name = name;
        Extensions = extensions
            .Select(extension =>
                extension.StartsWith('.')
                    ? extension.ToLowerInvariant()
                    : $".{extension.ToLowerInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Order = order;
        FileCount = fileCount;
        FolderPath = folderPath;
    }

    public string Id { get; }
    public string Name { get; set; }
    public List<string> Extensions { get; private set; }
    public int Order { get; set; }
    public int FileCount { get; private set; }
    public string FolderPath { get; private set; }

    public string OrderText => Order.ToString("00", CultureInfo.InvariantCulture);

    public string FileCountText =>
        FileCount.ToString(CultureInfo.CurrentCulture);

    public string ExtensionsSummary
    {
        get
        {
            const int visibleCount = 5;
            var visible = Extensions.Take(visibleCount);
            var summary = string.Join(" · ", visible);
            var remaining = Extensions.Count - visibleCount;

            return remaining > 0
                ? $"{summary} +{remaining}"
                : summary;
        }
    }

    public void ReplaceExtensions(IEnumerable<string> extensions)
    {
        Extensions = extensions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void UpdateDerivedState(string destinationRoot)
    {
        FolderPath = CategoryService.GetFolderPath(
            destinationRoot,
            Order,
            Name);

        FileCount = CategoryService.CountExistingFiles(
            destinationRoot,
            Order,
            Name);
    }

    public CategorySettings ToSettings() =>
        new(
            Id,
            Name,
            Extensions,
            Order);
}
