using System.Globalization;
using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class HomePage : Page
{
    private static readonly CultureInfo EsAr =
        CultureInfo.GetCultureInfo("es-AR");

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= HomePage_Loaded;
        await LoadHomeAsync();
    }

    private async Task LoadHomeAsync()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        UpdateConfigurationState(settings);
        HomeCategoriesCountText.Text =
            settings.Categories.Count.ToString(
                CultureInfo.CurrentCulture);

        IReadOnlyList<OrganizationExecutionRecord> records = [];

        try
        {
            records =
                await global::BandaNV.App.App.History.LoadAsync();
        }
        catch
        {
            // Inicio sigue mostrando la configuración aunque el historial
            // no pueda leerse temporalmente.
        }

        UpdateHistoryMetrics(records);
        BuildOrganizationActivity(records);
        BuildCategoryUsage(records, settings);
        BuildRecentActivity(records);

        await UpdatePendingFilesAsync(settings);
    }

    private void UpdateConfigurationState(
        AppSettings settings)
    {
        var source = settings.SourceFolder?.Trim() ?? string.Empty;
        var destination =
            settings.DestinationFolder?.Trim() ?? string.Empty;

        HomeSourceFolderText.Text =
            string.IsNullOrWhiteSpace(source)
                ? "Sin carpeta configurada"
                : source;

        ToolTipService.SetToolTip(
            HomeSourceFolderText,
            HomeSourceFolderText.Text);

        var sourceExists =
            !string.IsNullOrWhiteSpace(source) &&
            Directory.Exists(source);

        var destinationConfigured =
            !string.IsNullOrWhiteSpace(destination);

        if (sourceExists && destinationConfigured)
        {
            HomeStatusText.Text = "Listo para organizar";
            HomeStatusDot.Fill =
                (Brush)Application.Current.Resources[
                    "BandaAccentBrush"];
            return;
        }

        if (!sourceExists)
        {
            HomeStatusText.Text =
                string.IsNullOrWhiteSpace(source)
                    ? "Origen sin configurar"
                    : "Origen no disponible";
        }
        else
        {
            HomeStatusText.Text = "Destino sin configurar";
        }

        HomeStatusDot.Fill =
            (Brush)Application.Current.Resources[
                "BandaMutedBrush"];
    }

    private async Task UpdatePendingFilesAsync(
        AppSettings settings)
    {
        HomePendingFilesText.Text = "—";
        HomePendingDetailText.Text =
            "Analizando carpeta de origen...";

        try
        {
            var result =
                await global::BandaNV.App.App.OrganizationAnalysis.AnalyzeAsync(
                    settings);

            HomePendingFilesText.Text =
                result.Files.Count.ToString(
                    CultureInfo.CurrentCulture);

            if (result.Files.Count == 0)
            {
                HomePendingDetailText.Text =
                    "No hay archivos pendientes";
                return;
            }

            var parts = new List<string>();

            if (result.ClassifiedCount > 0)
            {
                parts.Add(
                    $"{result.ClassifiedCount} clasificados");
            }

            if (result.UnclassifiedCount > 0)
            {
                parts.Add(
                    $"{result.UnclassifiedCount} sin categoría");
            }

            if (result.SkippedDirectories > 0)
            {
                parts.Add(
                    $"{result.SkippedDirectories} carpetas omitidas");
            }

            HomePendingDetailText.Text =
                string.Join(" · ", parts);
        }
        catch (DirectoryNotFoundException)
        {
            HomePendingFilesText.Text = "—";
            HomePendingDetailText.Text =
                "La carpeta de origen no está disponible";
        }
        catch
        {
            HomePendingFilesText.Text = "—";
            HomePendingDetailText.Text =
                "No se pudo analizar el origen";
        }
    }

    private void UpdateHistoryMetrics(
        IReadOnlyList<OrganizationExecutionRecord> records)
    {
        var organizations = records
            .Where(IsOrganization)
            .OrderByDescending(record => record.StartedAt)
            .ToList();

        var movedItems = organizations
            .SelectMany(record => record.Items)
            .Where(item =>
                item.Status ==
                OrganizationExecutionItemStatus.Moved)
            .ToList();

        HomeTotalOrganizedText.Text =
            movedItems.Count.ToString(
                CultureInfo.CurrentCulture);

        HomeTotalMovedSizeText.Text =
            FormatBytes(
                movedItems.Sum(item => item.SizeBytes));

        var last = organizations.FirstOrDefault();

        if (last is null)
        {
            HomeLastOrganizationHeaderText.Text = "—";
            HomeLastExecutionFilesText.Text = "—";
            HomeLastExecutionDateText.Text = "—";
            return;
        }

        var lastMovedCount = last.Items.Count(item =>
            item.Status ==
            OrganizationExecutionItemStatus.Moved);

        HomeLastOrganizationHeaderText.Text =
            last.StartedAt.ToString(
                "dd/MM · HH:mm:ss",
                EsAr);

        HomeLastExecutionFilesText.Text =
            lastMovedCount.ToString(
                CultureInfo.CurrentCulture);

        HomeLastExecutionDateText.Text =
            last.StartedAt.ToString(
                "dd/MM/yyyy · HH:mm:ss",
                EsAr);
    }

    private void BuildOrganizationActivity(
        IReadOnlyList<OrganizationExecutionRecord> records)
    {
        HomeActivityPanel.Children.Clear();

        var activity = records
            .Where(IsOrganization)
            .OrderByDescending(record => record.StartedAt)
            .Take(7)
            .OrderBy(record => record.StartedAt)
            .Select(record => new
            {
                Record = record,
                Count = record.Items.Count(item =>
                    item.Status ==
                    OrganizationExecutionItemStatus.Moved)
            })
            .ToList();

        if (activity.Count == 0)
        {
            AddEmptyState(
                HomeActivityPanel,
                "Sin organizaciones todavía",
                "La actividad real aparecerá acá después de tu primera ejecución.");
            return;
        }

        var maximum =
            Math.Max(
                1,
                activity.Max(item => item.Count));

        foreach (var item in activity)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12
            };

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(92)
                });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

            var date = new TextBlock
            {
                Text = item.Record.StartedAt.ToString(
                    "dd/MM HH:mm",
                    EsAr),
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedStrongBrush"],
                FontSize = 12,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            var bar = new ProgressBar
            {
                Maximum = maximum,
                Value = item.Count,
                Height = 5,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            var count = new TextBlock
            {
                Text = item.Count.ToString(
                    CultureInfo.CurrentCulture),
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaTextBrush"],
                FontSize = 13,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            Grid.SetColumn(date, 0);
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(count, 2);

            grid.Children.Add(date);
            grid.Children.Add(bar);
            grid.Children.Add(count);

            HomeActivityPanel.Children.Add(grid);
        }
    }

    private void BuildCategoryUsage(
        IReadOnlyList<OrganizationExecutionRecord> records,
        AppSettings settings)
    {
        HomeCategoryUsagePanel.Children.Clear();

        var currentNames = settings.Categories
            .ToDictionary(
                category => category.Id,
                category => category.Name,
                StringComparer.OrdinalIgnoreCase);

        var movedItems = records
            .Where(IsOrganization)
            .SelectMany(record => record.Items)
            .Where(item =>
                item.Status ==
                OrganizationExecutionItemStatus.Moved)
            .ToList();

        var total = movedItems.Count;

        var usage = movedItems
            .GroupBy(
                item =>
                    !string.IsNullOrWhiteSpace(item.CategoryId)
                        ? item.CategoryId!
                        : $"legacy:{item.CategoryName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();

                var displayName =
                    !string.IsNullOrWhiteSpace(first.CategoryId) &&
                    currentNames.TryGetValue(
                        first.CategoryId,
                        out var currentName)
                        ? currentName
                        : first.CategoryName ?? "Sin categoría";

                return new
                {
                    Name = displayName,
                    Count = group.Count()
                };
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(
                item => item.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .Take(3)
            .ToList();

        if (usage.Count == 0)
        {
            AddEmptyState(
                HomeCategoryUsagePanel,
                "Sin datos todavía",
                "Las categorías más usadas aparecerán acá.");
            return;
        }

        foreach (var item in usage)
        {
            var percentage =
                total == 0
                    ? 0
                    : item.Count * 100.0 / total;

            var container = new StackPanel
            {
                Spacing = 5
            };

            var header = new Grid();

            header.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });
            header.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

            var name = new TextBlock
            {
                Text = item.Name,
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedStrongBrush"],
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

            var value = new TextBlock
            {
                Text =
                    $"{item.Count} · {percentage:0.#}%",
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedBrush"]
            };

            Grid.SetColumn(name, 0);
            Grid.SetColumn(value, 1);

            header.Children.Add(name);
            header.Children.Add(value);

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = percentage,
                Height = 4
            };

            container.Children.Add(header);
            container.Children.Add(bar);

            HomeCategoryUsagePanel.Children.Add(
                container);
        }
    }

    private void BuildRecentActivity(
        IReadOnlyList<OrganizationExecutionRecord> records)
    {
        HomeRecentActivityPanel.Children.Clear();

        var recent = records
            .OrderByDescending(record => record.StartedAt)
            .Take(3)
            .ToList();

        if (recent.Count == 0)
        {
            AddEmptyState(
                HomeRecentActivityPanel,
                "Sin actividad todavía",
                "Tus últimas ejecuciones aparecerán acá.");
            return;
        }

        foreach (var record in recent)
        {
            var movedCount = record.Items.Count(item =>
                item.Status ==
                OrganizationExecutionItemStatus.Moved);

            var row = new Grid
            {
                ColumnSpacing = 10
            };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });
            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });
            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

            var badge = new Border
            {
                Padding = new Thickness(
                    8,
                    4,
                    8,
                    4),
                CornerRadius =
                    new CornerRadius(9),
                Background =
                    (Brush)Application.Current.Resources[
                        "BandaNavIconBrush"],
                VerticalAlignment =
                    VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = IsOrganization(record)
                        ? "ORG"
                        : "UNDO",
                    Foreground =
                        (Brush)Application.Current.Resources[
                            IsOrganization(record)
                                ? "BandaAccentBrush"
                                : "BandaMutedStrongBrush"],
                    FontSize = 11,
                    FontWeight =
                        Microsoft.UI.Text.FontWeights.SemiBold
                }
            };

            var title = new TextBlock
            {
                Text = IsOrganization(record)
                    ? $"{movedCount} archivos organizados"
                    : $"{movedCount} archivos restaurados",
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaTextBrush"],
                FontSize = 13,
                TextTrimming =
                    TextTrimming.CharacterEllipsis,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            var date = new TextBlock
            {
                Text = record.StartedAt.ToString(
                    "dd/MM · HH:mm:ss",
                    EsAr),
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedBrush"],
                FontSize = 12,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(date, 2);

            row.Children.Add(badge);
            row.Children.Add(title);
            row.Children.Add(date);

            HomeRecentActivityPanel.Children.Add(row);
        }
    }

    private static void AddEmptyState(
        Panel panel,
        string title,
        string subtitle)
    {
        var container = new StackPanel
        {
            Spacing = 5,
            HorizontalAlignment =
                HorizontalAlignment.Center,
            VerticalAlignment =
                VerticalAlignment.Center
        };

        container.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedStrongBrush"],
                FontSize = 14,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment =
                    HorizontalAlignment.Center
            });

        container.Children.Add(
            new TextBlock
            {
                Text = subtitle,
                Foreground =
                    (Brush)Application.Current.Resources[
                        "BandaMutedBrush"],
                FontSize = 13,
                TextWrapping =
                    TextWrapping.Wrap,
                TextAlignment =
                    TextAlignment.Center,
                HorizontalAlignment =
                    HorizontalAlignment.Center
            });

        panel.Children.Add(container);
    }

    private static bool IsOrganization(
        OrganizationExecutionRecord record) =>
        record.Type.Equals(
            "ORGANIZE",
            StringComparison.OrdinalIgnoreCase);

    private static string FormatBytes(long bytes)
    {
        string[] units =
            ["B", "KB", "MB", "GB", "TB"];

        var value =
            (double)Math.Max(0, bytes);
        var index = 0;

        while (value >= 1024 &&
               index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0
            ? $"{value:0} {units[index]}"
            : $"{value:0.##} {units[index]}";
    }
}
