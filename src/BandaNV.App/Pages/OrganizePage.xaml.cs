using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;

namespace BandaNV.App.Pages;

public sealed partial class OrganizePage : Page
{
    private readonly List<OrganizePreviewFile> _files = new();
    private readonly List<OrganizeCategoryOption> _categories = GetPreviewCategories();
    private readonly Dictionary<string, OrganizeCategoryOption> _rememberedAssignments =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ResolvedExtensionAssignment> _resolvedAssignments = new();
    private bool _isRefreshingPreview;

    private string? _activeAssignmentExtension;
    private List<OrganizePreviewFile> _activeAssignmentFiles = new();
    private OrganizeCategoryOption? _pendingAssignmentCategory;
    private bool _isCreatingAssignmentCategory;

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

    private void AssignExtensionButton_Click(object sender, RoutedEventArgs e)
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

        _activeAssignmentExtension = normalizedExtension;
        _activeAssignmentFiles = affectedFiles;
        _pendingAssignmentCategory =
            GetSuggestedCategory(normalizedExtension) ??
            _categories
                .OrderBy(category => category.Order)
                .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

        _isCreatingAssignmentCategory = false;

        AssignmentOverlayTitleText.Text = $"Asignar extensión {normalizedExtension}";
        AssignmentHelperText.Text =
            $"{affectedFiles.Count} archivo{(affectedFiles.Count == 1 ? string.Empty : "s")} con {normalizedExtension} " +
            "quedarán asignados a la categoría elegida.";

        AssignmentCategoryValueText.Text =
            _pendingAssignmentCategory?.DisplayName ?? "Elegir categoría";

        AssignmentCategorySelectorButton.Visibility = Visibility.Visible;
        AssignmentCreateCategoryPanel.Visibility = Visibility.Collapsed;
        AssignmentToggleCreateCategoryButton.Content = "Crear nueva categoría";
        AssignmentCategoryNameTextBox.Text = string.Empty;
        AssignmentCreateCategoryInfoText.Text =
            $"La nueva categoría se agregará al final del orden actual. {normalizedExtension} se usará ahora; " +
            "solo se recordará para futuras organizaciones si activás la opción de abajo.";

        AssignmentRememberCheckBox.IsChecked = false;
        AssignmentValidationText.Text = string.Empty;
        AssignmentValidationText.Visibility = Visibility.Collapsed;

