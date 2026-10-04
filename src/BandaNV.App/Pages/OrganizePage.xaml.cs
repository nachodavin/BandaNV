using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace BandaNV.App.Pages;

public sealed partial class OrganizePage : Page
{
    private readonly List<OrganizePreviewFile> _files = new();
    private readonly List<OrganizeCategoryOption> _categories = GetPreviewCategories();
    private readonly Dictionary<string, OrganizeCategoryOption> _rememberedAssignments =
        new(StringComparer.OrdinalIgnoreCase);

    public OrganizePage()
    {
        InitializeComponent();
        ShowInitialState();
    }

    private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        BuildPreviewData();
        ShowPreviewState();
    }

    private void AnalyzeAgainButton_Click(object sender, RoutedEventArgs e)
    {
        BuildPreviewData();
        ShowPreviewState();
    }

    private void CancelPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        ShowInitialState();
    }

    private void NewOrganizationButton_Click(object sender, RoutedEventArgs e)
    {
        ShowInitialState();
    }

    private async void AssignExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string extension } ||
            string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var normalizedExtension = NormalizeExtension(extension);
        var affectedFiles = _files
            .Where(file => !file.IsClassified &&
                           file.Extension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (affectedFiles.Count == 0)
        {
            return;
        }

        var categoryPicker = new ComboBox
        {
            Header = "Asignar a categoría",
            ItemsSource = _categories,
            DisplayMemberPath = nameof(OrganizeCategoryOption.DisplayName),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 360
        };

        categoryPicker.SelectedItem = GetSuggestedCategory(normalizedExtension) ?? _categories.FirstOrDefault();

        var rememberCheckBox = new CheckBox
        {
            Content = "Recordar esta asignación para futuras organizaciones",
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var helperText = new TextBlock
        {
            Text = $"{affectedFiles.Count} archivo{(affectedFiles.Count == 1 ? string.Empty : "s")} con {normalizedExtension} quedarán asignados a la categoría elegida.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BandaMutedBrush"],
            FontSize = 13
        };

        var dialogContent = new StackPanel
        {
            Spacing = 10
        };

        dialogContent.Children.Add(helperText);
        dialogContent.Children.Add(categoryPicker);
        dialogContent.Children.Add(rememberCheckBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Asignar extensión {normalizedExtension}",
            PrimaryButtonText = "Guardar asignación",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = dialogContent
        };

        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary ||
            categoryPicker.SelectedItem is not OrganizeCategoryOption selectedCategory)
        {
            return;
        }

        foreach (var file in affectedFiles)
        {
            file.AssignTo(selectedCategory);
        }

        if (rememberCheckBox.IsChecked == true)
        {
            _rememberedAssignments[normalizedExtension] = selectedCategory;
        }

        AssignmentFeedbackText.Text =
            rememberCheckBox.IsChecked == true
                ? $"{normalizedExtension} se asignó a {selectedCategory.DisplayName}. BandaNV recordará esta elección para las próximas organizaciones de esta sesión."
                : $"{normalizedExtension} se asignó a {selectedCategory.DisplayName} solo para esta organización.";

        AssignmentFeedbackBorder.Visibility = Visibility.Visible;

        RefreshPreview();
    }

    private async void OrganizeButton_Click(object sender, RoutedEventArgs e)
    {
        var movableFiles = _files.Where(file => file.IsClassified).ToList();
        var skippedFiles = _files.Count - movableFiles.Count;

        if (movableFiles.Count == 0)
        {
            return;
        }

        InitialStatePanel.Visibility = Visibility.Collapsed;
        PreviewStatePanel.Visibility = Visibility.Collapsed;
        CompletionStatePanel.Visibility = Visibility.Collapsed;
        ProgressStatePanel.Visibility = Visibility.Visible;

        OrganizationProgressBar.Value = 0;
        ProgressCountText.Text = $"0 de {movableFiles.Count} archivos";
        ProgressStatusText.Text = "Preparando movimientos";

        const int steps = 12;

        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(75);

            var progress = step / (double)steps;
            var processed = Math.Min(
                movableFiles.Count,
                Math.Max(1, (int)Math.Round(movableFiles.Count * progress)));

            OrganizationProgressBar.Value = progress * 100;
            ProgressCountText.Text = $"{processed} de {movableFiles.Count} archivos";
            ProgressStatusText.Text =
                step < steps
                    ? "Aplicando la organización prevista..."
                    : "Finalizando...";
        }

        await Task.Delay(160);

        ProgressStatePanel.Visibility = Visibility.Collapsed;
        CompletionStatePanel.Visibility = Visibility.Visible;

        CompletionText.Text =
            skippedFiles > 0
                ? $"{movableFiles.Count} archivos quedaron listos para organizar y {skippedFiles} permanecerían sin mover por no tener categoría asignada."
                : $"{movableFiles.Count} archivos quedaron listos para organizar correctamente. No hay elementos pendientes.";
    }

    private void ShowInitialState()
    {
        InitialStatePanel.Visibility = Visibility.Visible;
        PreviewStatePanel.Visibility = Visibility.Collapsed;
        ProgressStatePanel.Visibility = Visibility.Collapsed;
        CompletionStatePanel.Visibility = Visibility.Collapsed;
        AssignmentFeedbackBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowPreviewState()
    {
        InitialStatePanel.Visibility = Visibility.Collapsed;
        PreviewStatePanel.Visibility = Visibility.Visible;
        ProgressStatePanel.Visibility = Visibility.Collapsed;
        CompletionStatePanel.Visibility = Visibility.Collapsed;
        RefreshPreview();
    }

    private void BuildPreviewData()
    {
        _files.Clear();
        AssignmentFeedbackBorder.Visibility = Visibility.Collapsed;

        AddGeneratedFiles(
            prefix: "IMG",
            extension: ".jpg",
            count: 10,
            categoryOrder: 4,
            categoryName: "IMAGES",
            baseSizeBytes: 2_400_000);

        AddGeneratedFiles(
            prefix: "captura",
            extension: ".png",
            count: 6,
            categoryOrder: 4,
            categoryName: "IMAGES",
            baseSizeBytes: 1_150_000);

        AddGeneratedFiles(
            prefix: "documento",
            extension: ".pdf",
            count: 5,
            categoryOrder: 3,
            categoryName: "DOCUMENTS",
            baseSizeBytes: 3_800_000);

        AddGeneratedFiles(
            prefix: "notas",
            extension: ".txt",
            count: 4,
            categoryOrder: 3,
            categoryName: "DOCUMENTS",
            baseSizeBytes: 85_000);

        AddGeneratedFiles(
            prefix: "video",
            extension: ".mp4",
            count: 4,
            categoryOrder: 6,
            categoryName: "VIDEOS",
            baseSizeBytes: 118_000_000);

        AddGeneratedFiles(
            prefix: "archivo",
            extension: ".rar",
            count: 3,
            categoryOrder: 1,
            categoryName: "RAR",
            baseSizeBytes: 42_000_000);

        AddGeneratedFiles(
            prefix: "setup",
            extension: ".exe",
            count: 2,
            categoryOrder: 2,
            categoryName: "INSTALLERS",
            baseSizeBytes: 66_000_000);

        AddGeneratedFiles(
            prefix: "foto_iphone",
            extension: ".heic",
            count: 7,
            categoryOrder: null,
            categoryName: null,
            baseSizeBytes: 4_200_000);

        AddGeneratedFiles(
            prefix: "escena_3d",
            extension: ".blend",
            count: 3,
            categoryOrder: null,
            categoryName: null,
            baseSizeBytes: 26_000_000);

        AddGeneratedFiles(
            prefix: "interfaz",
            extension: ".sketch",
            count: 2,
            categoryOrder: null,
            categoryName: null,
            baseSizeBytes: 8_500_000);

        ApplyRememberedAssignments();
    }

    private void AddGeneratedFiles(
        string prefix,
        string extension,
        int count,
        int? categoryOrder,
        string? categoryName,
        long baseSizeBytes)
    {
        var normalizedExtension = NormalizeExtension(extension);

        for (var index = 1; index <= count; index++)
        {
            var sizeVariation = Math.Max(1, baseSizeBytes / 10);
            var sizeBytes = baseSizeBytes + (sizeVariation * (index - 1));

            _files.Add(new OrganizePreviewFile(
                fileName: $"{prefix}_{index:00}{normalizedExtension}",
                extension: normalizedExtension,
                sizeBytes: sizeBytes,
                categoryOrder: categoryOrder,
                categoryName: categoryName));
        }
    }

    private void ApplyRememberedAssignments()
    {
        foreach (var file in _files.Where(file => !file.IsClassified))
        {
            if (_rememberedAssignments.TryGetValue(file.Extension, out var category))
            {
                file.AssignTo(category);
            }
        }
    }

    private void RefreshPreview()
    {
        var classifiedFiles = _files.Where(file => file.IsClassified).ToList();
        var unclassifiedFiles = _files.Where(file => !file.IsClassified).ToList();

        var unassignedExtensions = unclassifiedFiles
            .GroupBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var samples = group
                    .Take(2)
                    .Select(file => file.FileName)
                    .ToList();

                var suffix = group.Count() > samples.Count ? " · …" : string.Empty;

                return new UnassignedExtensionSummary(
                    group.Key,
                    group.Count(),
                    string.Join(" · ", samples) + suffix);
            })
            .ToList();

        var categorySummary = classifiedFiles
            .GroupBy(file => new
            {
                file.CategoryOrder,
                file.CategoryName
            })
            .OrderBy(group => group.Key.CategoryOrder)
            .ThenBy(group => group.Key.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new OrganizeCategorySummary(
                $"{group.Key.CategoryOrder} - {group.Key.CategoryName}",
                group.Count()))
            .ToList();

        PreviewFilesList.ItemsSource = null;
        PreviewFilesList.ItemsSource = _files
            .OrderBy(file => file.IsClassified ? 0 : 1)
            .ThenBy(file => file.CategoryOrder ?? int.MaxValue)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        UnassignedExtensionsList.ItemsSource = null;
        UnassignedExtensionsList.ItemsSource = unassignedExtensions;

        SummaryCategoriesList.ItemsSource = null;
        SummaryCategoriesList.ItemsSource = categorySummary;

        FilesMetricText.Text = _files.Count.ToString(CultureInfo.CurrentCulture);
        CategoriesMetricText.Text = categorySummary.Count.ToString(CultureInfo.CurrentCulture);
        SizeMetricText.Text = FormatBytes(_files.Sum(file => file.SizeBytes));
        UnclassifiedMetricText.Text = unclassifiedFiles.Count.ToString(CultureInfo.CurrentCulture);

        SummaryMoveCountText.Text = classifiedFiles.Count.ToString(CultureInfo.CurrentCulture);
        SummaryUnclassifiedCountText.Text = unclassifiedFiles.Count.ToString(CultureInfo.CurrentCulture);

        OrganizeButton.Content = $"Organizar {classifiedFiles.Count} archivos";
        OrganizeButton.IsEnabled = classifiedFiles.Count > 0;

        FooterStatusText.Text =
            unclassifiedFiles.Count > 0
                ? $"{unclassifiedFiles.Count} archivos quedarán sin mover si no resolvés sus extensiones."
                : "Todos los archivos tienen destino. Ya podés confirmar la organización.";

        if (unassignedExtensions.Count == 0)
        {
            UnassignedCard.Visibility = Visibility.Collapsed;
            AssignmentFeedbackBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            UnassignedCard.Visibility = Visibility.Visible;

            var extensionLabel = unassignedExtensions.Count == 1 ? "extensión" : "extensiones";
            var fileLabel = unclassifiedFiles.Count == 1 ? "archivo" : "archivos";

            UnassignedSummaryText.Text =
                $"{unassignedExtensions.Count} {extensionLabel} · {unclassifiedFiles.Count} {fileLabel}";
        }
    }

    private OrganizeCategoryOption? GetSuggestedCategory(string extension)
    {
        var preferredName = extension.ToLowerInvariant() switch
        {
            ".heic" => "IMAGES",
            ".sketch" => "DESIGN",
            ".blend" => "PROJECTS",
            _ => null
        };

        return preferredName is null
            ? null
            : _categories.FirstOrDefault(category =>
                category.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<OrganizeCategoryOption> GetPreviewCategories()
    {
        return
        [
            new(1, "RAR"),
            new(2, "INSTALLERS"),
            new(3, "DOCUMENTS"),
            new(4, "IMAGES"),
            new(5, "GIF"),
            new(6, "VIDEOS"),
            new(7, "AUDIO"),
            new(8, "FONTS"),
            new(9, "DESIGN"),
            new(10, "CODE"),
            new(11, "BACKUPS"),
            new(12, "PROJECTS"),
            new(13, "TEXTURES"),
            new(14, "PACKAGES")
        ];
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ".sin-extension";
        }

        return normalized.StartsWith('.') ? normalized : $".{normalized}";
    }

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

public sealed class OrganizePreviewFile
{
    public OrganizePreviewFile(
        string fileName,
        string extension,
        long sizeBytes,
        int? categoryOrder,
        string? categoryName)
    {
        FileName = fileName;
        Extension = extension;
        SizeBytes = sizeBytes;
        CategoryOrder = categoryOrder;
        CategoryName = categoryName;
    }

    public string FileName { get; }
    public string Extension { get; }
    public long SizeBytes { get; }

    public int? CategoryOrder { get; private set; }
    public string? CategoryName { get; private set; }

    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);

    public string ExtensionDisplay => Extension.ToUpperInvariant();

    public string CategoryDisplay =>
        IsClassified
            ? $"{CategoryOrder} - {CategoryName}"
            : "Sin clasificar";

    public string DestinationDisplay =>
        IsClassified
            ? $"{CategoryOrder} - {CategoryName}"
            : "Sin destino";

    public string SizeDisplay => FormatBytes(SizeBytes);

    public Visibility ClassifiedVisibility =>
        IsClassified ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnclassifiedVisibility =>
        IsClassified ? Visibility.Collapsed : Visibility.Visible;

    public void AssignTo(OrganizeCategoryOption category)
    {
        CategoryOrder = category.Order;
        CategoryName = category.Name;
    }

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

public sealed class OrganizeCategoryOption
{
    public OrganizeCategoryOption(int order, string name)
    {
        Order = order;
        Name = name;
    }

    public int Order { get; }
    public string Name { get; }
    public string DisplayName => $"{Order} - {Name}";
}

public sealed class UnassignedExtensionSummary
{
    public UnassignedExtensionSummary(
        string extension,
        int fileCount,
        string samplesText)
    {
        Extension = extension;
        FileCount = fileCount;
        SamplesText = samplesText;
    }

    public string Extension { get; }
    public int FileCount { get; }
    public string FilesText =>
        $"{FileCount} archivo{(FileCount == 1 ? string.Empty : "s")}";
    public string SamplesText { get; }
}

public sealed class OrganizeCategorySummary
{
    public OrganizeCategorySummary(string displayName, int count)
    {
        DisplayName = displayName;
        Count = count;
    }

    public string DisplayName { get; }
    public int Count { get; }
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);
}
