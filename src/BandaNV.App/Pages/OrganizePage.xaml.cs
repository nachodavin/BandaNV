using BandaNV.Core.Models;
using BandaNV.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;

namespace BandaNV.App.Pages;

public sealed partial class OrganizePage : Page, IOrganizationConflictResolver
{
    private readonly List<OrganizePreviewFile> _files = new();
    private readonly List<OrganizeCategoryOption> _categories = new();
    private readonly Dictionary<string, OrganizeCategoryOption> _rememberedAssignments =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ResolvedExtensionAssignment> _resolvedAssignments = new();
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _executionCts;
    private OrganizationAnalysisResult? _lastAnalysis;
    private bool _isRefreshingPreview;
    private TaskCompletionSource<OrganizationConflictResolution>? _conflictResolutionTcs;

    private string? _activeAssignmentExtension;
    private List<OrganizePreviewFile> _activeAssignmentFiles = new();
    private OrganizeCategoryOption? _pendingAssignmentCategory;
    private bool _isCreatingAssignmentCategory;

    public OrganizePage()
    {
        InitializeComponent();
        LoadCategoryOptions();
        UpdateInitialStateText();
        ShowInitialState();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeFilesAsync();
    }

    private async void AnalyzeAgainButton_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeFilesAsync();
    }

    private void CancelPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        ShowInitialState();
    }

    private void NewOrganizationButton_Click(object sender, RoutedEventArgs e)
    {
        LoadCategoryOptions();
        UpdateInitialStateText();
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
                Content = CreateCategoryOptionContent(category),
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

    private StackPanel CreateCategoryOptionContent(
        OrganizeCategoryOption category)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        content.Children.Add(
            new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = CreateCategoryBrush(category.ColorHex),
                VerticalAlignment = VerticalAlignment.Center
            });

        content.Children.Add(
            new TextBlock
            {
                Text = category.DisplayName,
                Foreground = GetBrush("BandaTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

        return content;
    }

    private Brush CreateCategoryBrush(string colorHex)
    {
        if (!CategoryColorPalette.TryNormalizeHex(
                colorHex,
                out var normalized))
        {
            return GetBrush("BandaAccentBrush");
        }

        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(
                255,
                Convert.ToByte(normalized.Substring(1, 2), 16),
                Convert.ToByte(normalized.Substring(3, 2), 16),
                Convert.ToByte(normalized.Substring(5, 2), 16)));
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

    private async void SaveAssignmentOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        AssignmentValidationText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_activeAssignmentExtension) ||
            _activeAssignmentFiles.Count == 0)
        {
            CloseAssignmentOverlay();
            return;
        }

        OrganizeCategoryOption selectedCategory;
        var createdNewCategory = false;

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

            if (proposedName.IndexOfAny(
                    System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                AssignmentValidationText.Text =
                    "El nombre contiene caracteres que no pueden usarse en una carpeta.";
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

            var newCategoryId = Guid.NewGuid().ToString("D");
            selectedCategory = new OrganizeCategoryOption(
                newCategoryId,
                nextOrder,
                categoryName,
                CategoryColorPalette.Generate(
                    newCategoryId,
                    global::BandaNV.App.App.Settings.Current.SecondaryColor));
            _categories.Add(selectedCategory);
            createdNewCategory = true;
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

        try
        {
            if (rememberAssignment)
            {
                await PersistRememberedAssignmentAsync(
                    _activeAssignmentExtension,
                    selectedCategory);

                _rememberedAssignments[_activeAssignmentExtension] =
                    selectedCategory;
            }
            else if (createdNewCategory)
            {
                await PersistNewCategoryAsync(selectedCategory);
            }
        }
        catch (Exception ex)
        {
            AssignmentValidationText.Text =
                $"No se pudo guardar la categoría/asignación: {ex.Message}";
            AssignmentValidationText.Visibility = Visibility.Visible;
            return;
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

    private async void RevertExtensionAssignmentButton_Click(object sender, RoutedEventArgs e)
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

            try
            {
                await RemoveRememberedAssignmentAsync(
                    assignment.Extension,
                    rememberedCategory.Id);
            }
            catch
            {
                // La reversión visual sigue siendo válida para esta ejecución.
                // Si persistir falla, la próxima recarga volverá a reflejar
                // lo que haya quedado realmente guardado.
            }
        }

        _resolvedAssignments.Remove(assignment);
        RefreshPreview();
    }

    private void FolderDetailButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string itemId })
        {
            return;
        }

        var folder =
            _files.FirstOrDefault(item =>
                item.ItemId.Equals(
                    itemId,
                    StringComparison.Ordinal));

        if (folder is null ||
            !folder.IsDirectory)
        {
            return;
        }

        FolderDetailTitleText.Text =
            folder.FileName;

        FolderDetailSummaryText.Text =
            folder.FolderDetailSummary;

        FolderDetailPathText.Text =
            folder.FullPath;

        FolderDetailFilesList.ItemsSource =
            folder.FolderContents;

        FolderDetailOverlay.Visibility =
            Visibility.Visible;
    }

    private void CloseFolderDetailButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseFolderDetail();
    }

    private void FolderDetailBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseFolderDetail();
    }

    private void CloseFolderDetail()
    {
        FolderDetailOverlay.Visibility =
            Visibility.Collapsed;

        FolderDetailFilesList.ItemsSource =
            null;
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
                Content = CreateCategoryOptionContent(category),
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

    public Task<OrganizationConflictResolution> ResolveAsync(
        OrganizationConflictInfo conflict,
        CancellationToken cancellationToken = default)
    {
        var completion =
            new TaskCompletionSource<OrganizationConflictResolution>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var registration = cancellationToken.Register(() =>
        {
            completion.TrySetCanceled(cancellationToken);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_conflictResolutionTcs, completion))
                {
                    _conflictResolutionTcs = null;
                    ConflictOverlay.Visibility = Visibility.Collapsed;
                }
            });
        });

        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                _conflictResolutionTcs = completion;

                var itemLabel =
                    conflict.IsDirectory
                        ? "carpeta"
                        : "archivo";

                ConflictTitleText.Text =
                    $"Ya existe un elemento con el mismo nombre";

                ConflictFileNameText.Text =
                    $"{conflict.FileName} · {itemLabel}";

                ConflictSourceLabelText.Text =
                    conflict.IsDirectory
                        ? "CARPETA A ORGANIZAR"
                        : "ARCHIVO A ORGANIZAR";

                ConflictSourcePathText.Text =
                    conflict.SourcePath;

                ConflictSourceMetaText.Text =
                    conflict.IsDirectory
                        ? $"{conflict.SourceItemCount} archivo{(conflict.SourceItemCount == 1 ? string.Empty : "s")} · {FormatBytes(conflict.SourceSizeBytes)} · Modificado {conflict.SourceModifiedAt:dd/MM/yyyy HH:mm:ss}"
                        : $"{FormatBytes(conflict.SourceSizeBytes)} · Modificado {conflict.SourceModifiedAt:dd/MM/yyyy HH:mm:ss}";

                ConflictDestinationPathText.Text =
                    conflict.DestinationPath;

                ConflictDestinationMetaText.Text =
                    conflict.DestinationItemCount > 1
                        ? $"{conflict.DestinationItemCount} archivos · {FormatBytes(conflict.DestinationSizeBytes)} · Modificado {conflict.DestinationModifiedAt:dd/MM/yyyy HH:mm:ss}"
                        : $"{FormatBytes(conflict.DestinationSizeBytes)} · Modificado {conflict.DestinationModifiedAt:dd/MM/yyyy HH:mm:ss}";

                ConflictInstructionText.Text =
                    $"Elegí qué hacer con este conflicto. Cerrar esta ventana equivale a omitir {itemLabel}.";

                ConflictApplyAllCheckBox.IsChecked = false;
                ConflictOverlay.Visibility = Visibility.Visible;
            }))
        {
            registration.Dispose();
            completion.TrySetResult(
                new OrganizationConflictResolution(
                    OrganizationConflictAction.Skip,
                    ApplyToRemaining: false));
        }

        return completion.Task;
    }

    private void ConflictRenameButton_Click(object sender, RoutedEventArgs e) =>
        CompleteConflictResolution(OrganizationConflictAction.Rename);

    private void ConflictReplaceButton_Click(object sender, RoutedEventArgs e) =>
        CompleteConflictResolution(OrganizationConflictAction.Replace);

    private void ConflictSkipButton_Click(object sender, RoutedEventArgs e) =>
        CompleteConflictResolution(OrganizationConflictAction.Skip);

    private void CloseConflictOverlayButton_Click(object sender, RoutedEventArgs e) =>
        CompleteConflictResolution(
            OrganizationConflictAction.Skip,
            allowApplyToRemaining: false);

    private void ConflictBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        CompleteConflictResolution(
            OrganizationConflictAction.Skip,
            allowApplyToRemaining: false);

    private void CompleteConflictResolution(
        OrganizationConflictAction action,
        bool allowApplyToRemaining = true)
    {
        var completion = _conflictResolutionTcs;
        if (completion is null)
        {
            ConflictOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        _conflictResolutionTcs = null;
        ConflictOverlay.Visibility = Visibility.Collapsed;

        completion.TrySetResult(
            new OrganizationConflictResolution(
                action,
                allowApplyToRemaining &&
                ConflictApplyAllCheckBox.IsChecked == true));
    }

    private async void OrganizeButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteOrganizationAsync();
    }

    private async Task ExecuteOrganizationAsync()
    {
        var movableFiles = _files
            .Where(file => file.IsClassified)
            .ToList();

        if (movableFiles.Count == 0 || _lastAnalysis is null)
        {
            return;
        }

        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();

        var requestItems = _files
            .Select(file => new OrganizationExecutionRequestItem(
                file.FullPath,
                file.FileName,
                file.SizeBytes,
                file.ModifiedUtcTicks,
                file.CategoryId,
                file.CategoryName,
                file.CategoryOrder,
                file.IsDirectory
                    ? OrganizationAnalysisItemKind.Folder
                    : OrganizationAnalysisItemKind.File,
                file.ContainedFileCount,
                file.ContentFingerprint))
            .ToList();

        InitialStatePanel.Visibility = Visibility.Collapsed;
        PreviewStatePanel.Visibility = Visibility.Collapsed;
        CompletionStatePanel.Visibility = Visibility.Collapsed;
        ProgressStatePanel.Visibility = Visibility.Visible;

        OrganizationProgressBar.Value = 0;
        ProgressCountText.Text = $"0 de {requestItems.Count} elementos";
        ProgressStatusText.Text = "Preparando organización segura...";

        var progress = new Progress<OrganizationExecutionProgress>(state =>
        {
            var percentage = state.Total == 0
                ? 0
                : state.Processed * 100.0 / state.Total;

            OrganizationProgressBar.Value = percentage;
            ProgressCountText.Text =
                $"{state.Processed} de {state.Total} elementos";
            ProgressStatusText.Text =
                $"{state.Message}: {state.FileName}";
        });

        try
        {
            var result =
                await global::BandaNV.App.App.OrganizationExecution.ExecuteAsync(
                    global::BandaNV.App.App.Settings.Current,
                    requestItems,
                    progress,
                    _executionCts.Token,
                    this);

            ProgressStatePanel.Visibility = Visibility.Collapsed;
            CompletionStatePanel.Visibility = Visibility.Visible;

            try
            {
                await global::BandaNV.App.App.History.ApplyRetentionAsync(
                    global::BandaNV.App.App.Settings.Current);
            }
            catch
            {
                // La organización ya terminó correctamente. Un fallo de
                // mantenimiento no debe convertirla en error.
            }

            var moved = result.Record.Items.Count(item =>
                item.Status == OrganizationExecutionItemStatus.Moved);

            var unclassified = result.Record.Items.Count(item =>
                item.Status == OrganizationExecutionItemStatus.SkippedUnclassified);

            var conflicts = result.Record.Items.Count(item =>
                item.Status is OrganizationExecutionItemStatus.SkippedConflict or
                    OrganizationExecutionItemStatus.ConflictNeedsDecision);

            var errors = result.Record.Items.Count(item =>
                item.Status is OrganizationExecutionItemStatus.Error or
                    OrganizationExecutionItemStatus.SourceMissing or
                    OrganizationExecutionItemStatus.SourceChanged);

            var parts = new List<string>
            {
                $"{moved} elemento{(moved == 1 ? string.Empty : "s")} organizado{(moved == 1 ? string.Empty : "s")} correctamente."
            };

            if (unclassified > 0)
            {
                parts.Add(
                    $"{unclassified} quedó{(unclassified == 1 ? string.Empty : "aron")} en origen por no tener categoría.");
            }

            if (conflicts > 0)
            {
                parts.Add(
                    $"{conflicts} elemento{(conflicts == 1 ? string.Empty : "s")} no se movió{(conflicts == 1 ? string.Empty : "eron")} por conflicto.");
            }

            if (errors > 0)
            {
                parts.Add(
                    $"{errors} elemento{(errors == 1 ? string.Empty : "s")} no pudo{(errors == 1 ? string.Empty : "ieron")} moverse de forma segura.");
            }

            CompletionText.Text = string.Join(" ", parts);

            _lastAnalysis = null;
        }
        catch (OperationCanceledException)
        {
            ProgressStatePanel.Visibility = Visibility.Collapsed;
            PreviewStatePanel.Visibility = Visibility.Visible;

            FooterStatusText.Foreground = GetBrush("BandaMutedStrongBrush");
            FooterStatusText.Text =
                "La organización fue cancelada. Los movimientos ya completados quedaron registrados.";
        }
        catch (Exception ex)
        {
            ProgressStatePanel.Visibility = Visibility.Collapsed;
            PreviewStatePanel.Visibility = Visibility.Visible;

            FooterStatusText.Foreground = GetBrush("BandaDangerBrush");
            FooterStatusText.Text =
                $"No se pudo completar la organización: {ex.Message}";
        }
    }

    private void ShowInitialState()
    {
        UpdateInitialStateText();
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

    private async Task AnalyzeFilesAsync()
    {
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        await EnsureOthersCategoryIfNeededAsync();
        LoadCategoryOptions();

        AnalyzeButton.IsEnabled = false;
        AnalyzeButton.Content = "Analizando...";
        AnalysisStatusText.Visibility = Visibility.Visible;
        AnalysisStatusText.Foreground = GetBrush("BandaMutedStrongBrush");
        AnalysisStatusText.Text =
            "Leyendo la carpeta de origen en modo seguro. No se moverá ni creará ningún archivo.";

        try
        {
            var result =
                await global::BandaNV.App.App.OrganizationAnalysis.AnalyzeAsync(
                    global::BandaNV.App.App.Settings.Current,
                    _analysisCts.Token);

            _lastAnalysis = result;
            _files.Clear();
            _resolvedAssignments.Clear();

            foreach (var file in result.Files)
            {
                _files.Add(new OrganizePreviewFile(
                    fullPath: file.FullPath,
                    relativePath: file.RelativePath,
                    fileName: file.FileName,
                    extension: file.Extension,
                    sizeBytes: file.SizeBytes,
                    modifiedAt: file.ModifiedAt,
                    modifiedUtcTicks: file.ModifiedUtcTicks,
                    categoryOptions: _categories,
                    categoryId: file.CategoryId,
                    categoryOrder: file.CategoryOrder,
                    categoryName: file.CategoryName,
                    destinationRoot: result.DestinationFolder,
                    conflictBehavior: global::BandaNV.App.App.Settings.Current.ConflictBehavior,
                    hasDestinationConflict: file.HasDestinationConflict,
                    isDirectory: file.IsDirectory,
                    containedFileCount: file.ContainedFileCount,
                    recognizedFileCount: file.RecognizedFileCount,
                    distinctCategoryCount: file.DistinctCategoryCount,
                    scanIncomplete: file.ScanIncomplete,
                    folderFiles: file.FolderContents,
                    contentFingerprint: file.ContentFingerprint));
            }

            ApplyRememberedAssignments();
            ApplyUnknownExtensionBehavior();

            PreviewDestinationText.Text = result.DestinationFolder;

            if (result.Files.Count == 0)
            {
                ShowInitialState();
                AnalysisStatusText.Foreground = GetBrush("BandaAccentBrush");
                AnalysisStatusText.Text =
                    "No se encontraron archivos pendientes en la carpeta de origen.";
                return;
            }

            var settings =
                global::BandaNV.App.App.Settings.Current;

            var hasUnclassified =
                _files.Any(file =>
                    !file.IsClassified);

            var requiresPreviewForUnknown =
                hasUnclassified &&
                settings.UnknownExtensionBehavior.Equals(
                    "Preguntar en la vista previa",
                    StringComparison.OrdinalIgnoreCase);

            var hasFolderItems =
                _files.Any(file =>
                    file.IsDirectory);

            if (settings.PreviewBeforeOrganize ||
                requiresPreviewForUnknown ||
                hasFolderItems)
            {
                ShowPreviewState();

                if (result.SkippedDirectories > 0)
                {
                    FooterStatusText.Foreground =
                        GetBrush("BandaMutedBrush");
                    FooterStatusText.Text =
                        $"Análisis real completado. {result.SkippedDirectories} carpeta(s) no pudieron leerse y fueron omitidas.";
                }

                return;
            }

            if (!_files.Any(file =>
                    file.IsClassified))
            {
                ShowInitialState();
                AnalysisStatusText.Visibility =
                    Visibility.Visible;
                AnalysisStatusText.Foreground =
                    GetBrush("BandaMutedStrongBrush");
                AnalysisStatusText.Text =
                    "No hay archivos clasificables para organizar. Los archivos sin categoría permanecen en origen.";
                return;
            }

            await ExecuteOrganizationAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lastAnalysis = null;
            _files.Clear();
            _resolvedAssignments.Clear();

            ShowInitialState();
            AnalysisStatusText.Visibility = Visibility.Visible;
            AnalysisStatusText.Foreground = GetBrush("BandaDangerBrush");
            AnalysisStatusText.Text = ex.Message;
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            UpdateInitialStateText();
        }
    }

    private void LoadCategoryOptions()
    {
        _categories.Clear();

        foreach (var category in global::BandaNV.App.App.Categories.GetAll())
        {
            _categories.Add(new OrganizeCategoryOption(
                category.Id,
                category.Order,
                category.Name,
                category.ColorHex));
        }
    }

    private void UpdateInitialStateText()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        var source =
            string.IsNullOrWhiteSpace(
                settings.SourceFolder)
                ? "Sin configurar"
                : settings.SourceFolder;

        AnalyzeButton.Content =
            settings.PreviewBeforeOrganize
                ? "Analizar archivos"
                : "Organizar archivos";

        InitialAnalysisDescriptionText.Text =
            settings.PreviewBeforeOrganize
                ? $"Origen: {source}\nBandaNV analizará los archivos y calculará sus destinos sin modificar el disco."
                : settings.UnknownExtensionBehavior.Equals(
                    "Preguntar en la vista previa",
                    StringComparison.OrdinalIgnoreCase)
                    ? $"Origen: {source}\nBandaNV analizará primero. Si encuentra extensiones sin categoría, abrirá la vista previa; si no, organizará directamente."
                    : $"Origen: {source}\nBandaNV analizará y organizará directamente los archivos según la configuración actual.";
    }

    private static async Task PersistRememberedAssignmentAsync(
        string extension,
        OrganizeCategoryOption selectedCategory)
    {
        var categories = global::BandaNV.App.App.Categories.GetAll()
            .Select(category => new CategorySettings(
                category.Id,
                category.Name,
                category.Extensions,
                category.Order,
                category.ColorHex))
            .ToList();

        foreach (var category in categories)
        {
            category.Extensions.RemoveAll(value =>
                value.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        var target = categories.FirstOrDefault(category =>
            category.Id.Equals(
                selectedCategory.Id,
                StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            target = new CategorySettings(
                selectedCategory.Id,
                selectedCategory.Name,
                [extension],
                selectedCategory.Order,
                selectedCategory.ColorHex);

            categories.Add(target);
        }
        else if (!target.Extensions.Contains(
                     extension,
                     StringComparer.OrdinalIgnoreCase))
        {
            target.Extensions.Add(extension);
        }

        await global::BandaNV.App.App.Categories.SaveAllAsync(categories);
    }

    private static async Task PersistNewCategoryAsync(
        OrganizeCategoryOption selectedCategory)
    {
        var categories = global::BandaNV.App.App.Categories.GetAll()
            .Select(category => new CategorySettings(
                category.Id,
                category.Name,
                category.Extensions,
                category.Order,
                category.ColorHex))
            .ToList();

        if (categories.Any(category =>
                category.Id.Equals(
                    selectedCategory.Id,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        categories.Add(new CategorySettings(
            selectedCategory.Id,
            selectedCategory.Name,
            [],
            selectedCategory.Order,
            selectedCategory.ColorHex));

        await global::BandaNV.App.App.Categories.SaveAllAsync(categories);
    }

    private static async Task RemoveRememberedAssignmentAsync(
        string extension,
        string categoryId)
    {
        var categories = global::BandaNV.App.App.Categories.GetAll()
            .Select(category => new CategorySettings(
                category.Id,
                category.Name,
                category.Extensions,
                category.Order,
                category.ColorHex))
            .ToList();

        var target = categories.FirstOrDefault(category =>
            category.Id.Equals(
                categoryId,
                StringComparison.OrdinalIgnoreCase));

        target?.Extensions.RemoveAll(value =>
            value.Equals(extension, StringComparison.OrdinalIgnoreCase));

        await global::BandaNV.App.App.Categories.SaveAllAsync(categories);
    }

    private async Task EnsureOthersCategoryIfNeededAsync()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        if (!settings.UnknownExtensionBehavior.Equals(
                "Mover a OTROS",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var categories =
            global::BandaNV.App.App.Categories
                .GetAll()
                .Select(category =>
                    new CategorySettings(
                        category.Id,
                        category.Name,
                        category.Extensions,
                        category.Order,
                        category.ColorHex))
                .ToList();

        if (categories.Any(category =>
                category.Name.Equals(
                    "OTROS",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var id =
            Guid.NewGuid()
                .ToString("D");

        var order =
            categories.Count == 0
                ? 1
                : categories.Max(category =>
                    category.Order) + 1;

        categories.Add(
            new CategorySettings(
                id,
                "OTROS",
                [],
                order,
                CategoryColorPalette.Generate(
                    id,
                    settings.SecondaryColor)));

        await global::BandaNV.App.App.Categories
            .SaveAllAsync(
                categories);
    }

    private void ApplyUnknownExtensionBehavior()
    {
        var behavior =
            global::BandaNV.App.App.Settings.Current
                .UnknownExtensionBehavior;

        if (!behavior.Equals(
                "Mover a OTROS",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var others =
            _categories.FirstOrDefault(category =>
                category.Name.Equals(
                    "OTROS",
                    StringComparison.OrdinalIgnoreCase));

        if (others is null)
        {
            return;
        }

        foreach (var file in _files.Where(file =>
                     !file.IsDirectory &&
                     !file.IsClassified))
        {
            file.AssignTo(
                others,
                OrganizeAssignmentSource.IndividualOverride);
        }
    }

    private void ApplyRememberedAssignments()
    {
        var pendingByExtension = _files
            .Where(file =>
                !file.IsDirectory &&
                !file.IsClassified)
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
        var classifiedFiles =
            _files
                .Where(file => file.IsClassified)
                .ToList();

        var unclassifiedFiles =
            _files
                .Where(file => !file.IsClassified)
                .ToList();

        var unclassifiedFolders =
            unclassifiedFiles
                .Where(file => file.IsDirectory)
                .ToList();

        var unclassifiedLooseFiles =
            unclassifiedFiles
                .Where(file => !file.IsDirectory)
                .ToList();

        var folderItems =
            _files
                .Where(file => file.IsDirectory)
                .ToList();

        var pendingConflicts =
            classifiedFiles.Count(file =>
                file.HasPendingConflict);

        var unassignedExtensions =
            unclassifiedLooseFiles
                .GroupBy(
                    file => file.Extension,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    group => group.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var samples =
                        group
                            .Take(2)
                            .Select(file => file.FileName)
                            .ToList();

                    var suffix =
                        group.Count() > samples.Count
                            ? " · …"
                            : string.Empty;

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
                !file.IsDirectory &&
                file.Extension.Equals(assignment.Extension, StringComparison.OrdinalIgnoreCase) &&
                file.AssignmentSource == OrganizeAssignmentSource.ExtensionRule &&
                file.IsAssignedTo(assignment.CategoryOrder, assignment.CategoryName));

            assignment.UpdateActiveFileCount(activeCount);
        }

        _isRefreshingPreview = true;

        PreviewFilesList.ItemsSource = null;
        PreviewFilesList.ItemsSource = _files
            .OrderBy(file => file.IsClassified ? 0 : 1)
            .ThenBy(file => file.IsDirectory ? 0 : 1)
            .ThenBy(file => file.CategoryOrder ?? int.MaxValue)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _isRefreshingPreview = false;

        UnassignedExtensionsList.ItemsSource = null;
        UnassignedExtensionsList.ItemsSource = unassignedExtensions;
        UnassignedExtensionsList.Visibility =
            unassignedExtensions.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        UnassignedFoldersList.ItemsSource = null;
        UnassignedFoldersList.ItemsSource = unclassifiedFolders;
        UnassignedFoldersPanel.Visibility =
            unclassifiedFolders.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

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

        var hasFolderItems =
            folderItems.Count > 0;

        OrganizeButton.Content =
            $"Organizar {classifiedFiles.Count} elemento{(classifiedFiles.Count == 1 ? string.Empty : "s")}";

        OrganizeButton.IsEnabled =
            classifiedFiles.Count > 0;

        ToolTipService.SetToolTip(
            OrganizeButton,
            classifiedFiles.Count > 0
                ? "Ejecuta exactamente la organización mostrada en esta vista previa."
                : null);

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
            UnclassifiedMetricSubtitle.Text = "No se moverán sin asignación";

            SummaryUnclassifiedCountText.Foreground = warningBrush;
            FooterStatusText.Foreground = mutedBrush;
            var pendingLabel =
                unclassifiedFiles.Count == 1
                    ? "elemento quedará"
                    : "elementos quedarán";

            FooterStatusText.Text =
                $"{unclassifiedFiles.Count} {pendingLabel} sin mover hasta tener una categoría." +
                (unclassifiedFolders.Count > 0
                    ? $" {unclassifiedFolders.Count} carpeta{(unclassifiedFolders.Count == 1 ? string.Empty : "s")} se asigna{(unclassifiedFolders.Count == 1 ? string.Empty : "n")} desde la vista previa."
                    : string.Empty) +
                (pendingConflicts > 0
                    ? $" Además, {pendingConflicts} conflicto{(pendingConflicts == 1 ? string.Empty : "s")} de nombre se resolverá{(pendingConflicts == 1 ? string.Empty : "n")} al organizar."
                    : string.Empty);
        }
        else
        {
            UnclassifiedMetricCard.Background = cardBrush;
            UnclassifiedMetricCard.BorderBrush = borderBrush;
            UnclassifiedMetricTitle.Foreground = mutedBrush;
            UnclassifiedMetricSubtitle.Foreground = mutedBrush;
            UnclassifiedMetricSubtitle.Text = "Todos tienen destino";

            SummaryUnclassifiedCountText.Foreground = accentBrush;

            if (pendingConflicts > 0)
            {
                FooterStatusText.Foreground = warningBrush;
                FooterStatusText.Text =
                    $"{pendingConflicts} conflicto{(pendingConflicts == 1 ? string.Empty : "s")} de nombre se resolverá{(pendingConflicts == 1 ? string.Empty : "n")} cuando organices.";
            }
            else
            {
                FooterStatusText.Foreground =
                    accentBrush;

                FooterStatusText.Text =
                    hasFolderItems
                        ? $"Todos los elementos tienen destino. {folderItems.Count} carpeta{(folderItems.Count == 1 ? string.Empty : "s")} se moverá{(folderItems.Count == 1 ? string.Empty : "n")} conservando su estructura interna."
                        : "Todos los archivos tienen destino. Ya podés ejecutar esta organización.";
            }
        }

        UnassignedCard.Visibility = Visibility.Visible;

        if (unassignedExtensions.Count > 0 ||
            unclassifiedFolders.Count > 0)
        {
            UnassignedCard.BorderBrush =
                warningBrush;

            UnassignedTitleText.Text =
                "Elementos sin asignar";

            UnassignedDescriptionText.Text =
                unclassifiedFolders.Count > 0
                    ? "Las extensiones nuevas y las carpetas con contenido mixto, desconocido o incompleto se resuelven directamente en este panel."
                    : _resolvedAssignments.Count > 0
                        ? "Todavía quedan extensiones pendientes. Las asignaciones que ya resolviste se mantienen registradas abajo."
                        : "Estas extensiones todavía no pertenecen a ninguna categoría. Podés resolverlas ahora para incluir sus archivos en esta organización.";

            UnassignedSummaryBadge.Background =
                warningSoftBrush;
            UnassignedSummaryText.Foreground =
                warningBrush;

            var parts =
                new List<string>();

            if (unassignedExtensions.Count > 0)
            {
                parts.Add(
                    $"{unassignedExtensions.Count} {(unassignedExtensions.Count == 1 ? "extensión" : "extensiones")}");
            }

            if (unclassifiedFolders.Count > 0)
            {
                parts.Add(
                    $"{unclassifiedFolders.Count} {(unclassifiedFolders.Count == 1 ? "carpeta" : "carpetas")}");
            }

            UnassignedSummaryText.Text =
                string.Join(
                    " · ",
                    parts);

            ResolvedAssignmentsTitle.Text =
                "Asignaciones realizadas";
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
        string fullPath,
        string relativePath,
        string fileName,
        string extension,
        long sizeBytes,
        DateTime modifiedAt,
        long modifiedUtcTicks,
        IReadOnlyList<OrganizeCategoryOption> categoryOptions,
        string? categoryId,
        int? categoryOrder,
        string? categoryName,
        string destinationRoot,
        string conflictBehavior,
        bool hasDestinationConflict,
        bool isDirectory = false,
        int containedFileCount = 1,
        int recognizedFileCount = 0,
        int distinctCategoryCount = 0,
        bool scanIncomplete = false,
        IReadOnlyList<OrganizationAnalysisFolderFile>? folderFiles = null,
        string? contentFingerprint = null)
    {
        ItemId = Guid.NewGuid().ToString("N");
        FullPath = fullPath;
        RelativePath = relativePath;
        FileName = fileName;
        Extension = extension;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        ModifiedUtcTicks = modifiedUtcTicks;
        CategoryOptions = categoryOptions;
        CategoryId = categoryId;
        CategoryOrder = categoryOrder;
        CategoryName = categoryName;
        _destinationRoot = destinationRoot;
        _conflictBehavior = conflictBehavior;
        HasPendingConflict = hasDestinationConflict;
        IsDirectory = isDirectory;
        ContainedFileCount = containedFileCount;
        RecognizedFileCount = recognizedFileCount;
        DistinctCategoryCount = distinctCategoryCount;
        ScanIncomplete = scanIncomplete;
        ContentFingerprint = contentFingerprint;
        FolderContents =
            (folderFiles ?? [])
                .Select(item =>
                    new FolderContentPreviewItem(
                        item.RelativePath,
                        item.FileName,
                        item.Extension,
                        item.SizeBytes,
                        item.ModifiedAt,
                        item.CategoryName))
                .ToList();
        AssignmentSource =
            categoryOrder.HasValue && !string.IsNullOrWhiteSpace(categoryName)
                ? OrganizeAssignmentSource.InitialCategory
                : OrganizeAssignmentSource.Unclassified;
    }

    private readonly string _destinationRoot;
    private readonly string _conflictBehavior;

    public string ItemId { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public long ModifiedUtcTicks { get; }
    public IReadOnlyList<OrganizeCategoryOption> CategoryOptions { get; }
    public bool IsDirectory { get; }
    public int ContainedFileCount { get; }
    public int RecognizedFileCount { get; }
    public int DistinctCategoryCount { get; }
    public bool ScanIncomplete { get; }
    public string? ContentFingerprint { get; }
    public IReadOnlyList<FolderContentPreviewItem> FolderContents { get; }

    public string? CategoryId { get; private set; }
    public int? CategoryOrder { get; private set; }
    public string? CategoryName { get; private set; }
    public OrganizeAssignmentSource AssignmentSource { get; private set; }
    public bool HasPendingConflict { get; private set; }

    public Visibility ConflictVisibility =>
        HasPendingConflict ? Visibility.Visible : Visibility.Collapsed;

    public string ConflictDisplay =>
        HasPendingConflict
            ? IsDirectory
                ? "Conflicto de carpeta · se preguntará al organizar"
                : "Conflicto de nombre · se preguntará al organizar"
            : string.Empty;

    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);

    public OrganizeCategoryOption? SelectedCategory =>
        CategoryOptions.FirstOrDefault(category =>
            IsAssignedTo(category.Order, category.Name));

    public string SelectedCategoryDisplayName =>
        SelectedCategory?.DisplayName ??
        (IsDirectory
            ? "Resolver arriba"
            : "Elegir categoría");

    public bool CanAssignFromPreview =>
        !IsDirectory ||
        IsClassified;

    public Visibility FolderDetailVisibility =>
        IsDirectory
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string FolderIssueDisplay
    {
        get
        {
            if (!IsDirectory)
            {
                return string.Empty;
            }

            var fileCountText =
                $"{ContainedFileCount} archivo{(ContainedFileCount == 1 ? string.Empty : "s")}";

            if (ScanIncomplete)
            {
                return $"{fileCountText} · análisis parcial";
            }

            var unknownCount =
                Math.Max(
                    0,
                    ContainedFileCount -
                    RecognizedFileCount);

            if (DistinctCategoryCount > 1 &&
                unknownCount > 0)
            {
                return $"{fileCountText} · {DistinctCategoryCount} categorías · {unknownCount} sin reconocer";
            }

            if (DistinctCategoryCount > 1)
            {
                return $"{fileCountText} · {DistinctCategoryCount} categorías detectadas";
            }

            if (unknownCount > 0)
            {
                return $"{fileCountText} · {unknownCount} sin reconocer";
            }

            return $"{fileCountText} · requiere asignación manual";
        }
    }

    public string FolderDetailSummary
    {
        get
        {
            var parts =
                new List<string>
                {
                    $"{ContainedFileCount} archivo{(ContainedFileCount == 1 ? string.Empty : "s")}",
                    $"{RecognizedFileCount} reconocido{(RecognizedFileCount == 1 ? string.Empty : "s")}"
                };

            if (DistinctCategoryCount > 0)
            {
                parts.Add(
                    $"{DistinctCategoryCount} categoría{(DistinctCategoryCount == 1 ? string.Empty : "s")}");
            }

            var unknownCount =
                Math.Max(
                    0,
                    ContainedFileCount -
                    RecognizedFileCount);

            if (unknownCount > 0)
            {
                parts.Add(
                    $"{unknownCount} sin reconocer");
            }

            if (ScanIncomplete)
            {
                parts.Add(
                    "análisis parcial");
            }

            return string.Join(
                " · ",
                parts);
        }
    }

    public string ExtensionDisplay
    {
        get
        {
            if (!IsDirectory)
            {
                return Extension.ToUpperInvariant();
            }

            var fileLabel =
                ContainedFileCount == 1
                    ? "1 archivo"
                    : $"{ContainedFileCount} archivos";

            if (ScanIncomplete)
            {
                return $"CARPETA · {fileLabel} · análisis parcial";
            }

            var unknownCount =
                Math.Max(
                    0,
                    ContainedFileCount -
                    RecognizedFileCount);

            if (unknownCount > 0)
            {
                return $"CARPETA · {fileLabel} · {unknownCount} sin reconocer";
            }

            if (DistinctCategoryCount > 1)
            {
                return $"CARPETA · {fileLabel} · {DistinctCategoryCount} categorías";
            }

            return $"CARPETA · {fileLabel}";
        }
    }

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
        CategoryId = category.Id;
        CategoryOrder = category.Order;
        CategoryName = category.Name;
        AssignmentSource = source;
        RefreshConflictState();
    }

    public void ClearAssignment()
    {
        CategoryId = null;
        CategoryOrder = null;
        CategoryName = null;
        AssignmentSource = OrganizeAssignmentSource.Unclassified;
        HasPendingConflict = false;
    }

    private void RefreshConflictState()
    {
        if (!_conflictBehavior.Equals(
                "Preguntar",
                StringComparison.OrdinalIgnoreCase) ||
            !CategoryOrder.HasValue ||
            string.IsNullOrWhiteSpace(CategoryName))
        {
            HasPendingConflict = false;
            return;
        }

        var destinationFolder = CategoryService.GetFolderPath(
            _destinationRoot,
            CategoryOrder.Value,
            CategoryName);

        var destinationPath = Path.Combine(
            destinationFolder,
            FileName);

        try
        {
            HasPendingConflict =
                !Path.GetFullPath(destinationPath).Equals(
                    Path.GetFullPath(FullPath),
                    StringComparison.OrdinalIgnoreCase) &&
                (File.Exists(destinationPath) ||
                 Directory.Exists(destinationPath));
        }
        catch
        {
            HasPendingConflict =
                File.Exists(destinationPath) ||
                Directory.Exists(destinationPath);
        }
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

public sealed class FolderContentPreviewItem
{
    public FolderContentPreviewItem(
        string relativePath,
        string fileName,
        string extension,
        long sizeBytes,
        DateTime modifiedAt,
        string? categoryName)
    {
        RelativePath =
            relativePath;

        FileName =
            fileName;

        Extension =
            extension;

        SizeBytes =
            sizeBytes;

        ModifiedAt =
            modifiedAt;

        CategoryName =
            categoryName;
    }

    public string RelativePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string? CategoryName { get; }

    public string ExtensionDisplay =>
        Extension.ToUpperInvariant();

    public string CategoryDisplay =>
        string.IsNullOrWhiteSpace(
            CategoryName)
            ? "Sin categoría"
            : CategoryName;

    public string SizeDisplay =>
        FormatBytes(
            SizeBytes);

    private static string FormatBytes(
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

public sealed class OrganizeCategoryOption
{
    public OrganizeCategoryOption(
        string id,
        int order,
        string name,
        string colorHex)
    {
        Id = id;
        Order = order;
        Name = name;
        ColorHex = colorHex;
    }

    public string Id { get; }
    public int Order { get; }
    public string Name { get; }
    public string ColorHex { get; }
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
