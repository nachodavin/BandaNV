using System.Globalization;
using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace BandaNV.App.Pages;

public sealed partial class HomePage : Page
{
    private static readonly CultureInfo EsAr =
        CultureInfo.GetCultureInfo("es-AR");

    private readonly Dictionary<string, DonutVisual> _donutVisuals =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<HomeCategoryUsageItem> _currentCategoryUsage = [];
    private readonly object _liveRefreshSync = new();
    private FileSystemWatcher? _sourceWatcher;
    private FileSystemWatcher? _destinationWatcher;
    private CancellationTokenSource? _liveRefreshDebounceCts;
    private bool _isHomeLoaded;
    private int _currentOrganizedFileCount;
    private long _currentOrganizedSize;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private async void HomePage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _isHomeLoaded = true;

        ConfigureLiveWatchers(
            global::BandaNV.App.App.Settings.Current);

        await LoadHomeAsync();
    }

    private void HomePage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _isHomeLoaded = false;
        StopLiveWatchers();
    }

    private async Task LoadHomeAsync()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        UpdateConfigurationState(settings);

        IReadOnlyList<OrganizationExecutionRecord> records = [];
        IReadOnlyList<IndexedSearchFile> organizedFiles = [];

        try
        {
            records =
                await global::BandaNV.App.App.History.LoadAsync();
        }
        catch
        {
            // Inicio continúa funcionando aunque un registro puntual
            // del historial no pueda leerse.
        }

        try
        {
            organizedFiles =
                await global::BandaNV.App.App.SearchIndex.ScanAsync(
                    settings);
        }
        catch
        {
            // El estado actual de la biblioteca queda vacío si el destino
            // no puede escanearse temporalmente.
        }

        UpdateLastOrganization(records);
        BuildCurrentLibrary(organizedFiles, settings);
        await UpdatePendingFilesAsync(settings);
    }

    private void ConfigureLiveWatchers(
        AppSettings settings)
    {
        StopLiveWatchers();

        _sourceWatcher = CreateWatcher(
            settings.SourceFolder,
            settings.IncludeSubfolders);

        _destinationWatcher = CreateWatcher(
            settings.DestinationFolder,
            includeSubdirectories: true);
    }

    private FileSystemWatcher? CreateWatcher(
        string? path,
        bool includeSubdirectories)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var expanded =
                System.IO.Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(
                        path.Trim()));

            if (!Directory.Exists(expanded))
            {
                return null;
            }

            var watcher = new FileSystemWatcher(expanded)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.Size |
                    NotifyFilters.LastWrite,
                Filter = "*",
                EnableRaisingEvents = false
            };

            watcher.Created += WatchedFileSystem_Changed;
            watcher.Deleted += WatchedFileSystem_Changed;
            watcher.Changed += WatchedFileSystem_Changed;
            watcher.Renamed += WatchedFileSystem_Renamed;
            watcher.Error += WatchedFileSystem_Error;
            watcher.EnableRaisingEvents = true;

            return watcher;
        }
        catch
        {
            return null;
        }
    }

    private void WatchedFileSystem_Changed(
        object sender,
        FileSystemEventArgs e)
    {
        QueueLiveRefresh();
    }

    private void WatchedFileSystem_Renamed(
        object sender,
        RenamedEventArgs e)
    {
        QueueLiveRefresh();
    }

    private void WatchedFileSystem_Error(
        object sender,
        ErrorEventArgs e)
    {
        QueueLiveRefresh();
    }

    private void QueueLiveRefresh()
    {
        CancellationTokenSource next;

        lock (_liveRefreshSync)
        {
            _liveRefreshDebounceCts?.Cancel();
            _liveRefreshDebounceCts?.Dispose();

            next = new CancellationTokenSource();
            _liveRefreshDebounceCts = next;
        }

        _ = DebouncedLiveRefreshAsync(next.Token);
    }

    private async Task DebouncedLiveRefreshAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(300),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (cancellationToken.IsCancellationRequested ||
                !_isHomeLoaded)
            {
                return;
            }

            await LoadHomeAsync();
        });
    }

    private void StopLiveWatchers()
    {
        DisposeWatcher(ref _sourceWatcher);
        DisposeWatcher(ref _destinationWatcher);

        lock (_liveRefreshSync)
        {
            _liveRefreshDebounceCts?.Cancel();
            _liveRefreshDebounceCts?.Dispose();
            _liveRefreshDebounceCts = null;
        }
    }

    private void DisposeWatcher(
        ref FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= WatchedFileSystem_Changed;
            watcher.Deleted -= WatchedFileSystem_Changed;
            watcher.Changed -= WatchedFileSystem_Changed;
            watcher.Renamed -= WatchedFileSystem_Renamed;
            watcher.Error -= WatchedFileSystem_Error;
            watcher.Dispose();
        }
        catch
        {
        }
        finally
        {
            watcher = null;
        }
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
        HomeUnassignedFilesText.Text = "—";
        HomePendingDetailText.Text =
            "Analizando origen...";
        HomeUnassignedDetailText.Text =
            "Analizando origen...";

        try
        {
            var result =
                await global::BandaNV.App.App.OrganizationAnalysis.AnalyzeAsync(
                    settings);

            HomePendingFilesText.Text =
                result.Files.Count.ToString(
                    CultureInfo.CurrentCulture);

            HomeUnassignedFilesText.Text =
                result.UnclassifiedCount.ToString(
                    CultureInfo.CurrentCulture);

            UpdateUnassignedCardState(
                result.UnclassifiedCount);

            HomePendingDetailText.Text =
                result.Files.Count == 0
                    ? "No hay archivos pendientes"
                    : result.ClassifiedCount == 1
                        ? "1 archivo listo para organizar"
                        : $"{result.ClassifiedCount} archivos listos para organizar";

            HomeUnassignedDetailText.Text =
                result.UnclassifiedCount == 0
                    ? "Todo tiene una categoría asignada"
                    : result.UnclassifiedCount == 1
                        ? "1 archivo necesita categoría"
                        : $"{result.UnclassifiedCount} archivos necesitan categoría";
        }
        catch (DirectoryNotFoundException)
        {
            HomePendingFilesText.Text = "—";
            HomeUnassignedFilesText.Text = "—";
            UpdateUnassignedCardState(0);
            HomePendingDetailText.Text =
                "La carpeta de origen no está disponible";
            HomeUnassignedDetailText.Text =
                "No se pudo revisar el origen";
        }
        catch
        {
            HomePendingFilesText.Text = "—";
            HomeUnassignedFilesText.Text = "—";
            UpdateUnassignedCardState(0);
            HomePendingDetailText.Text =
                "No se pudo analizar el origen";
            HomeUnassignedDetailText.Text =
                "No se pudo analizar el origen";
        }
    }

    private void UpdateUnassignedCardState(
        int unassignedCount)
    {
        var hasUnassigned = unassignedCount > 0;

        HomeUnassignedCard.BorderBrush =
            hasUnassigned
                ? (Brush)Resources["HomeWarningBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaBorderBrush"];

        HomeUnassignedCard.Background =
            hasUnassigned
                ? (Brush)Resources["HomeWarningSoftBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaCardBrush"];

        HomeUnassignedTitleText.Foreground =
            hasUnassigned
                ? (Brush)Resources["HomeWarningBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaMutedStrongBrush"];

        HomeUnassignedDetailText.Foreground =
            hasUnassigned
                ? (Brush)Resources["HomeWarningBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaMutedBrush"];

        HomeUnassignedIconBorder.Background =
            hasUnassigned
                ? (Brush)Resources["HomeWarningSoftBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaNavIconBrush"];

        HomeUnassignedIconText.Foreground =
            hasUnassigned
                ? (Brush)Resources["HomeWarningBrush"]
                : (Brush)Application.Current.Resources[
                    "BandaAccentBrush"];
    }

    private void UpdateLastOrganization(
        IReadOnlyList<OrganizationExecutionRecord> records)
    {
        var last = records
            .Where(IsOrganization)
            .OrderByDescending(record => record.StartedAt)
            .FirstOrDefault();

        if (last is null)
        {
            HomeLastOrganizationHeaderText.Text = "—";
            HomeLastMovedSizeText.Text = "—";
            HomeLastMovedSizeDetailText.Text =
                "Sin organizaciones todavía";
            return;
        }

        var movedItems = last.Items
            .Where(item =>
                item.Status ==
                OrganizationExecutionItemStatus.Moved)
            .ToList();

        var movedSize =
            movedItems.Sum(item => item.SizeBytes);

        HomeLastOrganizationHeaderText.Text =
            last.StartedAt.ToString(
                "dd/MM · HH:mm:ss",
                EsAr);

        HomeLastMovedSizeText.Text =
            FormatBytes(movedSize);

        HomeLastMovedSizeDetailText.Text =
            $"{movedItems.Count} archivo{(movedItems.Count == 1 ? string.Empty : "s")} · " +
            last.StartedAt.ToString(
                "dd/MM/yyyy · HH:mm:ss",
                EsAr);
    }

    private void BuildCurrentLibrary(
        IReadOnlyList<IndexedSearchFile> files,
        AppSettings settings)
    {
        _currentOrganizedFileCount = files.Count;
        _currentOrganizedSize =
            files.Sum(file => file.SizeBytes);

        HomeCurrentOrganizedFilesText.Text =
            _currentOrganizedFileCount.ToString(
                CultureInfo.CurrentCulture);

        HomeCurrentTotalSizeText.Text =
            FormatBytes(_currentOrganizedSize);

        var categoryById = settings.Categories
            .ToDictionary(
                category => category.Id,
                StringComparer.OrdinalIgnoreCase);

        _currentCategoryUsage = files
            .GroupBy(
                file => file.CategoryId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();

                var category =
                    categoryById.TryGetValue(
                        group.Key,
                        out var currentCategory)
                        ? currentCategory
                        : null;

                var count = group.Count();
                var percentage =
                    files.Count == 0
                        ? 0
                        : count * 100.0 / files.Count;

                return new HomeCategoryUsageItem(
                    group.Key,
                    category?.Name ?? first.CategoryName,
                    category?.Order ?? first.CategoryOrder,
                    count,
                    group.Sum(file => file.SizeBytes),
                    percentage);
            })
            .OrderBy(item => item.Order)
            .ThenBy(
                item => item.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        HomeCategoriesInUseText.Text =
            $"{_currentCategoryUsage.Count} / {settings.Categories.Count}";

        BuildDonut();
    }

    private void BuildDonut()
    {
        HomeDonutCanvas.Children.Clear();
        HomeDonutLegendPanel.Children.Clear();
        _donutVisuals.Clear();

        ResetDonutCenter();

        if (_currentCategoryUsage.Count == 0 ||
            _currentOrganizedFileCount == 0)
        {
            HomeDonutLegendPanel.Children.Add(
                new TextBlock
                {
                    Text = "Todavía no hay categorías con archivos.",
                    Foreground =
                        (Brush)Application.Current.Resources[
                            "BandaMutedBrush"],
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 10, 4, 0)
                });

            return;
        }

        var startAngle = -90d;

        foreach (var item in _currentCategoryUsage)
        {
            var sweep =
                360d * item.FileCount /
                _currentOrganizedFileCount;

            var gap =
                _currentCategoryUsage.Count == 1
                    ? 0.35
                    : Math.Min(
                        2.2,
                        Math.Max(0.45, sweep * 0.08));

            var visibleSweep =
                Math.Max(0.35, sweep - gap);

            var brush =
                CreateCategoryBrush(item.Id);

            var segment =
                CreateDonutSegment(
                    startAngle + gap / 2,
                    visibleSweep,
                    brush);

            segment.Tag = item.Id;
            segment.PointerEntered +=
                (_, _) => HighlightDonutCategory(item.Id);
            segment.PointerExited +=
                (_, _) => ResetDonutHighlight();

            HomeDonutCanvas.Children.Add(segment);

            var legendRow =
                CreateLegendRow(item, brush);

            HomeDonutLegendPanel.Children.Add(
                legendRow);

            _donutVisuals[item.Id] =
                new DonutVisual(
                    segment,
                    legendRow,
                    item);

            startAngle += sweep;
        }
    }

    private Border CreateLegendRow(
        HomeCategoryUsageItem item,
        Brush brush)
    {
        var row = new Border
        {
            Tag = item.Id,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background =
                new SolidColorBrush(
                    Microsoft.UI.Colors.Transparent)
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var dot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = brush,
            VerticalAlignment =
                VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = item.Name,
            Foreground =
                (Brush)Application.Current.Resources[
                    "BandaMutedStrongBrush"],
            FontSize = 16,
            FontWeight =
                Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming =
                TextTrimming.CharacterEllipsis,
            VerticalAlignment =
                VerticalAlignment.Center
        };

        var count = new TextBlock
        {
            Text =
                item.FileCount == 1
                    ? "1 archivo"
                    : $"{item.FileCount} archivos",
            Foreground =
                (Brush)Application.Current.Resources[
                    "BandaMutedBrush"],
            FontSize = 14,
            VerticalAlignment =
                VerticalAlignment.Center
        };

        content.Children.Add(dot);
        content.Children.Add(name);
        content.Children.Add(count);

        row.Child = content;

        row.PointerEntered +=
            (_, _) => HighlightDonutCategory(item.Id);
        row.PointerExited +=
            (_, _) => ResetDonutHighlight();

        return row;
    }

    private static Microsoft.UI.Xaml.Shapes.Path CreateDonutSegment(
        double startAngle,
        double sweepAngle,
        Brush brush)
    {
        const double center = 275;
        const double radius = 215;

        var start =
            PointOnCircle(
                center,
                center,
                radius,
                startAngle);

        var end =
            PointOnCircle(
                center,
                center,
                radius,
                startAngle + sweepAngle);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false
        };

        figure.Segments.Add(
            new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                RotationAngle = 0,
                IsLargeArc = sweepAngle > 180,
                SweepDirection =
                    SweepDirection.Clockwise
            });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 56,
            Opacity = 1,
            IsHitTestVisible = true
        };
    }

    private static Point PointOnCircle(
        double centerX,
        double centerY,
        double radius,
        double angleDegrees)
    {
        var radians =
            angleDegrees * Math.PI / 180d;

        return new Point(
            centerX + radius * Math.Cos(radians),
            centerY + radius * Math.Sin(radians));
    }

    private void HighlightDonutCategory(string categoryId)
    {
        if (!_donutVisuals.TryGetValue(
                categoryId,
                out var active))
        {
            return;
        }

        foreach (var pair in _donutVisuals)
        {
            var isActive =
                pair.Key.Equals(
                    categoryId,
                    StringComparison.OrdinalIgnoreCase);

            pair.Value.Segment.Opacity =
                isActive ? 1 : 0.22;

            pair.Value.Segment.StrokeThickness =
                isActive ? 66 : 52;

            pair.Value.LegendRow.Opacity =
                isActive ? 1 : 0.48;

            pair.Value.LegendRow.Background =
                isActive
                    ? (Brush)Application.Current.Resources[
                        "BandaNavIconBrush"]
                    : new SolidColorBrush(
                        Microsoft.UI.Colors.Transparent);
        }

        HomeDonutCenterCategoryText.Text =
            active.Item.Name.ToUpperInvariant();

        HomeDonutCenterCountText.Text =
            active.Item.FileCount.ToString(
                CultureInfo.CurrentCulture);

        HomeDonutCenterFilesLabelText.Text =
            active.Item.FileCount == 1
                ? "archivo"
                : "archivos";

        HomeDonutCenterSizeText.Text =
            $"Tamaño · {FormatBytes(active.Item.SizeBytes)}";

        HomeDonutCenterUsageText.Text =
            $"% de uso · {active.Item.Percentage:0.#}%";

        HomeDonutCenterSizeText.Visibility =
            Visibility.Visible;

        HomeDonutCenterUsageText.Visibility =
            Visibility.Visible;
    }

    private void ResetDonutHighlight()
    {
        foreach (var visual in _donutVisuals.Values)
        {
            visual.Segment.Opacity = 1;
            visual.Segment.StrokeThickness = 56;
            visual.LegendRow.Opacity = 1;
            visual.LegendRow.Background =
                new SolidColorBrush(
                    Microsoft.UI.Colors.Transparent);
        }

        ResetDonutCenter();
    }

    private void ResetDonutCenter()
    {
        HomeDonutCenterCategoryText.Text =
            "TODAS LAS CATEGORÍAS";

        HomeDonutCenterCountText.Text =
            _currentOrganizedFileCount.ToString(
                CultureInfo.CurrentCulture);

        HomeDonutCenterFilesLabelText.Text =
            "archivos organizados";

        HomeDonutCenterSizeText.Text = string.Empty;
        HomeDonutCenterUsageText.Text = string.Empty;

        HomeDonutCenterSizeText.Visibility =
            Visibility.Collapsed;
        HomeDonutCenterUsageText.Visibility =
            Visibility.Collapsed;
    }

    private static SolidColorBrush CreateCategoryBrush(
        string categoryId)
    {
        var accent =
            (SolidColorBrush)Application.Current.Resources[
                "BandaAccentBrush"];

        var (baseHue, _, _) =
            RgbToHsl(accent.Color);

        var hash =
            StableHash(categoryId);

        var hueOffset =
            (int)(hash % 191) - 95;

        var hue =
            (baseHue + hueOffset + 360) % 360;

        var saturation =
            0.58 + ((hash >> 8) % 13) / 100d;

        var lightness =
            0.54 + ((hash >> 16) % 10) / 100d;

        return new SolidColorBrush(
            HslToColor(
                hue,
                saturation,
                lightness));
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;

        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static (double Hue, double Saturation, double Lightness)
        RgbToHsl(Windows.UI.Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var lightness = (max + min) / 2d;

        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation =
            delta /
            (1 - Math.Abs(2 * lightness - 1));

        double hue;

        if (max == r)
        {
            hue =
                60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue =
                60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue =
                60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, saturation, lightness);
    }

    private static Windows.UI.Color HslToColor(
        double hue,
        double saturation,
        double lightness)
    {
        var chroma =
            (1 - Math.Abs(2 * lightness - 1)) *
            saturation;

        var hPrime = hue / 60d;
        var x =
            chroma *
            (1 - Math.Abs(hPrime % 2 - 1));

        var (r1, g1, b1) =
            hPrime switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };

        var m =
            lightness - chroma / 2d;

        return Windows.UI.Color.FromArgb(
            255,
            ToByte(r1 + m),
            ToByte(g1 + m),
            ToByte(b1 + m));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(
            Math.Clamp(value, 0, 1) * 255);

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

    private sealed record HomeCategoryUsageItem(
        string Id,
        string Name,
        int Order,
        int FileCount,
        long SizeBytes,
        double Percentage);

    private sealed record DonutVisual(
        Microsoft.UI.Xaml.Shapes.Path Segment,
        Border LegendRow,
        HomeCategoryUsageItem Item);
}