        BuildAssignmentCategoryOptions();
        AssignmentOverlay.Visibility = Visibility.Visible;
    }

    private void BuildAssignmentCategoryOptions()
    {
        AssignmentCategoryOptionsPanel.Children.Clear();

        AssignmentCategoryOptionsPanel.Children.Add(new TextBlock
        {
            Text = "CATEGORÍAS",
            Margin = new Thickness(10, 6, 10, 4),
            Foreground = GetBrush("BandaMutedStrongBrush"),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        foreach (var category in _categories
                     .OrderBy(category => category.Order)
                     .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var optionButton = new Button
            {
                Tag = category,
                Content = category.DisplayName,
                Style = (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
            };

            if (_pendingAssignmentCategory is not null &&
                _pendingAssignmentCategory.Order == category.Order &&
                _pendingAssignmentCategory.Name.Equals(
                    category.Name,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                optionButton.Background = GetBrush("BandaAccentSoftBrush");
                optionButton.Foreground = GetBrush("BandaAccentBrush");
            }

            optionButton.Click += (_, _) =>
            {
                _pendingAssignmentCategory = category;
                AssignmentCategoryValueText.Text = category.DisplayName;
                AssignmentValidationText.Visibility = Visibility.Collapsed;
                BuildAssignmentCategoryOptions();
                AssignmentCategoryFlyout.Hide();
            };

            AssignmentCategoryOptionsPanel.Children.Add(optionButton);
        }
    }

    private void AssignmentToggleCreateCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        _isCreatingAssignmentCategory = !_isCreatingAssignmentCategory;

        AssignmentCategorySelectorButton.Visibility =
            _isCreatingAssignmentCategory ? Visibility.Collapsed : Visibility.Visible;

        AssignmentCreateCategoryPanel.Visibility =
            _isCreatingAssignmentCategory ? Visibility.Visible : Visibility.Collapsed;

        AssignmentToggleCreateCategoryButton.Content =
            _isCreatingAssignmentCategory
                ? "Usar categoría existente"
                : "Crear nueva categoría";

        AssignmentValidationText.Visibility = Visibility.Collapsed;

        if (_isCreatingAssignmentCategory)
        {
            AssignmentCategoryNameTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void SaveAssignmentOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        AssignmentValidationText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_activeAssignmentExtension) ||
            _activeAssignmentFiles.Count == 0)
        {
            CloseAssignmentOverlay();
            return;
        }

        OrganizeCategoryOption selectedCategory;

        if (_isCreatingAssignmentCategory)
        {
            var proposedName = AssignmentCategoryNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(proposedName))
            {
                AssignmentValidationText.Text =
                    "Escribí un nombre para la nueva categoría.";
                AssignmentValidationText.Visibility = Visibility.Visible;
                return;
            }

            if (_categories.Any(category =>
                    category.Name.Equals(
                        proposedName,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                AssignmentValidationText.Text =
                    "Ya existe una categoría con ese nombre. Usá la categoría existente o elegí otro nombre.";
                AssignmentValidationText.Visibility = Visibility.Visible;
                return;
            }

            var categoryName =
                proposedName.ToUpper(CultureInfo.CurrentCulture);

            var nextOrder = _categories.Count == 0
                ? 1
                : _categories.Max(category => category.Order) + 1;

            selectedCategory = new OrganizeCategoryOption(nextOrder, categoryName);
            _categories.Add(selectedCategory);
        }
        else if (_pendingAssignmentCategory is not null)
        {
            selectedCategory = _pendingAssignmentCategory;
        }
        else
        {
            AssignmentValidationText.Text =
                "Elegí una categoría antes de continuar.";
            AssignmentValidationText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var file in _activeAssignmentFiles)
        {
            file.AssignTo(
                selectedCategory,
                OrganizeAssignmentSource.ExtensionRule);
        }

        var rememberAssignment =
            AssignmentRememberCheckBox.IsChecked == true;

        if (rememberAssignment)
        {
            _rememberedAssignments[_activeAssignmentExtension] =
                selectedCategory;
        }

        _resolvedAssignments.RemoveAll(item =>
            item.Extension.Equals(
                _activeAssignmentExtension,
                StringComparison.OrdinalIgnoreCase));

        _resolvedAssignments.Add(new ResolvedExtensionAssignment(
            _activeAssignmentExtension,
            _activeAssignmentFiles.Count,
            selectedCategory,
            rememberAssignment,
            rememberAssignment
                ? "Asignación recordada para próximas organizaciones"
                : "Asignación aplicada solo a esta organización"));

        CloseAssignmentOverlay();
        RefreshPreview();
    }

    private void CloseAssignmentOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAssignmentOverlay();
    }

    private void AssignmentBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseAssignmentOverlay();
    }

    private void CloseAssignmentOverlay()
    {
        AssignmentCategoryFlyout.Hide();
        AssignmentOverlay.Visibility = Visibility.Collapsed;

        _activeAssignmentExtension = null;
        _activeAssignmentFiles = new List<OrganizePreviewFile>();
        _pendingAssignmentCategory = null;
        _isCreatingAssignmentCategory = false;
    }

    private void RevertExtensionAssignmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string extension } ||
            string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var assignment = _resolvedAssignments.FirstOrDefault(item =>
            item.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));

        if (assignment is null)
        {
            return;
        }

        foreach (var file in _files.Where(file =>
                     file.Extension.Equals(assignment.Extension, StringComparison.OrdinalIgnoreCase) &&
                     file.AssignmentSource == OrganizeAssignmentSource.ExtensionRule &&
                     file.IsAssignedTo(assignment.CategoryOrder, assignment.CategoryName)))
        {
            file.ClearAssignment();
        }

        if (assignment.WasRemembered &&
            _rememberedAssignments.TryGetValue(assignment.Extension, out var rememberedCategory) &&
            rememberedCategory.Order == assignment.CategoryOrder &&
            rememberedCategory.Name.Equals(assignment.CategoryName, StringComparison.OrdinalIgnoreCase))
        {
            _rememberedAssignments.Remove(assignment.Extension);
        }

        _resolvedAssignments.Remove(assignment);
        RefreshPreview();
    }

    private void PreviewCategorySelectorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingPreview ||
            sender is not Button { Tag: string itemId } selectorButton)
        {
            return;
        }

        var file = _files.FirstOrDefault(item =>
            item.ItemId.Equals(itemId, StringComparison.Ordinal));

        if (file is null)
        {
            return;
        }

        var optionsPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 0, 12, 0)
        };

        optionsPanel.Children.Add(new TextBlock
        {
            Text = "CATEGORÍAS",
            Margin = new Thickness(10, 6, 10, 4),
            Foreground = GetBrush("BandaMutedStrongBrush"),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var flyout = new Flyout
        {
            Placement =
                Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle =
                (Style)Application.Current.Resources["BandaPopupFlyoutPresenterStyle"]
        };

        foreach (var category in _categories
                     .OrderBy(category => category.Order)
                     .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var optionButton = new Button
            {
                Content = category.DisplayName,
                Style =
                    (Style)Application.Current.Resources["BandaPopupOptionButtonStyle"]
            };

            if (file.IsAssignedTo(category.Order, category.Name))
            {
                optionButton.Background = GetBrush("BandaAccentSoftBrush");
                optionButton.Foreground = GetBrush("BandaAccentBrush");
            }

            optionButton.Click += (_, _) =>
            {
                flyout.Hide();

                if (file.IsAssignedTo(category.Order, category.Name))
                {
                    return;
                }

                file.AssignTo(
                    category,
                    OrganizeAssignmentSource.IndividualOverride);

                RefreshPreview();
            };

            optionsPanel.Children.Add(optionButton);
        }

        flyout.Content = new Border
        {
            Width = 220,
            MaxHeight = 330,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(16),
            Background = GetBrush("BandaPopupSurfaceBrush"),
            BorderBrush = GetBrush("BandaBorderStrongBrush"),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = optionsPanel
            }
        };

        flyout.ShowAt(selectorButton);
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
        _resolvedAssignments.Clear();

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
        var modificationAnchor = DateTime.Now.AddMinutes(-(_files.Count * 19));

        for (var index = 1; index <= count; index++)
        {
            var sizeVariation = Math.Max(1, baseSizeBytes / 10);
            var sizeBytes = baseSizeBytes + (sizeVariation * (index - 1));
            var modifiedAt = modificationAnchor
                .AddHours(-(index % 5))
                .AddMinutes(-(index * 13));

            _files.Add(new OrganizePreviewFile(
                fileName: $"{prefix}_{index:00}{normalizedExtension}",
                extension: normalizedExtension,
                sizeBytes: sizeBytes,
                modifiedAt: modifiedAt,
                categoryOptions: _categories,
                categoryOrder: categoryOrder,
                categoryName: categoryName));
        }
    }

    private void ApplyRememberedAssignments()
    {
        var pendingByExtension = _files
            .Where(file => !file.IsClassified)
            .GroupBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var extensionGroup in pendingByExtension)
        {
            if (!_rememberedAssignments.TryGetValue(extensionGroup.Key, out var category))
            {
                continue;
            }

            var affectedFiles = extensionGroup.ToList();

            foreach (var file in affectedFiles)
            {
                file.AssignTo(category, OrganizeAssignmentSource.ExtensionRule);
            }

            _resolvedAssignments.Add(new ResolvedExtensionAssignment(
                extensionGroup.Key,
                affectedFiles.Count,
                category,
                wasRemembered: true,
                persistenceText: "Asignación recordada aplicada automáticamente"));
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
                group.Key.CategoryName ?? "Sin categoría",
                group.Count()))
            .ToList();

        foreach (var assignment in _resolvedAssignments)
        {
            var activeCount = _files.Count(file =>
                file.Extension.Equals(assignment.Extension, StringComparison.OrdinalIgnoreCase) &&
                file.AssignmentSource == OrganizeAssignmentSource.ExtensionRule &&
                file.IsAssignedTo(assignment.CategoryOrder, assignment.CategoryName));

            assignment.UpdateActiveFileCount(activeCount);
        }

        _isRefreshingPreview = true;

        PreviewFilesList.ItemsSource = null;
        PreviewFilesList.ItemsSource = _files
            .OrderBy(file => file.IsClassified ? 0 : 1)
            .ThenBy(file => file.CategoryOrder ?? int.MaxValue)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _isRefreshingPreview = false;

        UnassignedExtensionsList.ItemsSource = null;
        UnassignedExtensionsList.ItemsSource = unassignedExtensions;

        ResolvedAssignmentsList.ItemsSource = null;
        ResolvedAssignmentsList.ItemsSource = _resolvedAssignments
            .OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResolvedAssignmentsPanel.Visibility =
            _resolvedAssignments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ResolvedAssignmentsCountText.Text =
            $"{_resolvedAssignments.Count} resuelta{(_resolvedAssignments.Count == 1 ? string.Empty : "s")}";

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

        var warningBrush = GetBrush("OrganizeWarningBrush");
        var warningSoftBrush = GetBrush("OrganizeWarningSoftBrush");
        var cardBrush = GetBrush("BandaCardBrush");
        var borderBrush = GetBrush("BandaBorderBrush");
        var mutedBrush = GetBrush("BandaMutedBrush");
        var accentBrush = GetBrush("BandaAccentBrush");
        var accentSoftBrush = GetBrush("BandaAccentSoftBrush");

        // La métrica solo es una advertencia mientras realmente haya pendientes.
        if (unclassifiedFiles.Count > 0)
        {
            UnclassifiedMetricCard.Background = warningSoftBrush;
            UnclassifiedMetricCard.BorderBrush = warningBrush;
            UnclassifiedMetricTitle.Foreground = warningBrush;
            UnclassifiedMetricSubtitle.Foreground = warningBrush;
            UnclassifiedMetricSubtitle.Text = "no se moverán sin asignación";

            SummaryUnclassifiedCountText.Foreground = warningBrush;
            FooterStatusText.Foreground = mutedBrush;
            FooterStatusText.Text =
                $"{unclassifiedFiles.Count} archivos quedarán sin mover si no resolvés sus extensiones.";
        }
        else
        {
            UnclassifiedMetricCard.Background = cardBrush;
            UnclassifiedMetricCard.BorderBrush = borderBrush;
            UnclassifiedMetricTitle.Foreground = mutedBrush;
            UnclassifiedMetricSubtitle.Foreground = mutedBrush;
            UnclassifiedMetricSubtitle.Text = "todos tienen destino";

            SummaryUnclassifiedCountText.Foreground = accentBrush;
            FooterStatusText.Foreground = accentBrush;
            FooterStatusText.Text =
                "Todos los archivos tienen destino. Ya podés confirmar la organización.";
        }

        UnassignedCard.Visibility = Visibility.Visible;

        if (unassignedExtensions.Count > 0)
        {
            UnassignedCard.BorderBrush = warningBrush;

            UnassignedTitleText.Text = "Extensiones sin asignar";
            UnassignedDescriptionText.Text =
                _resolvedAssignments.Count > 0
                    ? "Todavía quedan extensiones pendientes. Las asignaciones que ya resolviste se mantienen registradas abajo."
                    : "Estas extensiones todavía no pertenecen a ninguna categoría. Podés resolverlas ahora para incluir sus archivos en esta organización.";

            UnassignedSummaryBadge.Background = warningSoftBrush;
            UnassignedSummaryText.Foreground = warningBrush;

            var extensionLabel = unassignedExtensions.Count == 1 ? "extensión" : "extensiones";
            var fileLabel = unclassifiedFiles.Count == 1 ? "archivo" : "archivos";

            UnassignedSummaryText.Text =
                $"{unassignedExtensions.Count} {extensionLabel} · {unclassifiedFiles.Count} {fileLabel}";

            ResolvedAssignmentsTitle.Text = "Asignaciones realizadas";
        }
        else
        {
            UnassignedCard.BorderBrush = accentBrush;

            UnassignedTitleText.Text = "Extensiones resueltas";
            UnassignedDescriptionText.Text =
                _resolvedAssignments.Count > 0
                    ? "Todas las extensiones detectadas ya tienen una categoría y un destino. El registro de asignaciones queda disponible para que puedas revisarlo."
                    : "No se detectaron extensiones pendientes de asignación.";

            UnassignedSummaryBadge.Background = accentSoftBrush;
            UnassignedSummaryText.Foreground = accentBrush;
            UnassignedSummaryText.Text =
                _resolvedAssignments.Count > 0 ? "Todo resuelto" : "Sin pendientes";

            ResolvedAssignmentsTitle.Text = "Registro de asignaciones";
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

    private Brush GetBrush(string resourceKey)
    {
        if (Resources.TryGetValue(resourceKey, out var pageResource) &&
            pageResource is Brush pageBrush)
        {
            return pageBrush;
        }

        if (Application.Current.Resources.TryGetValue(resourceKey, out var appResource) &&
            appResource is Brush appBrush)
        {
            return appBrush;
        }

        throw new InvalidOperationException($"No se encontró el recurso de pincel '{resourceKey}'.");
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

public enum OrganizeAssignmentSource
{
    Unclassified,
    InitialCategory,
    ExtensionRule,
    IndividualOverride
}

public sealed class OrganizePreviewFile
{
    public OrganizePreviewFile(
        string fileName,
        string extension,
        long sizeBytes,
        DateTime modifiedAt,
        IReadOnlyList<OrganizeCategoryOption> categoryOptions,
        int? categoryOrder,
        string? categoryName)
    {
        ItemId = Guid.NewGuid().ToString("N");
        FileName = fileName;
        Extension = extension;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        CategoryOptions = categoryOptions;
        CategoryOrder = categoryOrder;
        CategoryName = categoryName;
        AssignmentSource =
            categoryOrder.HasValue && !string.IsNullOrWhiteSpace(categoryName)
                ? OrganizeAssignmentSource.InitialCategory
                : OrganizeAssignmentSource.Unclassified;
    }

    public string ItemId { get; }
    public string FileName { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public IReadOnlyList<OrganizeCategoryOption> CategoryOptions { get; }

    public int? CategoryOrder { get; private set; }
    public string? CategoryName { get; private set; }
    public OrganizeAssignmentSource AssignmentSource { get; private set; }

    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);

    public OrganizeCategoryOption? SelectedCategory =>
        CategoryOptions.FirstOrDefault(category =>
            IsAssignedTo(category.Order, category.Name));

    public string SelectedCategoryDisplayName =>
        SelectedCategory?.DisplayName ?? "Elegir categoría";

    public string ExtensionDisplay => Extension.ToUpperInvariant();

    public string CategoryDisplay =>
        IsClassified
            ? CategoryName ?? string.Empty
            : "Sin asignar";

    public string SizeDisplay => FormatBytes(SizeBytes);

    public string ModifiedDisplay =>
        ModifiedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("es-AR"));

    public bool IsAssignedTo(int order, string name) =>
        CategoryOrder == order &&
        CategoryName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

    public void AssignTo(
        OrganizeCategoryOption category,
        OrganizeAssignmentSource source)
    {
        CategoryOrder = category.Order;
        CategoryName = category.Name;
        AssignmentSource = source;
    }

    public void ClearAssignment()
    {
        CategoryOrder = null;
        CategoryName = null;
        AssignmentSource = OrganizeAssignmentSource.Unclassified;
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
    // El número define el orden y el nombre de carpeta en el explorador,
    // pero no forma parte del nombre visual de la categoría dentro de la app.
    public string DisplayName => Name;
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

public sealed class ResolvedExtensionAssignment
{
    public ResolvedExtensionAssignment(
        string extension,
        int fileCount,
        OrganizeCategoryOption category,
        bool wasRemembered,
        string persistenceText)
    {
        Extension = extension;
        OriginalFileCount = fileCount;
        ActiveFileCount = fileCount;
        CategoryOrder = category.Order;
        CategoryName = category.Name;
        DestinationText = $"→ {category.DisplayName}";
        WasRemembered = wasRemembered;
        PersistenceText = persistenceText;
    }

    public string Extension { get; }
    public int OriginalFileCount { get; }
    public int ActiveFileCount { get; private set; }
    public int CategoryOrder { get; }
    public string CategoryName { get; }
    public string DestinationText { get; }
    public bool WasRemembered { get; }
    public string PersistenceText { get; }

    public string FilesText =>
        ActiveFileCount == OriginalFileCount
            ? $"{OriginalFileCount} archivo{(OriginalFileCount == 1 ? string.Empty : "s")}"
            : $"{ActiveFileCount} de {OriginalFileCount} archivos";

    public void UpdateActiveFileCount(int activeFileCount)
    {
        ActiveFileCount = Math.Clamp(activeFileCount, 0, OriginalFileCount);
    }
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
