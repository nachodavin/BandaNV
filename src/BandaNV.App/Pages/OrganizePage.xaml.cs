using BandaNV.Core.Models;
using BandaNV.Core.Services;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace BandaNV.App.Pages;

public sealed partial class OrganizePage : Page, IOrganizationConflictResolver
{
    private const double OrganizeDateWheelItemHeight = 36d;

    private readonly List<OrganizePreviewFile> _files = new();

    public ObservableCollection<OrganizePreviewFile> VisibleOrganizePreviewFiles { get; } = new();
    public ObservableCollection<OrganizePreviewGroup> GroupedOrganizePreviewFiles { get; } = new();
    public ObservableCollection<FolderContentPreviewItem> VisibleOrganizeFolderItems { get; } = new();
    public ObservableCollection<OrganizeFolderContentGroup> GroupedOrganizeFolderItems { get; } = new();

    private readonly CollectionViewSource _organizePreviewViewSource = new()
    {
        ItemsPath = new PropertyPath(nameof(OrganizePreviewGroup.Items))
    };

    private readonly CollectionViewSource _organizeFolderViewSource = new()
    {
        ItemsPath = new PropertyPath(nameof(OrganizeFolderContentGroup.Items))
    };

    private SearchDateFilter _organizeDateFilter = SearchDateFilter.All;
    private DateTime? _organizeSpecificDateFilter;
    private SearchSizeFilter _organizeSizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _organizeExtensionFilters =
        new(StringComparer.OrdinalIgnoreCase);
    private SearchSortField _organizeSortField = SearchSortField.DateModified;
    private SearchSortDirection _organizeSortDirection = SearchSortDirection.Descending;
    private SearchGroupField _organizeGroupField = SearchGroupField.None;

    private SearchDateFilter _pendingOrganizeDateFilter = SearchDateFilter.All;
    private DateTime? _pendingOrganizeSpecificDateFilter;
    private SearchSizeFilter _pendingOrganizeSizeFilter = SearchSizeFilter.All;
    private readonly HashSet<string> _pendingOrganizeExtensionFilters =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<int> _organizeDateWheelYears = new();
    private bool _isUpdatingOrganizeDateWheels;
    private int _organizeWheelDay = 1;
    private int _organizeWheelMonth = 1;
    private int _organizeWheelYear = DateTime.Today.Year;
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
    private bool _activeAssignmentIsFolder;
    private OrganizeCategoryOption? _pendingAssignmentCategory;
    private bool _isCreatingAssignmentCategory;

    private OrganizePreviewFile? _folderDetailRoot;
    private string _folderDetailCurrentRelativePath = string.Empty;
    private readonly Stack<string> _folderDetailHistory = new();
    private bool _syncingFolderDetailSelection;
    private bool _syncingUnassignedFolderSelection;
    private bool _syncingUnassignedExtensionSelection;

    private readonly List<OrganizePreviewFile> _managedOrganizeFiles = new();
    private readonly List<OrganizeActionTarget> _managedOrganizeTargets = new();
    private OrganizeManageMode _organizeManageMode = OrganizeManageMode.None;
    private OrganizeCategoryOption? _pendingOrganizeCategory;

    private readonly object _sourceWatcherGate = new();
    private FileSystemWatcher? _sourceWatcher;
    private CancellationTokenSource? _sourceWatcherDebounceCts;
    private string _watchedSourceRoot = string.Empty;
    private bool _watcherIncludesSubdirectories;
    private int _sourceWatcherSuppressionCount;
    private bool _isLiveSourceRefreshRunning;
    private bool _liveSourceRefreshPending;
    private readonly Dictionary<string, string> _pendingExternalRenames =
        new(StringComparer.OrdinalIgnoreCase);

    public OrganizePage()
    {
        InitializeComponent();

        _organizePreviewViewSource.Source =
            VisibleOrganizePreviewFiles;
        PreviewFilesList.ItemsSource =
            _organizePreviewViewSource.View;

        _organizeFolderViewSource.Source =
            VisibleOrganizeFolderItems;
        FolderDetailFilesList.ItemsSource =
            _organizeFolderViewSource.View;

        UpdateOrganizeSortAndGroupSelectorText();
        UpdateOrganizeSortAndGroupOptionHighlights();
        InitializeOrganizeSpecificDateWheels();

        Unloaded += OrganizePage_Unloaded;
        LoadCategoryOptions();
        UpdateInitialStateText();
        ShowInitialState();
    }

    private void OrganizePage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        StopSourceWatcher();
    }

    private bool IsSourceWatcherSuppressed =>
        Volatile.Read(
            ref _sourceWatcherSuppressionCount) > 0;

    private void BeginSourceWatcherSuppression() =>
        Interlocked.Increment(
            ref _sourceWatcherSuppressionCount);

    private void EndSourceWatcherSuppression()
    {
        var value =
            Interlocked.Decrement(
                ref _sourceWatcherSuppressionCount);

        if (value < 0)
        {
            Interlocked.Exchange(
                ref _sourceWatcherSuppressionCount,
                0);
        }
    }

    private void StartSourceWatcher()
    {
        var settings =
            global::BandaNV.App.App.Settings.Current;

        string sourceRoot;

        try
        {
            sourceRoot =
                Path.GetFullPath(
                    settings.SourceFolder);
        }
        catch
        {
            StopSourceWatcher();
            return;
        }

        if (!Directory.Exists(
                sourceRoot))
        {
            StopSourceWatcher();
            return;
        }

        var includeSubdirectories =
            settings.OrganizeFoldersAsUnits;

        lock (_sourceWatcherGate)
        {
            if (_sourceWatcher is not null &&
                _watchedSourceRoot.Equals(
                    sourceRoot,
                    StringComparison.OrdinalIgnoreCase) &&
                _watcherIncludesSubdirectories ==
                    includeSubdirectories)
            {
                _sourceWatcher.EnableRaisingEvents =
                    true;
                return;
            }
        }

        StopSourceWatcher();

        try
        {
            var watcher =
                new FileSystemWatcher(
                    sourceRoot)
                {
                    Filter =
                        "*",
                    IncludeSubdirectories =
                        includeSubdirectories,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime,
                    InternalBufferSize =
                        32 * 1024
                };

            watcher.Changed +=
                SourceWatcher_Changed;
            watcher.Created +=
                SourceWatcher_Changed;
            watcher.Deleted +=
                SourceWatcher_Changed;
            watcher.Renamed +=
                SourceWatcher_Renamed;
            watcher.Error +=
                SourceWatcher_Error;

            lock (_sourceWatcherGate)
            {
                _sourceWatcher =
                    watcher;
                _watchedSourceRoot =
                    sourceRoot;
                _watcherIncludesSubdirectories =
                    includeSubdirectories;
                _pendingExternalRenames.Clear();
                watcher.EnableRaisingEvents =
                    true;
            }
        }
        catch
        {
            StopSourceWatcher();
        }
    }

    private void StopSourceWatcher()
    {
        FileSystemWatcher? watcher;
        CancellationTokenSource? debounce;

        lock (_sourceWatcherGate)
        {
            watcher =
                _sourceWatcher;
            _sourceWatcher =
                null;

            debounce =
                _sourceWatcherDebounceCts;
            _sourceWatcherDebounceCts =
                null;

            _watchedSourceRoot =
                string.Empty;
            _watcherIncludesSubdirectories =
                false;
            _pendingExternalRenames.Clear();
            _liveSourceRefreshPending =
                false;
        }

        try
        {
            debounce?.Cancel();
        }
        catch
        {
        }

        debounce?.Dispose();

        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents =
                false;

            watcher.Changed -=
                SourceWatcher_Changed;
            watcher.Created -=
                SourceWatcher_Changed;
            watcher.Deleted -=
                SourceWatcher_Changed;
            watcher.Renamed -=
                SourceWatcher_Renamed;
            watcher.Error -=
                SourceWatcher_Error;

            watcher.Dispose();
        }
        catch
        {
        }
    }

    private void SourceWatcher_Changed(
        object sender,
        FileSystemEventArgs e)
    {
        if (IsSourceWatcherSuppressed ||
            ShouldIgnoreSourceWatcherPath(
                e.FullPath))
        {
            return;
        }

        ScheduleLiveSourceRefresh();
    }

    private void SourceWatcher_Renamed(
        object sender,
        RenamedEventArgs e)
    {
        if (IsSourceWatcherSuppressed)
        {
            return;
        }

        var oldRelevant =
            !ShouldIgnoreSourceWatcherPath(
                e.OldFullPath);

        var newRelevant =
            !ShouldIgnoreSourceWatcherPath(
                e.FullPath);

        if (!oldRelevant &&
            !newRelevant)
        {
            return;
        }

        if (oldRelevant &&
            newRelevant)
        {
            lock (_sourceWatcherGate)
            {
                _pendingExternalRenames[
                    Path.GetFullPath(
                        e.OldFullPath)] =
                    Path.GetFullPath(
                        e.FullPath);
            }
        }

        ScheduleLiveSourceRefresh();
    }

    private void SourceWatcher_Error(
        object sender,
        ErrorEventArgs e)
    {
        if (IsSourceWatcherSuppressed)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            () =>
            {
                if (PreviewStatePanel.Visibility !=
                    Visibility.Visible)
                {
                    return;
                }

                // Si Windows perdió eventos por desborde del buffer,
                // reiniciamos el watcher y hacemos un análisis completo.
                StopSourceWatcher();
                StartSourceWatcher();
                ScheduleLiveSourceRefresh();
            });
    }

    private bool ShouldIgnoreSourceWatcherPath(
        string path)
    {
        try
        {
            var fullPath =
                Path.GetFullPath(
                    path);

            var destinationSetting =
                global::BandaNV.App.App.Settings.Current
                    .DestinationFolder;

            if (string.IsNullOrWhiteSpace(
                    destinationSetting))
            {
                return false;
            }

            var destination =
                Path.GetFullPath(
                    destinationSetting);

            return IsSameOrInsidePath(
                fullPath,
                destination);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameOrInsidePath(
        string candidate,
        string root)
    {
        var normalizedCandidate =
            Path.GetFullPath(
                    candidate)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var normalizedRoot =
            Path.GetFullPath(
                    root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        return normalizedCandidate.Equals(
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleLiveSourceRefresh()
    {
        CancellationTokenSource debounce;

        lock (_sourceWatcherGate)
        {
            if (_sourceWatcher is null)
            {
                return;
            }

            try
            {
                _sourceWatcherDebounceCts?.Cancel();
            }
            catch
            {
            }

            _sourceWatcherDebounceCts?.Dispose();

            debounce =
                new CancellationTokenSource();

            _sourceWatcherDebounceCts =
                debounce;
        }

        _ =
            DebounceLiveSourceRefreshAsync(
                debounce);
    }

    private async Task DebounceLiveSourceRefreshAsync(
        CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(
                750,
                debounce.Token);

            if (debounce.IsCancellationRequested ||
                IsSourceWatcherSuppressed)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(
                async () =>
                {
                    await HandleLiveSourceRefreshAsync();
                });
        }
        catch (OperationCanceledException)
        {
        }
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

    private void OrganizeFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateOrganizeFilterOverlayControls();
        OrganizeFiltersOverlay.Visibility =
            Visibility.Visible;
    }

    private void CloseOrganizeFiltersOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OrganizeFiltersOverlay.Visibility =
            Visibility.Collapsed;
    }

    private void OrganizeFiltersBackdrop_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        OrganizeFiltersOverlay.Visibility =
            Visibility.Collapsed;
    }

    private void ResetOrganizeFiltersOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _pendingOrganizeDateFilter =
            SearchDateFilter.All;
        _pendingOrganizeSpecificDateFilter =
            null;
        _pendingOrganizeSizeFilter =
            SearchSizeFilter.All;
        _pendingOrganizeExtensionFilters.Clear();

        OrganizeSpecificDateWheelPanel.Visibility =
            Visibility.Collapsed;

        UpdatePendingOrganizeFilterLabels();
        BuildOrganizeExtensionFilterOptions();
    }

    private void ApplyOrganizeFiltersOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pendingOrganizeDateFilter ==
                SearchDateFilter.SpecificDate &&
            !_pendingOrganizeSpecificDateFilter.HasValue)
        {
            _pendingOrganizeSpecificDateFilter =
                DateTime.Today;
        }

        _organizeDateFilter =
            _pendingOrganizeDateFilter;

        _organizeSpecificDateFilter =
            _pendingOrganizeDateFilter ==
                SearchDateFilter.SpecificDate
                ? _pendingOrganizeSpecificDateFilter?.Date
                : null;

        _organizeSizeFilter =
            _pendingOrganizeSizeFilter;

        _organizeExtensionFilters.Clear();
        _organizeExtensionFilters.UnionWith(
            _pendingOrganizeExtensionFilters);

        OrganizeFiltersOverlay.Visibility =
            Visibility.Collapsed;

        RefreshPreview();
    }

    private void PopulateOrganizeFilterOverlayControls()
    {
        _pendingOrganizeDateFilter =
            _organizeDateFilter;
        _pendingOrganizeSpecificDateFilter =
            _organizeSpecificDateFilter;
        _pendingOrganizeSizeFilter =
            _organizeSizeFilter;

        _pendingOrganizeExtensionFilters.Clear();
        _pendingOrganizeExtensionFilters.UnionWith(
            _organizeExtensionFilters);

        OrganizeSpecificDateWheelPanel.Visibility =
            _pendingOrganizeDateFilter ==
                SearchDateFilter.SpecificDate
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (_pendingOrganizeDateFilter ==
            SearchDateFilter.SpecificDate)
        {
            var selectedDate =
                _pendingOrganizeSpecificDateFilter ??
                DateTime.Today;

            _pendingOrganizeSpecificDateFilter =
                selectedDate.Date;

            SetOrganizeSpecificDateWheel(
                selectedDate);
            QueueOrganizeSpecificDateWheelSync();
        }

        UpdatePendingOrganizeFilterLabels();
        BuildOrganizeExtensionFilterOptions();
    }

    private void OrganizeDateFilterOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterKey } ||
            !Enum.TryParse<SearchDateFilter>(
                filterKey,
                out var parsedFilter))
        {
            return;
        }

        _pendingOrganizeDateFilter =
            parsedFilter;

        if (parsedFilter ==
            SearchDateFilter.SpecificDate)
        {
            var selectedDate =
                DateTime.Today;

            _pendingOrganizeSpecificDateFilter =
                selectedDate.Date;
            OrganizeSpecificDateWheelPanel.Visibility =
                Visibility.Visible;

            SetOrganizeSpecificDateWheel(
                selectedDate);
            QueueOrganizeSpecificDateWheelSync();
        }
        else
        {
            OrganizeSpecificDateWheelPanel.Visibility =
                Visibility.Collapsed;
        }

        OrganizeDateFilterValueText.Text =
            GetOrganizeDateFilterDisplayName(
                parsedFilter,
                _pendingOrganizeSpecificDateFilter);

        OrganizeDateFilterFlyout.Hide();
    }

    private void OrganizeSpecificDateTodayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _pendingOrganizeDateFilter =
            SearchDateFilter.SpecificDate;
        _pendingOrganizeSpecificDateFilter =
            DateTime.Today;

        OrganizeSpecificDateWheelPanel.Visibility =
            Visibility.Visible;

        SetOrganizeSpecificDateWheel(
            DateTime.Today);

        OrganizeDateFilterValueText.Text =
            GetOrganizeDateFilterDisplayName(
                SearchDateFilter.SpecificDate,
                _pendingOrganizeSpecificDateFilter);

        QueueOrganizeSpecificDateWheelSync();
    }

    private void OrganizeDateWheelScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_isUpdatingOrganizeDateWheels ||
            e.IsIntermediate ||
            sender is not ScrollViewer
            {
                Tag: string wheelName
            } scrollViewer)
        {
            return;
        }

        var itemCount =
            wheelName switch
            {
                "Day" =>
                    DateTime.DaysInMonth(
                        _organizeWheelYear,
                        _organizeWheelMonth),
                "Month" =>
                    12,
                "Year" =>
                    _organizeDateWheelYears.Count,
                _ =>
                    0
            };

        if (itemCount <= 0)
        {
            return;
        }

        var index =
            Math.Clamp(
                (int)Math.Round(
                    scrollViewer.VerticalOffset /
                    OrganizeDateWheelItemHeight),
                0,
                itemCount - 1);

        _isUpdatingOrganizeDateWheels =
            true;

        scrollViewer.ChangeView(
            null,
            index * OrganizeDateWheelItemHeight,
            null,
            true);

        _isUpdatingOrganizeDateWheels =
            false;

        var rebuildDays =
            false;

        switch (wheelName)
        {
            case "Day":
                _organizeWheelDay =
                    index + 1;
                break;

            case "Month":
                _organizeWheelMonth =
                    index + 1;
                rebuildDays =
                    true;
                break;

            case "Year":
                _organizeWheelYear =
                    _organizeDateWheelYears[index];
                rebuildDays =
                    true;
                break;
        }

        if (rebuildDays)
        {
            var daysInMonth =
                DateTime.DaysInMonth(
                    _organizeWheelYear,
                    _organizeWheelMonth);

            _organizeWheelDay =
                Math.Min(
                    _organizeWheelDay,
                    daysInMonth);

            RebuildOrganizeDayWheelItems();
            QueueOrganizeSpecificDateWheelSync();
        }

        _pendingOrganizeSpecificDateFilter =
            new DateTime(
                _organizeWheelYear,
                _organizeWheelMonth,
                _organizeWheelDay);

        OrganizeDateFilterValueText.Text =
            GetOrganizeDateFilterDisplayName(
                SearchDateFilter.SpecificDate,
                _pendingOrganizeSpecificDateFilter);
    }

    private void InitializeOrganizeSpecificDateWheels()
    {
        var culture =
            CultureInfo.GetCultureInfo(
                "es-AR");

        OrganizeMonthWheelItems.ItemsSource =
            Enumerable.Range(
                    1,
                    12)
                .Select(month =>
                    culture.TextInfo.ToTitleCase(
                        culture.DateTimeFormat
                            .GetMonthName(
                                month)))
                .ToList();

        const int firstYear =
            1900;

        var lastYear =
            DateTime.Today.Year + 20;

        _organizeDateWheelYears.Clear();
        _organizeDateWheelYears.AddRange(
            Enumerable.Range(
                firstYear,
                (lastYear - firstYear) + 1));

        OrganizeYearWheelItems.ItemsSource =
            _organizeDateWheelYears
                .Select(year =>
                    year.ToString(
                        CultureInfo.InvariantCulture))
                .ToList();

        SetOrganizeSpecificDateWheel(
            DateTime.Today);
    }

    private void SetOrganizeSpecificDateWheel(
        DateTime date)
    {
        var minYear =
            _organizeDateWheelYears.First();
        var maxYear =
            _organizeDateWheelYears.Last();

        _organizeWheelYear =
            Math.Clamp(
                date.Year,
                minYear,
                maxYear);

        _organizeWheelMonth =
            Math.Clamp(
                date.Month,
                1,
                12);

        var daysInMonth =
            DateTime.DaysInMonth(
                _organizeWheelYear,
                _organizeWheelMonth);

        _organizeWheelDay =
            Math.Clamp(
                date.Day,
                1,
                daysInMonth);

        RebuildOrganizeDayWheelItems();
    }

    private void RebuildOrganizeDayWheelItems()
    {
        var daysInMonth =
            DateTime.DaysInMonth(
                _organizeWheelYear,
                _organizeWheelMonth);

        OrganizeDayWheelItems.ItemsSource =
            Enumerable.Range(
                    1,
                    daysInMonth)
                .Select(day =>
                    day.ToString(
                        "00",
                        CultureInfo.InvariantCulture))
                .ToList();
    }

    private void QueueOrganizeSpecificDateWheelSync()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            OrganizeSpecificDateWheelPanel.UpdateLayout();
            OrganizeDayWheelScrollViewer.UpdateLayout();
            OrganizeMonthWheelScrollViewer.UpdateLayout();
            OrganizeYearWheelScrollViewer.UpdateLayout();

            SyncOrganizeSpecificDateWheelOffsets();

            DispatcherQueue.TryEnqueue(
                SyncOrganizeSpecificDateWheelOffsets);
        });
    }

    private void SyncOrganizeSpecificDateWheelOffsets()
    {
        if (OrganizeSpecificDateWheelPanel.Visibility !=
            Visibility.Visible)
        {
            return;
        }

        _isUpdatingOrganizeDateWheels =
            true;

        OrganizeDayWheelScrollViewer.ChangeView(
            null,
            (_organizeWheelDay - 1) *
                OrganizeDateWheelItemHeight,
            null,
            true);

        OrganizeMonthWheelScrollViewer.ChangeView(
            null,
            (_organizeWheelMonth - 1) *
                OrganizeDateWheelItemHeight,
            null,
            true);

        var yearIndex =
            _organizeDateWheelYears.IndexOf(
                _organizeWheelYear);

        OrganizeYearWheelScrollViewer.ChangeView(
            null,
            Math.Max(
                0,
                yearIndex) *
                OrganizeDateWheelItemHeight,
            null,
            true);

        _isUpdatingOrganizeDateWheels =
            false;
    }

    private void OrganizeSizeFilterOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterKey } ||
            !Enum.TryParse<SearchSizeFilter>(
                filterKey,
                out var parsedFilter))
        {
            return;
        }

        _pendingOrganizeSizeFilter =
            parsedFilter;

        OrganizeSizeFilterValueText.Text =
            GetOrganizeSizeFilterDisplayName(
                parsedFilter);

        OrganizeSizeFilterFlyout.Hide();
    }

    private void OrganizeExtensionFilterOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: string extensionKey
            })
        {
            return;
        }

        if (extensionKey.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingOrganizeExtensionFilters.Clear();
        }
        else if (!_pendingOrganizeExtensionFilters.Add(
                     extensionKey))
        {
            _pendingOrganizeExtensionFilters.Remove(
                extensionKey);
        }

        OrganizeExtensionFilterValueText.Text =
            GetOrganizeExtensionFilterDisplayName(
                _pendingOrganizeExtensionFilters);

        BuildOrganizeExtensionFilterOptions();
    }

    private void OrganizeSortFieldOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sortKey })
        {
            return;
        }

        _organizeSortField =
            sortKey switch
            {
                "Name" =>
                    SearchSortField.Name,
                "Size" =>
                    SearchSortField.Size,
                "Category" =>
                    SearchSortField.Category,
                "Extension" =>
                    SearchSortField.Extension,
                _ =>
                    SearchSortField.DateModified
            };

        UpdateOrganizeSortAndGroupSelectorText();
        UpdateOrganizeSortAndGroupOptionHighlights();

        OrganizeSortFlyout.Hide();
        RefreshPreview();
    }

    private void OrganizeSortDirectionOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: string directionKey
            })
        {
            return;
        }

        _organizeSortDirection =
            directionKey.Equals(
                "Ascending",
                StringComparison.OrdinalIgnoreCase)
                ? SearchSortDirection.Ascending
                : SearchSortDirection.Descending;

        UpdateOrganizeSortAndGroupSelectorText();
        UpdateOrganizeSortAndGroupOptionHighlights();

        OrganizeSortFlyout.Hide();
        RefreshPreview();
    }

    private void OrganizeGroupFieldOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string groupKey })
        {
            return;
        }

        _organizeGroupField =
            groupKey switch
            {
                "Name" =>
                    SearchGroupField.Name,
                "DateModified" =>
                    SearchGroupField.DateModified,
                "Size" =>
                    SearchGroupField.Size,
                "Category" =>
                    SearchGroupField.Category,
                "Extension" =>
                    SearchGroupField.Extension,
                _ =>
                    SearchGroupField.None
            };

        UpdateOrganizeSortAndGroupSelectorText();
        UpdateOrganizeSortAndGroupOptionHighlights();

        OrganizeGroupFlyout.Hide();
        RefreshPreview();
    }

    private void UpdatePendingOrganizeFilterLabels()
    {
        OrganizeDateFilterValueText.Text =
            GetOrganizeDateFilterDisplayName(
                _pendingOrganizeDateFilter,
                _pendingOrganizeSpecificDateFilter);

        OrganizeSizeFilterValueText.Text =
            GetOrganizeSizeFilterDisplayName(
                _pendingOrganizeSizeFilter);

        OrganizeExtensionFilterValueText.Text =
            GetOrganizeExtensionFilterDisplayName(
                _pendingOrganizeExtensionFilters);
    }

    private void BuildOrganizeExtensionFilterOptions()
    {
        OrganizeExtensionFilterOptionsPanel.Children.Clear();

        OrganizeExtensionFilterOptionsPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "EXTENSIÓN",
                Margin =
                    new Thickness(
                        10,
                        6,
                        10,
                        4),
                Foreground =
                    GetBrush(
                        "BandaMutedStrongBrush"),
                FontSize =
                    11,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold
            });

        var options =
            new List<string>
            {
                "All"
            };

        options.AddRange(
            _files
                .SelectMany(file =>
                    file.IsDirectory
                        ? file.FolderContents
                            .Where(item =>
                                !item.IsDirectory)
                            .Select(item =>
                                item.Extension.ToLowerInvariant())
                        : new[]
                        {
                            file.Extension.ToLowerInvariant()
                        })
                .Where(extension =>
                    !string.IsNullOrWhiteSpace(
                        extension))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    extension =>
                        extension,
                    StringComparer.OrdinalIgnoreCase));

        foreach (var option in options)
        {
            var isSelected =
                option.Equals(
                    "All",
                    StringComparison.OrdinalIgnoreCase)
                    ? _pendingOrganizeExtensionFilters.Count == 0
                    : _pendingOrganizeExtensionFilters.Contains(
                        option);

            var button =
                new Button
                {
                    Tag =
                        option,
                    Content =
                        option.Equals(
                            "All",
                            StringComparison.OrdinalIgnoreCase)
                            ? "Todas las extensiones"
                            : option,
                    Style =
                        (Style)Application.Current.Resources[
                            "BandaPopupOptionButtonStyle"]
                };

            if (isSelected)
            {
                button.Background =
                    GetBrush(
                        "BandaAccentSoftBrush");

                button.Foreground =
                    GetBrush(
                        "BandaAccentBrush");
            }

            button.Click +=
                OrganizeExtensionFilterOptionButton_Click;

            OrganizeExtensionFilterOptionsPanel.Children.Add(
                button);
        }
    }

    private static string GetOrganizeDateFilterDisplayName(
        SearchDateFilter filter,
        DateTime? specificDate = null) =>
        filter switch
        {
            SearchDateFilter.Today =>
                "Hoy",
            SearchDateFilter.Last7Days =>
                "Últimos 7 días",
            SearchDateFilter.Last30Days =>
                "Últimos 30 días",
            SearchDateFilter.SpecificDate
                when specificDate.HasValue =>
                    specificDate.Value.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.GetCultureInfo(
                            "es-AR")),
            SearchDateFilter.SpecificDate =>
                "Fecha específica",
            _ =>
                "Cualquier fecha"
        };

    private static string GetOrganizeSizeFilterDisplayName(
        SearchSizeFilter filter) =>
        filter switch
        {
            SearchSizeFilter.Under100Mb =>
                "Menos de 100 MB",
            SearchSizeFilter.From100To500Mb =>
                "100 MB a 500 MB",
            SearchSizeFilter.From500MbTo1Gb =>
                "500 MB a 1 GB",
            SearchSizeFilter.From1To5Gb =>
                "1 GB a 5 GB",
            SearchSizeFilter.From5To20Gb =>
                "5 GB a 20 GB",
            SearchSizeFilter.Over20Gb =>
                "Más de 20 GB",
            _ =>
                "Cualquier tamaño"
        };

    private static string GetOrganizeExtensionFilterDisplayName(
        IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0)
        {
            return "Todas las extensiones";
        }

        var ordered =
            extensions
                .OrderBy(
                    extension =>
                        extension,
                    StringComparer.OrdinalIgnoreCase)
                .Select(extension =>
                    extension.ToLowerInvariant())
                .ToList();

        return ordered.Count <= 2
            ? string.Join(
                " · ",
                ordered)
            : $"{ordered.Count} extensiones seleccionadas";
    }

    private void UpdateOrganizeSortAndGroupSelectorText()
    {
        OrganizeSortValueText.Text =
            $"{GetOrganizeSortFieldDisplayName(_organizeSortField)} · {GetOrganizeDirectionShortDisplayName(_organizeSortDirection)}";

        OrganizeGroupValueText.Text =
            _organizeGroupField ==
                SearchGroupField.None
                ? "Ninguno"
                : GetOrganizeGroupFieldDisplayName(
                    _organizeGroupField);
    }

    private void UpdateOrganizeSortAndGroupOptionHighlights()
    {
        SetOrganizePopupOptionSelected(
            OrganizeSortNameOptionButton,
            _organizeSortField ==
                SearchSortField.Name);

        SetOrganizePopupOptionSelected(
            OrganizeSortDateOptionButton,
            _organizeSortField ==
                SearchSortField.DateModified);

        SetOrganizePopupOptionSelected(
            OrganizeSortSizeOptionButton,
            _organizeSortField ==
                SearchSortField.Size);

        SetOrganizePopupOptionSelected(
            OrganizeSortCategoryOptionButton,
            _organizeSortField ==
                SearchSortField.Category);

        SetOrganizePopupOptionSelected(
            OrganizeSortExtensionOptionButton,
            _organizeSortField ==
                SearchSortField.Extension);

        SetOrganizePopupOptionSelected(
            OrganizeSortAscendingOptionButton,
            _organizeSortDirection ==
                SearchSortDirection.Ascending);

        SetOrganizePopupOptionSelected(
            OrganizeSortDescendingOptionButton,
            _organizeSortDirection ==
                SearchSortDirection.Descending);

        SetOrganizePopupOptionSelected(
            OrganizeGroupNoneOptionButton,
            _organizeGroupField ==
                SearchGroupField.None);

        SetOrganizePopupOptionSelected(
            OrganizeGroupNameOptionButton,
            _organizeGroupField ==
                SearchGroupField.Name);

        SetOrganizePopupOptionSelected(
            OrganizeGroupDateOptionButton,
            _organizeGroupField ==
                SearchGroupField.DateModified);

        SetOrganizePopupOptionSelected(
            OrganizeGroupSizeOptionButton,
            _organizeGroupField ==
                SearchGroupField.Size);

        SetOrganizePopupOptionSelected(
            OrganizeGroupCategoryOptionButton,
            _organizeGroupField ==
                SearchGroupField.Category);

        SetOrganizePopupOptionSelected(
            OrganizeGroupExtensionOptionButton,
            _organizeGroupField ==
                SearchGroupField.Extension);
    }

    private void SetOrganizePopupOptionSelected(
        Button button,
        bool isSelected)
    {
        button.Background =
            isSelected
                ? GetBrush(
                    "BandaAccentSoftBrush")
                : new SolidColorBrush(
                    Microsoft.UI.Colors.Transparent);

        button.Foreground =
            GetBrush(
                isSelected
                    ? "BandaAccentBrush"
                    : "BandaTextBrush");
    }

    private static string GetOrganizeSortFieldDisplayName(
        SearchSortField field) =>
        field switch
        {
            SearchSortField.Name =>
                "Nombre",
            SearchSortField.Size =>
                "Tamaño",
            SearchSortField.Category =>
                "Categoría",
            SearchSortField.Extension =>
                "Extensión",
            _ =>
                "Fecha de modificación"
        };

    private static string GetOrganizeGroupFieldDisplayName(
        SearchGroupField field) =>
        field switch
        {
            SearchGroupField.Name =>
                "Nombre",
            SearchGroupField.DateModified =>
                "Fecha de modificación",
            SearchGroupField.Size =>
                "Tamaño",
            SearchGroupField.Category =>
                "Categoría",
            SearchGroupField.Extension =>
                "Extensión",
            _ =>
                "Ninguno"
        };

    private static string GetOrganizeDirectionShortDisplayName(
        SearchSortDirection direction) =>
        direction ==
            SearchSortDirection.Ascending
            ? "Asc."
            : "Desc.";

    private void UpdateOrganizeFiltersButtonVisual()
    {
        var activeFilterCount =
            (_organizeDateFilter != SearchDateFilter.All
                ? 1
                : 0) +
            (_organizeSizeFilter != SearchSizeFilter.All
                ? 1
                : 0) +
            (_organizeExtensionFilters.Count > 0
                ? 1
                : 0);

        OrganizeFiltersButton.Content =
            activeFilterCount > 0
                ? $"Filtros ({activeFilterCount})"
                : "Filtros";

        OrganizeFiltersButton.Background =
            GetBrush(
                activeFilterCount > 0
                    ? "BandaAccentSoftBrush"
                    : "BandaCardBrush");

        OrganizeFiltersButton.Foreground =
            GetBrush(
                activeFilterCount > 0
                    ? "BandaAccentBrush"
                    : "BandaTextBrush");
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
        _activeAssignmentIsFolder = false;
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
        AssignmentRememberCheckBox.Visibility = Visibility.Visible;
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

        if (_activeAssignmentFiles.Count == 0 ||
            (!_activeAssignmentIsFolder &&
             string.IsNullOrWhiteSpace(_activeAssignmentExtension)))
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
                _activeAssignmentIsFolder
                    ? OrganizeAssignmentSource.IndividualOverride
                    : OrganizeAssignmentSource.ExtensionRule);
        }

        var rememberAssignment =
            !_activeAssignmentIsFolder &&
            AssignmentRememberCheckBox.IsChecked == true;

        try
        {
            if (rememberAssignment)
            {
                var extension =
                    _activeAssignmentExtension!;

                await PersistRememberedAssignmentAsync(
                    extension,
                    selectedCategory);

                _rememberedAssignments[extension] =
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

        if (!_activeAssignmentIsFolder)
        {
            var extension =
                _activeAssignmentExtension!;

            _resolvedAssignments.RemoveAll(item =>
                item.Extension.Equals(
                    extension,
                    StringComparison.OrdinalIgnoreCase));

            _resolvedAssignments.Add(new ResolvedExtensionAssignment(
                extension,
                _activeAssignmentFiles.Count,
                selectedCategory,
                rememberAssignment,
                rememberAssignment
                    ? "Asignación recordada para próximas organizaciones"
                    : "Asignación aplicada solo a esta organización"));
        }

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
        _activeAssignmentIsFolder = false;
        _pendingAssignmentCategory = null;
        _isCreatingAssignmentCategory = false;
        AssignmentRememberCheckBox.IsChecked = false;
        AssignmentRememberCheckBox.Visibility = Visibility.Visible;
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

    private void PreviewFilesList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isRefreshingPreview)
        {
            return;
        }

        var files =
            GetSelectedOrganizeFiles();

        SyncUnassignedFolderSelection(
            files);
        SyncUnassignedExtensionSelection(
            files);

        if (files.Count == 0)
        {
            ShowOrganizeSummary();
            return;
        }

        if (files.Count == 1)
        {
            ShowOrganizeDetail(
                files[0]);
            return;
        }

        ShowMultipleOrganizeDetails(
            files);
    }

    private void PreviewFilesList_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        if (IsTapInsideListViewItem(
                e.OriginalSource,
                PreviewFilesList))
        {
            return;
        }

        PreviewFilesList.SelectedItems.Clear();

        SyncUnassignedFolderSelection(
            Array.Empty<OrganizePreviewFile>());
        SyncUnassignedExtensionSelection(
            Array.Empty<OrganizePreviewFile>());

        ShowOrganizeSummary();

        e.Handled =
            true;
    }

    private void PreviewSelectAllAccelerator_Invoked(
        KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var file in VisibleOrganizePreviewFiles)
        {
            if (!PreviewFilesList.SelectedItems.Contains(
                    file))
            {
                PreviewFilesList.SelectedItems.Add(
                    file);
            }
        }

        var selected =
            GetSelectedOrganizeFiles();

        if (selected.Count == 1)
        {
            ShowOrganizeDetail(
                selected[0]);
        }
        else if (selected.Count > 1)
        {
            ShowMultipleOrganizeDetails(
                selected);
        }

        args.Handled =
            true;
    }

    private List<OrganizePreviewFile> GetSelectedOrganizeFiles() =>
        PreviewFilesList.SelectedItems
            .OfType<OrganizePreviewFile>()
            .ToList();

    private void UnassignedExtensionsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncingUnassignedExtensionSelection ||
            _isRefreshingPreview ||
            UnassignedExtensionsList.SelectedItem is not UnassignedExtensionSummary summary)
        {
            return;
        }

        var affectedFiles =
            _files
                .Where(file =>
                    !file.IsDirectory &&
                    !file.IsClassified &&
                    file.Extension.Equals(
                        summary.Extension,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (affectedFiles.Count == 0)
        {
            return;
        }

        try
        {
            _syncingUnassignedExtensionSelection =
                true;

            PreviewFilesList.SelectedItems.Clear();

            foreach (var file in affectedFiles.Where(
                         VisibleOrganizePreviewFiles.Contains))
            {
                PreviewFilesList.SelectedItems.Add(
                    file);
            }
        }
        finally
        {
            _syncingUnassignedExtensionSelection =
                false;
        }

        SyncUnassignedFolderSelection(
            affectedFiles);

        if (affectedFiles.Count == 1)
        {
            ShowOrganizeDetail(
                affectedFiles[0]);
        }
        else
        {
            ShowMultipleOrganizeDetails(
                affectedFiles);
        }
    }

    private void UnassignedExtensionsList_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        if (IsTapInsideListViewItem(
                e.OriginalSource,
                UnassignedExtensionsList))
        {
            return;
        }

        try
        {
            _syncingUnassignedExtensionSelection =
                true;

            UnassignedExtensionsList.SelectedItem =
                null;
            PreviewFilesList.SelectedItems.Clear();
        }
        finally
        {
            _syncingUnassignedExtensionSelection =
                false;
        }

        SyncUnassignedFolderSelection(
            Array.Empty<OrganizePreviewFile>());

        ShowOrganizeSummary();

        e.Handled =
            true;
    }

    private void SyncUnassignedExtensionSelection(
        IReadOnlyList<OrganizePreviewFile> selectedFiles)
    {
        if (_syncingUnassignedExtensionSelection)
        {
            return;
        }

        UnassignedExtensionSummary? selectedSummary =
            null;

        if (selectedFiles.Count > 0 &&
            selectedFiles.All(file =>
                !file.IsDirectory &&
                !file.IsClassified))
        {
            var extension =
                selectedFiles[0].Extension;

            var sameExtension =
                selectedFiles.All(file =>
                    file.Extension.Equals(
                        extension,
                        StringComparison.OrdinalIgnoreCase));

            if (sameExtension)
            {
                var pendingFiles =
                    _files
                        .Where(file =>
                            !file.IsDirectory &&
                            !file.IsClassified &&
                            file.Extension.Equals(
                                extension,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var selectedIds =
                    selectedFiles
                        .Select(file =>
                            file.ItemId)
                        .ToHashSet(
                            StringComparer.Ordinal);

                if (pendingFiles.Count == selectedFiles.Count &&
                    pendingFiles.All(file =>
                        selectedIds.Contains(
                            file.ItemId)))
                {
                    selectedSummary =
                        UnassignedExtensionsList.Items
                            .OfType<UnassignedExtensionSummary>()
                            .FirstOrDefault(item =>
                                item.Extension.Equals(
                                    extension,
                                    StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        try
        {
            _syncingUnassignedExtensionSelection =
                true;

            UnassignedExtensionsList.SelectedItem =
                selectedSummary;
        }
        finally
        {
            _syncingUnassignedExtensionSelection =
                false;
        }
    }

    private void UnassignedFoldersList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncingUnassignedFolderSelection ||
            _isRefreshingPreview ||
            UnassignedFoldersList.SelectedItem is not OrganizePreviewFile folder ||
            !folder.IsDirectory)
        {
            return;
        }

        try
        {
            _syncingUnassignedFolderSelection =
                true;

            PreviewFilesList.SelectedItems.Clear();

            if (VisibleOrganizePreviewFiles.Contains(
                    folder))
            {
                PreviewFilesList.SelectedItem =
                    folder;
            }
        }
        finally
        {
            _syncingUnassignedFolderSelection =
                false;
        }

        ShowOrganizeDetail(
            folder);
    }

    private void UnassignedFoldersList_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        if (IsTapInsideListViewItem(
                e.OriginalSource,
                UnassignedFoldersList))
        {
            return;
        }

        try
        {
            _syncingUnassignedFolderSelection =
                true;

            UnassignedFoldersList.SelectedItem =
                null;
            PreviewFilesList.SelectedItems.Clear();
        }
        finally
        {
            _syncingUnassignedFolderSelection =
                false;
        }

        ShowOrganizeSummary();

        e.Handled =
            true;
    }

    private static bool IsTapInsideListViewItem(
        object? originalSource,
        ListView owner)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current is not null &&
               !ReferenceEquals(
                   current,
                   owner))
        {
            if (current is ListViewItem)
            {
                return true;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return false;
    }

    private void SyncUnassignedFolderSelection(
        IReadOnlyList<OrganizePreviewFile> selectedFiles)
    {
        if (_syncingUnassignedFolderSelection)
        {
            return;
        }

        var selectedFolder =
            selectedFiles.Count == 1 &&
            selectedFiles[0].IsDirectory &&
            !selectedFiles[0].IsClassified
                ? selectedFiles[0]
                : null;

        try
        {
            _syncingUnassignedFolderSelection =
                true;

            UnassignedFoldersList.SelectedItem =
                selectedFolder;
        }
        finally
        {
            _syncingUnassignedFolderSelection =
                false;
        }
    }

    private void ClearOrganizeDetailButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        PreviewFilesList.SelectedItem =
            null;

        ShowOrganizeSummary();
    }

    private void ShowOrganizeSummary()
    {
        OrganizeDetailPanel.Visibility =
            Visibility.Collapsed;
        OrganizeSummaryPanel.Visibility =
            Visibility.Visible;

        ResetFolderDetailNavigation();
    }

    private void ShowOrganizeDetail(
        OrganizePreviewFile item,
        bool preserveFolderContext = false)
    {
        OrganizeSummaryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeDetailPanel.Visibility =
            Visibility.Visible;

        OrganizeDetailTitleText.Text =
            item.IsDirectory
                ? "Detalle de la carpeta"
                : "Detalle del archivo";

        OrganizeDetailFileNameText.Text =
            item.FileName;

        OrganizeDetailCategoryText.Text =
            item.IsClassified
                ? item.CategoryName ?? "—"
                : "Sin asignar";

        ApplyOrganizeDetailCategoryVisual(
            item);

        OrganizeDetailSizeText.Text =
            item.SizeDisplay;

        OrganizeDetailTypeText.Text =
            item.IsDirectory
                ? item.ContainedFileCount == 1
                    ? "CARPETA · 1 archivo"
                    : $"CARPETA · {item.ContainedFileCount} archivos"
                : item.ExtensionDisplay;

        OrganizeDetailModifiedText.Text =
            item.ModifiedDisplay;

        OrganizeDetailLocationText.Text =
            item.FullPath;

        OrganizeDetailActionStatusText.Text =
            string.Empty;
        OrganizeDetailActionStatusText.Visibility =
            Visibility.Collapsed;

        OrganizeOpenButton.Content =
            item.IsDirectory
                ? "Abrir contenido"
                : "Abrir archivo";
        OrganizeCopyPathButton.Content =
            "Copiar ruta";
        OrganizeChangeCategoryButton.Content =
            "Cambiar categoría";

        OrganizeDeleteButton.Content =
            item.IsDirectory
                ? "Eliminar carpeta"
                : "Eliminar archivo";

        OrganizeOpenButton.IsEnabled =
            true;
        OrganizeOpenLocationButton.IsEnabled =
            true;
        OrganizeCopyPathButton.IsEnabled =
            true;
        OrganizeChangeCategoryButton.IsEnabled =
            !item.IsDirectory ||
            item.CanAssignFolder;
        OrganizeRenameButton.IsEnabled =
            true;
        OrganizeDeleteButton.IsEnabled =
            true;

        if (!item.IsDirectory)
        {
            OrganizeFolderContentsPanel.Visibility =
                Visibility.Collapsed;

            ResetFolderDetailNavigation();
            return;
        }

        if (!preserveFolderContext ||
            _folderDetailRoot is null ||
            !_folderDetailRoot.FullPath.Equals(
                item.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            _folderDetailRoot =
                item;
            _folderDetailCurrentRelativePath =
                string.Empty;
            _folderDetailHistory.Clear();
        }

        OrganizeFolderContentsPanel.Visibility =
            Visibility.Visible;

        RefreshFolderDetailView();
    }

    private void ShowMultipleOrganizeDetails(
        IReadOnlyList<OrganizePreviewFile> files)
    {
        ResetFolderDetailNavigation();
        OrganizeFolderContentsPanel.Visibility =
            Visibility.Collapsed;

        OrganizeSummaryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeDetailPanel.Visibility =
            Visibility.Visible;

        var categories =
            files
                .Select(file =>
                    file.CategoryDisplay)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var types =
            files
                .Select(file =>
                    file.IsDirectory
                        ? "CARPETA"
                        : file.ExtensionDisplay)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var locations =
            files
                .Select(file =>
                    Path.GetDirectoryName(
                        file.FullPath) ??
                    file.FullPath)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        OrganizeDetailTitleText.Text =
            "Selección múltiple";
        OrganizeDetailFileNameText.Text =
            $"{files.Count} elementos seleccionados";
        OrganizeDetailCategoryText.Text =
            categories.Count == 1
                ? categories[0]
                : $"{categories.Count} categorías";

        ApplyOrganizeDetailCategoryVisual(
            categories.Count == 1 &&
            files.All(file =>
                file.IsClassified)
                ? files[0]
                : null);

        OrganizeDetailSizeText.Text =
            FormatBytes(
                files.Sum(file =>
                    file.SizeBytes));

        OrganizeDetailTypeText.Text =
            types.Count == 1
                ? types[0]
                : $"{types.Count} tipos";
        OrganizeDetailModifiedText.Text =
            "Varias fechas";
        OrganizeDetailLocationText.Text =
            locations.Count == 1
                ? locations[0]
                : $"{locations.Count} ubicaciones";

        OrganizeDetailActionStatusText.Text =
            "Abrir elemento, abrir ubicación y renombrar requieren una selección individual.";
        OrganizeDetailActionStatusText.Visibility =
            Visibility.Visible;

        OrganizeOpenButton.Content =
            "Abrir elemento";
        OrganizeCopyPathButton.Content =
            "Copiar rutas";
        OrganizeChangeCategoryButton.Content =
            "Cambiar categoría";
        OrganizeDeleteButton.Content =
            $"Eliminar {files.Count} elementos";

        OrganizeOpenButton.IsEnabled =
            false;
        OrganizeOpenLocationButton.IsEnabled =
            false;
        OrganizeCopyPathButton.IsEnabled =
            true;
        OrganizeChangeCategoryButton.IsEnabled =
            files.All(file =>
                !file.IsDirectory ||
                file.CanAssignFolder);
        OrganizeRenameButton.IsEnabled =
            false;
        OrganizeDeleteButton.IsEnabled =
            true;
    }

    private void ApplyOrganizeDetailCategoryVisual(
        OrganizePreviewFile? item)
    {
        ApplyOrganizeDetailCategoryVisual(
            item?.SelectedCategory);
    }

    private void ApplyOrganizeDetailCategoryVisual(
        string? categoryName)
    {
        var category =
            string.IsNullOrWhiteSpace(
                categoryName)
                ? null
                : _categories.FirstOrDefault(item =>
                    item.Name.Equals(
                        categoryName,
                        StringComparison.CurrentCultureIgnoreCase));

        ApplyOrganizeDetailCategoryVisual(
            category);
    }

    private void ApplyOrganizeDetailCategoryVisual(
        OrganizeCategoryOption? category)
    {
        if (category is null)
        {
            OrganizeDetailCategoryCard.Background =
                (Brush)Application.Current.Resources[
                    "BandaNavIconBrush"];
            OrganizeDetailCategoryCard.BorderBrush =
                (Brush)Application.Current.Resources[
                    "BandaBorderBrush"];
            OrganizeDetailCategoryDot.Fill =
                (Brush)Application.Current.Resources[
                    "BandaMutedBrush"];
            OrganizeDetailCategoryText.Foreground =
                (Brush)Application.Current.Resources[
                    "BandaMutedStrongBrush"];
            return;
        }

        var categoryBrush =
            CreateOrganizeCategoryBrush(
                category.ColorHex,
                alpha: 0xFF);

        OrganizeDetailCategoryCard.Background =
            CreateOrganizeCategoryBrush(
                category.ColorHex,
                alpha: 0x22);
        OrganizeDetailCategoryCard.BorderBrush =
            categoryBrush;
        OrganizeDetailCategoryDot.Fill =
            categoryBrush;
        OrganizeDetailCategoryText.Foreground =
            categoryBrush;
    }

    private static Brush CreateOrganizeCategoryBrush(
        string colorHex,
        byte alpha)
    {
        if (!CategoryColorPalette.TryNormalizeHex(
                colorHex,
                out var normalized))
        {
            return (Brush)Application.Current.Resources[
                alpha == 0xFF
                    ? "BandaAccentBrush"
                    : "BandaAccentSoftBrush"];
        }

        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(
                alpha,
                Convert.ToByte(
                    normalized.Substring(1, 2),
                    16),
                Convert.ToByte(
                    normalized.Substring(3, 2),
                    16),
                Convert.ToByte(
                    normalized.Substring(5, 2),
                    16)));
    }

    private void FolderDetailFilesList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncingFolderDetailSelection ||
            _folderDetailRoot is null)
        {
            return;
        }

        var selectedItems =
            GetSelectedFolderContentItems();

        FolderDetailSelectionText.Text =
            selectedItems.Count switch
            {
                0 =>
                    "Seleccioná para gestionar",
                1 =>
                    "1 seleccionado",
                _ =>
                    $"{selectedItems.Count} seleccionados"
            };

        if (selectedItems.Count == 0)
        {
            ShowOrganizeDetail(
                _folderDetailRoot,
                preserveFolderContext: true);
            return;
        }

        if (selectedItems.Count == 1)
        {
            ShowFolderContentItemDetails(
                selectedItems[0]);
            return;
        }

        ShowMultipleFolderContentDetails(
            selectedItems);
    }

    private void FolderDetailFilesList_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        var current =
            e.OriginalSource as DependencyObject;

        while (current is not null &&
               current != FolderDetailFilesList)
        {
            if (current is ListViewItem)
            {
                return;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        FolderDetailFilesList.SelectedItems.Clear();
    }

    private void FolderDetailFilesList_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        var item =
            GetFolderContentItemFromEventSource(
                e.OriginalSource);

        if (item is null ||
            _folderDetailRoot is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            _folderDetailHistory.Push(
                _folderDetailCurrentRelativePath);

            _folderDetailCurrentRelativePath =
                item.RelativePath;

            RefreshFolderDetailView();

            ShowOrganizeDetail(
                _folderDetailRoot,
                preserveFolderContext: true);

            e.Handled =
                true;
            return;
        }

        var fullPath =
            GetFolderContentFullPath(
                item);

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        fullPath,
                    UseShellExecute =
                        true
                });

            ShowOrganizeActionStatus(
                "Archivo abierto.");
        }
        catch
        {
            ShowOrganizeActionStatus(
                "No se pudo abrir el archivo.",
                isError: true);
        }

        e.Handled =
            true;
    }

    private void FolderDetailSelectAllAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var item in VisibleOrganizeFolderItems)
        {
            if (!FolderDetailFilesList.SelectedItems.Contains(
                    item))
            {
                FolderDetailFilesList.SelectedItems.Add(
                    item);
            }
        }

        args.Handled =
            true;
    }

    private List<FolderContentPreviewItem> GetSelectedFolderContentItems() =>
        FolderDetailFilesList.SelectedItems
            .OfType<FolderContentPreviewItem>()
            .ToList();

    private FolderContentPreviewItem? GetFolderContentItemFromEventSource(
        object? source)
    {
        var current =
            source as DependencyObject;

        while (current is not null &&
               current != FolderDetailFilesList)
        {
            if (current is FrameworkElement
                {
                    DataContext:
                        FolderContentPreviewItem item
                })
            {
                return item;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return null;
    }

    private void ShowFolderContentItemDetails(
        FolderContentPreviewItem item)
    {
        if (_folderDetailRoot is null)
        {
            return;
        }

        OrganizeSummaryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeDetailPanel.Visibility =
            Visibility.Visible;
        OrganizeFolderContentsPanel.Visibility =
            Visibility.Visible;

        OrganizeDetailTitleText.Text =
            item.IsDirectory
                ? "Detalle de la carpeta"
                : "Detalle del archivo";

        OrganizeDetailFileNameText.Text =
            item.FileName;

        OrganizeDetailCategoryText.Text =
            item.CategoryDisplay;

        ApplyOrganizeDetailCategoryVisual(
            item.CategoryName);

        OrganizeDetailSizeText.Text =
            item.SizeDisplay;

        OrganizeDetailTypeText.Text =
            item.IsDirectory
                ? item.ContainedFileCount == 1
                    ? "CARPETA · 1 archivo"
                    : $"CARPETA · {item.ContainedFileCount} archivos"
                : item.ExtensionDisplay;

        OrganizeDetailModifiedText.Text =
            item.ModifiedDisplay;

        OrganizeDetailLocationText.Text =
            GetFolderContentFullPath(
                item);

        OrganizeDetailActionStatusText.Text =
            string.Empty;
        OrganizeDetailActionStatusText.Visibility =
            Visibility.Collapsed;

        OrganizeOpenButton.Content =
            item.IsDirectory
                ? "Abrir contenido"
                : "Abrir archivo";
        OrganizeCopyPathButton.Content =
            "Copiar ruta";
        OrganizeChangeCategoryButton.Content =
            "Cambiar categoría";

        OrganizeDeleteButton.Content =
            item.IsDirectory
                ? "Eliminar carpeta"
                : "Eliminar archivo";

        OrganizeOpenButton.IsEnabled =
            true;
        OrganizeOpenLocationButton.IsEnabled =
            true;
        OrganizeCopyPathButton.IsEnabled =
            true;
        OrganizeChangeCategoryButton.IsEnabled =
            _folderDetailRoot.CanAssignFolder;
        OrganizeRenameButton.IsEnabled =
            true;
        OrganizeDeleteButton.IsEnabled =
            true;
    }

    private void ShowMultipleFolderContentDetails(
        IReadOnlyList<FolderContentPreviewItem> items)
    {
        if (_folderDetailRoot is null)
        {
            return;
        }

        OrganizeSummaryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeDetailPanel.Visibility =
            Visibility.Visible;
        OrganizeFolderContentsPanel.Visibility =
            Visibility.Visible;

        var categories =
            items
                .Select(item =>
                    item.CategoryDisplay)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var types =
            items
                .Select(item =>
                    item.IsDirectory
                        ? "CARPETA"
                        : item.ExtensionDisplay)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var locations =
            items
                .Select(item =>
                    Path.GetDirectoryName(
                        GetFolderContentFullPath(
                            item)) ??
                    GetFolderContentFullPath(
                        item))
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        OrganizeDetailTitleText.Text =
            "Selección múltiple";
        OrganizeDetailFileNameText.Text =
            $"{items.Count} elementos seleccionados";

        OrganizeDetailCategoryText.Text =
            categories.Count == 1
                ? categories[0]
                : $"{categories.Count} categorías";

        ApplyOrganizeDetailCategoryVisual(
            categories.Count == 1
                ? categories[0]
                : null);

        OrganizeDetailSizeText.Text =
            FormatBytes(
                items.Sum(item =>
                    item.SizeBytes));

        OrganizeDetailTypeText.Text =
            types.Count == 1
                ? types[0]
                : $"{types.Count} tipos";

        OrganizeDetailModifiedText.Text =
            "Varias fechas";

        OrganizeDetailLocationText.Text =
            locations.Count == 1
                ? locations[0]
                : $"{locations.Count} ubicaciones";

        OrganizeDetailActionStatusText.Text =
            "Abrir elemento, abrir ubicación y renombrar requieren una selección individual.";
        OrganizeDetailActionStatusText.Visibility =
            Visibility.Visible;

        OrganizeOpenButton.Content =
            "Abrir elemento";
        OrganizeCopyPathButton.Content =
            "Copiar rutas";
        OrganizeChangeCategoryButton.Content =
            "Cambiar categoría";
        OrganizeDeleteButton.Content =
            $"Eliminar {items.Count} elementos";

        OrganizeOpenButton.IsEnabled =
            false;
        OrganizeOpenLocationButton.IsEnabled =
            false;
        OrganizeCopyPathButton.IsEnabled =
            true;
        OrganizeChangeCategoryButton.IsEnabled =
            _folderDetailRoot.CanAssignFolder;
        OrganizeRenameButton.IsEnabled =
            false;
        OrganizeDeleteButton.IsEnabled =
            true;
    }

    private void FolderDetailBackButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_folderDetailHistory.Count == 0 ||
            _folderDetailRoot is null)
        {
            return;
        }

        _folderDetailCurrentRelativePath =
            _folderDetailHistory.Pop();

        RefreshFolderDetailView();

        ShowOrganizeDetail(
            _folderDetailRoot,
            preserveFolderContext: true);
    }

    private void RefreshFolderDetailView()
    {
        if (_folderDetailRoot is null)
        {
            return;
        }

        var currentPath =
            _folderDetailCurrentRelativePath;

        FolderDetailPathText.Text =
            string.IsNullOrWhiteSpace(
                currentPath)
                ? "· Raíz"
                : $"· {currentPath}";

        FolderDetailBackButton.Visibility =
            _folderDetailHistory.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        var categoryOrder =
            GetOrganizeCategoryOrder();

        var visibleItems =
            SortOrganizeFolderItems(
                ApplyOrganizeFilters(
                    _folderDetailRoot.FolderContents
                        .Where(item =>
                            GetParentRelativePath(
                                item.RelativePath)
                                .Equals(
                                    currentPath,
                                    StringComparison.OrdinalIgnoreCase))),
                categoryOrder);

        var visibleFolderCount =
            visibleItems.Count(item =>
                item.IsDirectory);

        var visibleFileCount =
            visibleItems.Count -
            visibleFolderCount;

        OrganizeFolderContentsTitleText.Text =
            visibleFolderCount == 0
                ? visibleFileCount == 1
                    ? "CONTENIDO · 1 ARCHIVO"
                    : $"CONTENIDO · {visibleFileCount} ARCHIVOS"
                : visibleItems.Count == 1
                    ? "CONTENIDO · 1 ELEMENTO"
                    : $"CONTENIDO · {visibleItems.Count} ELEMENTOS";

        _syncingFolderDetailSelection =
            true;

        try
        {
            FolderDetailFilesList.SelectedItems.Clear();

            ApplyOrganizeFolderView(
                visibleItems,
                categoryOrder);
        }
        finally
        {
            _syncingFolderDetailSelection =
                false;
        }

        FolderDetailSelectionText.Text =
            "Seleccioná para gestionar";
    }

    private void ResetFolderDetailNavigation()
    {
        _syncingFolderDetailSelection =
            true;

        try
        {
            FolderDetailFilesList.SelectedItems.Clear();
            VisibleOrganizeFolderItems.Clear();
            GroupedOrganizeFolderItems.Clear();
            _organizeFolderViewSource.Source =
                null;
            FolderDetailFilesList.ItemsSource =
                null;
        }
        finally
        {
            _syncingFolderDetailSelection =
                false;
        }

        _folderDetailRoot =
            null;
        _folderDetailCurrentRelativePath =
            string.Empty;
        _folderDetailHistory.Clear();

        OrganizeFolderContentsTitleText.Text =
            "CONTENIDO";
        FolderDetailPathText.Text =
            string.Empty;
        FolderDetailSelectionText.Text =
            "Seleccioná para gestionar";
        FolderDetailBackButton.Visibility =
            Visibility.Collapsed;
    }

    private string GetFolderContentFullPath(
        FolderContentPreviewItem item)
    {
        if (_folderDetailRoot is null)
        {
            return item.RelativePath;
        }

        return Path.GetFullPath(
            Path.Combine(
                _folderDetailRoot.FullPath,
                item.RelativePath));
    }

    private List<OrganizeActionTarget> GetActiveOrganizeActionTargets()
    {
        var nestedItems =
            GetSelectedFolderContentItems();

        if (nestedItems.Count > 0 &&
            _folderDetailRoot is not null)
        {
            return nestedItems
                .Select(item =>
                    new OrganizeActionTarget(
                        GetFolderContentFullPath(
                            item),
                        item.FileName,
                        item.IsDirectory,
                        item.SizeBytes,
                        item.ModifiedAt,
                        item.CategoryName,
                        item.ExtensionDisplay,
                        _folderDetailRoot,
                        IsRootItem: false))
                .ToList();
        }

        return GetSelectedOrganizeFiles()
            .Select(file =>
                new OrganizeActionTarget(
                    file.FullPath,
                    file.FileName,
                    file.IsDirectory,
                    file.SizeBytes,
                    file.ModifiedAt,
                    file.CategoryName,
                    file.ExtensionDisplay,
                    file,
                    IsRootItem: true))
            .ToList();
    }

    private List<OrganizePreviewFile> GetCategoryAssignmentFiles()
    {
        var nestedItems =
            GetSelectedFolderContentItems();

        if (nestedItems.Count > 0 &&
            _folderDetailRoot is not null)
        {
            return
            [
                _folderDetailRoot
            ];
        }

        return GetSelectedOrganizeFiles();
    }

    private static string GetParentRelativePath(
        string relativePath)
    {
        var parent =
            Path.GetDirectoryName(
                relativePath);

        return string.IsNullOrWhiteSpace(parent)
            ? string.Empty
            : parent;
    }

    private void OrganizeOpenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var targets =
            GetActiveOrganizeActionTargets();

        if (targets.Count != 1)
        {
            return;
        }

        var target =
            targets[0];

        if (!OrganizationEntrySafety.Exists(
                target.FullPath))
        {
            ShowOrganizeActionStatus(
                "El elemento ya no existe. Se volverá a analizar el origen.");

            _ =
                ReanalyzePreviewAfterSourceActionAsync(
                    target.RootPreviewFile.FullPath);
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        target.FullPath,
                    UseShellExecute =
                        true
                });

            ShowOrganizeActionStatus(
                target.IsDirectory
                    ? "Carpeta abierta."
                    : "Archivo abierto.");
        }
        catch
        {
            ShowOrganizeActionStatus(
                target.IsDirectory
                    ? "No se pudo abrir la carpeta."
                    : "No se pudo abrir el archivo.",
                isError: true);
        }
    }

    private void OrganizeOpenLocationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var targets =
            GetActiveOrganizeActionTargets();

        if (targets.Count != 1)
        {
            return;
        }

        var location =
            Path.GetDirectoryName(
                targets[0].FullPath);

        if (string.IsNullOrWhiteSpace(
                location) ||
            !Directory.Exists(
                location))
        {
            ShowOrganizeActionStatus(
                "La ubicación ya no existe.",
                isError: true);
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        location,
                    UseShellExecute =
                        true
                });

            ShowOrganizeActionStatus(
                "Ubicación abierta.");
        }
        catch
        {
            ShowOrganizeActionStatus(
                "No se pudo abrir la ubicación.",
                isError: true);
        }
    }

    private void OrganizeCopyPathButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var targets =
            GetActiveOrganizeActionTargets();

        if (targets.Count == 0)
        {
            return;
        }

        var dataPackage =
            new DataPackage();

        dataPackage.SetText(
            string.Join(
                Environment.NewLine,
                targets.Select(target =>
                    target.FullPath)));

        Clipboard.SetContent(
            dataPackage);

        ShowOrganizeActionStatus(
            targets.Count == 1
                ? "Ruta copiada al portapapeles."
                : $"{targets.Count} rutas copiadas al portapapeles.");
    }

    private void OrganizeChangeCategoryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var files =
            GetCategoryAssignmentFiles();

        if (files.Count == 0 ||
            files.Any(file =>
                file.IsDirectory &&
                !file.CanAssignFolder))
        {
            return;
        }

        _managedOrganizeTargets.Clear();
        _managedOrganizeFiles.Clear();
        _managedOrganizeFiles.AddRange(
            files);

        _organizeManageMode =
            OrganizeManageMode.ChangeCategory;

        var commonCategory =
            files
                .Select(file =>
                    file.CategoryName)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        _pendingOrganizeCategory =
            commonCategory.Count == 1 &&
            !string.IsNullOrWhiteSpace(
                commonCategory[0])
                ? _categories.FirstOrDefault(category =>
                    category.Name.Equals(
                        commonCategory[0]!,
                        StringComparison.CurrentCultureIgnoreCase))
                : null;

        var isNestedSelection =
            GetSelectedFolderContentItems().Count > 0 &&
            _folderDetailRoot is not null;

        OrganizeManageTitleText.Text =
            "Cambiar categoría";

        OrganizeManageSubtitleText.Text =
            isNestedSelection
                ? $"La selección pertenece a \"{_folderDetailRoot!.FileName}\". Se cambiará la categoría prevista de esa carpeta completa."
                : files.Count == 1
                    ? files[0].FileName
                    : $"{files.Count} elementos seleccionados";

        OrganizeManageIconText.Text =
            "↻";
        OrganizeManageIconBorder.Background =
            GetBrush(
                "BandaAccentSoftBrush");
        OrganizeManageIconText.Foreground =
            GetBrush(
                "BandaAccentBrush");

        OrganizeManageCategoryPanel.Visibility =
            Visibility.Visible;
        OrganizeManageRenamePanel.Visibility =
            Visibility.Collapsed;
        OrganizeManageDeletePanel.Visibility =
            Visibility.Collapsed;

        OrganizeManageCategoryValueText.Text =
            _pendingOrganizeCategory?.DisplayName ??
            "Elegir categoría";
        OrganizeManageCategoryValueText.Foreground =
            _pendingOrganizeCategory is null
                ? GetBrush(
                    "BandaTextBrush")
                : CreateOrganizeCategoryBrush(
                    _pendingOrganizeCategory.ColorHex,
                    0xFF);

        OrganizeManagePrimaryButton.Content =
            files.Count == 1
                ? "Cambiar categoría"
                : $"Cambiar {files.Count} elementos";
        OrganizeManagePrimaryButton.Visibility =
            Visibility.Visible;
        OrganizeManageDangerButton.Visibility =
            Visibility.Collapsed;

        OrganizeManageValidationText.Visibility =
            Visibility.Collapsed;

        BuildOrganizeManageCategoryOptions();

        OrganizeManageOverlay.Visibility =
            Visibility.Visible;
    }

    private void OrganizeRenameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var targets =
            GetActiveOrganizeActionTargets();

        if (targets.Count != 1)
        {
            return;
        }

        _managedOrganizeFiles.Clear();
        _managedOrganizeTargets.Clear();
        _managedOrganizeTargets.Add(
            targets[0]);

        _organizeManageMode =
            OrganizeManageMode.Rename;
        _pendingOrganizeCategory =
            null;

        OrganizeManageTitleText.Text =
            targets[0].IsDirectory
                ? "Renombrar carpeta"
                : "Renombrar archivo";
        OrganizeManageSubtitleText.Text =
            targets[0].FileName;
        OrganizeManageIconText.Text =
            "✎";
        OrganizeManageIconBorder.Background =
            GetBrush(
                "BandaAccentSoftBrush");
        OrganizeManageIconText.Foreground =
            GetBrush(
                "BandaAccentBrush");

        OrganizeManageCategoryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeManageRenamePanel.Visibility =
            Visibility.Visible;
        OrganizeManageDeletePanel.Visibility =
            Visibility.Collapsed;

        OrganizeManageRenameTextBox.Text =
            targets[0].FileName;

        OrganizeManagePrimaryButton.Content =
            "Guardar nombre";
        OrganizeManagePrimaryButton.Visibility =
            Visibility.Visible;
        OrganizeManageDangerButton.Visibility =
            Visibility.Collapsed;

        OrganizeManageValidationText.Visibility =
            Visibility.Collapsed;
        OrganizeManageOverlay.Visibility =
            Visibility.Visible;

        OrganizeManageRenameTextBox.SelectAll();
        OrganizeManageRenameTextBox.Focus(
            FocusState.Programmatic);
    }

    private async void OrganizeDeleteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var targets =
            GetActiveOrganizeActionTargets();

        if (targets.Count == 0)
        {
            return;
        }

        _managedOrganizeFiles.Clear();
        _managedOrganizeTargets.Clear();
        _managedOrganizeTargets.AddRange(
            targets);

        _organizeManageMode =
            OrganizeManageMode.Delete;
        _pendingOrganizeCategory =
            null;

        OrganizeManageTitleText.Text =
            targets.Count == 1
                ? targets[0].IsDirectory
                    ? "Eliminar carpeta"
                    : "Eliminar archivo"
                : "Eliminar elementos";
        OrganizeManageSubtitleText.Text =
            targets.Count == 1
                ? targets[0].FileName
                : $"{targets.Count} elementos seleccionados";

        OrganizeManageIconText.Text =
            "!";
        OrganizeManageIconBorder.Background =
            GetBrush(
                "BandaDangerSoftBrush");
        OrganizeManageIconText.Foreground =
            GetBrush(
                "BandaDangerBrush");

        OrganizeManageCategoryPanel.Visibility =
            Visibility.Collapsed;
        OrganizeManageRenamePanel.Visibility =
            Visibility.Collapsed;
        OrganizeManageDeletePanel.Visibility =
            Visibility.Visible;

        var settings =
            global::BandaNV.App.App.Settings.Current;

        OrganizeManageDeleteText.Text =
            targets.Count == 1
                ? settings.UseRecycleBin
                    ? $"¿Enviar \"{targets[0].FileName}\" a la Papelera?"
                    : $"¿Eliminar permanentemente \"{targets[0].FileName}\"?"
                : settings.UseRecycleBin
                    ? $"¿Enviar los {targets.Count} elementos seleccionados a la Papelera?"
                    : $"¿Eliminar permanentemente los {targets.Count} elementos seleccionados?";

        OrganizeManagePrimaryButton.Visibility =
            Visibility.Collapsed;
        OrganizeManageDangerButton.Content =
            settings.UseRecycleBin
                ? "Enviar a Papelera"
                : targets.Count == 1
                    ? "Eliminar"
                    : $"Eliminar {targets.Count} elementos";
        OrganizeManageDangerButton.Visibility =
            Visibility.Visible;

        OrganizeManageValidationText.Visibility =
            Visibility.Collapsed;

        if (!settings.ConfirmDestructiveActions)
        {
            await ExecuteDeleteManagedOrganizeTargetsAsync();
            return;
        }

        OrganizeManageOverlay.Visibility =
            Visibility.Visible;
    }

    private void BuildOrganizeManageCategoryOptions()
    {
        OrganizeManageCategoryOptionsPanel.Children.Clear();

        OrganizeManageCategoryOptionsPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "CATEGORÍAS",
                Margin =
                    new Thickness(
                        10,
                        6,
                        10,
                        4),
                Foreground =
                    GetBrush(
                        "BandaMutedStrongBrush"),
                FontSize =
                    11,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold
            });

        foreach (var category in _categories
                     .OrderBy(category =>
                         category.Order)
                     .ThenBy(
                         category =>
                             category.Name,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            var optionButton =
                new Button
                {
                    Content =
                        CreateCategoryOptionContent(
                            category),
                    Style =
                        (Style)Application.Current.Resources[
                            "BandaPopupOptionButtonStyle"]
                };

            if (_pendingOrganizeCategory?.Id.Equals(
                    category.Id,
                    StringComparison.OrdinalIgnoreCase) ==
                true)
            {
                optionButton.Background =
                    GetBrush(
                        "BandaAccentSoftBrush");
            }

            optionButton.Click +=
                (_, _) =>
                {
                    _pendingOrganizeCategory =
                        category;

                    OrganizeManageCategoryValueText.Text =
                        category.DisplayName;
                    OrganizeManageCategoryValueText.Foreground =
                        CreateOrganizeCategoryBrush(
                            category.ColorHex,
                            0xFF);

                    OrganizeManageCategoryFlyout.Hide();
                    BuildOrganizeManageCategoryOptions();
                };

            OrganizeManageCategoryOptionsPanel.Children.Add(
                optionButton);
        }
    }

    private async void OrganizeManagePrimaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OrganizeManageValidationText.Visibility =
            Visibility.Collapsed;

        if (_organizeManageMode ==
            OrganizeManageMode.ChangeCategory)
        {
            if (_pendingOrganizeCategory is null)
            {
                OrganizeManageValidationText.Text =
                    "Elegí una categoría.";
                OrganizeManageValidationText.Visibility =
                    Visibility.Visible;
                return;
            }

            foreach (var managedFile in _managedOrganizeFiles)
            {
                managedFile.AssignTo(
                    _pendingOrganizeCategory,
                    OrganizeAssignmentSource.IndividualOverride);
            }

            var selectedIds =
                _managedOrganizeFiles
                    .Select(file =>
                        file.ItemId)
                    .ToHashSet(
                        StringComparer.Ordinal);

            CloseOrganizeManageOverlay();

            RefreshPreview();

            _isRefreshingPreview =
                true;

            try
            {
                PreviewFilesList.SelectedItems.Clear();

                foreach (var item in PreviewFilesList.Items
                             .OfType<OrganizePreviewFile>()
                             .Where(item =>
                                 selectedIds.Contains(
                                     item.ItemId)))
                {
                    PreviewFilesList.SelectedItems.Add(
                        item);
                }
            }
            finally
            {
                _isRefreshingPreview =
                    false;
            }

            var selected =
                GetSelectedOrganizeFiles();

            if (selected.Count == 1)
            {
                ShowOrganizeDetail(
                    selected[0]);
            }
            else if (selected.Count > 1)
            {
                ShowMultipleOrganizeDetails(
                    selected);
            }

            ShowOrganizeActionStatus(
                "Categoría prevista actualizada. La organización todavía no se ejecutó.");
            return;
        }

        if (_organizeManageMode !=
            OrganizeManageMode.Rename ||
            _managedOrganizeTargets.Count != 1)
        {
            return;
        }

        var target =
            _managedOrganizeTargets[0];

        var preservedFolderPath =
            target.IsRootItem
                ? null
                : _folderDetailCurrentRelativePath;

        StopSourceWatcher();
        BeginSourceWatcherSuppression();

        try
        {
            OrganizeManagePrimaryButton.IsEnabled =
                false;

            var result =
                await global::BandaNV.App.App.OrganizationSourceActions
                    .RenameAsync(
                        global::BandaNV.App.App.Settings.Current,
                        target.FullPath,
                        OrganizeManageRenameTextBox.Text);

            var itemResult =
                result.Items.FirstOrDefault();

            if (itemResult is null ||
                itemResult.Status !=
                OrganizationSourceActionStatus.Completed)
            {
                OrganizeManageValidationText.Text =
                    itemResult?.Message ??
                    "No se pudo renombrar el elemento.";
                OrganizeManageValidationText.Visibility =
                    Visibility.Visible;
                return;
            }

            var renamedPath =
                itemResult.ResultPath;

            var preferredRootPath =
                target.IsRootItem
                    ? renamedPath
                    : target.RootPreviewFile.FullPath;

            CloseOrganizeManageOverlay();

            await ReanalyzePreviewAfterSourceActionAsync(
                preferredRootPath,
                renamedFromPath:
                    target.IsRootItem
                        ? target.FullPath
                        : null,
                preferredFolderRelativePath:
                    preservedFolderPath);

            ShowOrganizeActionStatus(
                "Elemento renombrado. El análisis se actualizó automáticamente.");
        }
        catch (Exception ex)
        {
            OrganizeManageValidationText.Text =
                ex.Message;
            OrganizeManageValidationText.Visibility =
                Visibility.Visible;
        }
        finally
        {
            OrganizeManagePrimaryButton.IsEnabled =
                true;
            EndSourceWatcherSuppression();

            if (PreviewStatePanel.Visibility ==
                Visibility.Visible)
            {
                StartSourceWatcher();
            }
        }
    }

    private async void OrganizeManageDangerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_organizeManageMode !=
                OrganizeManageMode.Delete ||
            _managedOrganizeTargets.Count == 0)
        {
            CloseOrganizeManageOverlay();
            return;
        }

        await ExecuteDeleteManagedOrganizeTargetsAsync();
    }

    private async Task ExecuteDeleteManagedOrganizeTargetsAsync()
    {
        StopSourceWatcher();
        BeginSourceWatcherSuppression();

        try
        {
            OrganizeManageDangerButton.IsEnabled =
                false;

            var preferredRootPath =
                _managedOrganizeTargets.All(target =>
                    !target.IsRootItem)
                    ? _managedOrganizeTargets[0]
                        .RootPreviewFile
                        .FullPath
                    : null;

            var preservedFolderPath =
                _managedOrganizeTargets.All(target =>
                    !target.IsRootItem)
                    ? _folderDetailCurrentRelativePath
                    : null;

            var result =
                await global::BandaNV.App.App.OrganizationSourceActions
                    .DeleteAsync(
                        global::BandaNV.App.App.Settings.Current,
                        _managedOrganizeTargets
                            .Select(target =>
                                target.FullPath)
                            .ToList());

            if (result.CompletedCount == 0)
            {
                OrganizeManageValidationText.Text =
                    result.Items
                        .Select(item =>
                            item.Message)
                        .FirstOrDefault(message =>
                            !string.IsNullOrWhiteSpace(
                                message)) ??
                    "No se pudo eliminar ningún elemento.";
                OrganizeManageValidationText.Visibility =
                    Visibility.Visible;

                if (OrganizeManageOverlay.Visibility !=
                    Visibility.Visible)
                {
                    OrganizeManageOverlay.Visibility =
                        Visibility.Visible;
                }

                return;
            }

            var completed =
                result.CompletedCount;

            var hadErrors =
                result.HasErrors;

            CloseOrganizeManageOverlay();

            await ReanalyzePreviewAfterSourceActionAsync(
                preferredSelectionPath:
                    preferredRootPath,
                preferredFolderRelativePath:
                    preservedFolderPath);

            ShowOrganizeActionStatus(
                hadErrors
                    ? $"{completed} elemento{(completed == 1 ? string.Empty : "s")} eliminado{(completed == 1 ? string.Empty : "s")}. Algunos no pudieron modificarse."
                    : completed == 1
                        ? "Elemento eliminado. El análisis se actualizó automáticamente."
                        : $"{completed} elementos eliminados. El análisis se actualizó automáticamente.",
                isError:
                    hadErrors);
        }
        catch (Exception ex)
        {
            OrganizeManageValidationText.Text =
                $"No se pudo eliminar: {ex.Message}";
            OrganizeManageValidationText.Visibility =
                Visibility.Visible;

            if (OrganizeManageOverlay.Visibility !=
                Visibility.Visible)
            {
                OrganizeManageOverlay.Visibility =
                    Visibility.Visible;
            }
        }
        finally
        {
            OrganizeManageDangerButton.IsEnabled =
                true;
            EndSourceWatcherSuppression();

            if (PreviewStatePanel.Visibility ==
                Visibility.Visible)
            {
                StartSourceWatcher();
            }
        }
    }

    private void CloseOrganizeManageOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseOrganizeManageOverlay();
    }

    private void OrganizeManageBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseOrganizeManageOverlay();
    }

    private void CloseOrganizeManageOverlay()
    {
        OrganizeManageOverlay.Visibility =
            Visibility.Collapsed;

        _managedOrganizeFiles.Clear();
        _managedOrganizeTargets.Clear();
        _pendingOrganizeCategory =
            null;
        _organizeManageMode =
            OrganizeManageMode.None;

        OrganizeManageValidationText.Visibility =
            Visibility.Collapsed;
    }

    private void ShowOrganizeActionStatus(
        string message,
        bool isError = false)
    {
        var brush =
            GetBrush(
                isError
                    ? "BandaDangerBrush"
                    : "BandaMutedStrongBrush");

        FooterStatusText.Foreground =
            brush;
        FooterStatusText.Text =
            message;

        if (OrganizeDetailPanel.Visibility ==
            Visibility.Visible)
        {
            OrganizeDetailActionStatusText.Text =
                message;
            OrganizeDetailActionStatusText.Foreground =
                brush;
            OrganizeDetailActionStatusText.Visibility =
                Visibility.Visible;
        }
    }

    private async Task HandleLiveSourceRefreshAsync()
    {
        if (IsSourceWatcherSuppressed ||
            PreviewStatePanel.Visibility !=
                Visibility.Visible ||
            _lastAnalysis is null)
        {
            return;
        }

        if (_isLiveSourceRefreshRunning)
        {
            _liveSourceRefreshPending =
                true;
            return;
        }

        _isLiveSourceRefreshRunning =
            true;

        IReadOnlyDictionary<string, string> renames;

        lock (_sourceWatcherGate)
        {
            renames =
                new Dictionary<string, string>(
                    _pendingExternalRenames,
                    StringComparer.OrdinalIgnoreCase);

            _pendingExternalRenames.Clear();
        }

        var selectedMainPaths =
            GetSelectedOrganizeFiles()
                .Select(file =>
                    RemapPathByRenames(
                        file.FullPath,
                        renames))
                .ToList();

        var folderState =
            CaptureFolderDetailState();

        if (folderState is not null)
        {
            folderState =
                RemapFolderDetailState(
                    folderState,
                    renames);
        }

        var preservedAssignments =
            CaptureManualAssignments(
                renames);

        var preservedExtensionAssignments =
            CaptureTemporaryExtensionAssignments();

        try
        {
            if (OrganizeManageOverlay.Visibility ==
                Visibility.Visible)
            {
                CloseOrganizeManageOverlay();
            }

            if (AssignmentOverlay.Visibility ==
                Visibility.Visible)
            {
                CloseAssignmentOverlay();
            }

            await AnalyzeFilesAsync(
                forcePreview:
                    true,
                preservedAssignments:
                    preservedAssignments,
                preservedExtensionAssignments:
                    preservedExtensionAssignments);

            if (PreviewStatePanel.Visibility !=
                Visibility.Visible)
            {
                return;
            }

            var folderRestored =
                folderState is not null &&
                RestoreFolderDetailState(
                    folderState);

            if (!folderRestored)
            {
                RestoreMainSelectionByPaths(
                    selectedMainPaths);
            }

            ShowOrganizeActionStatus(
                "Se detectaron cambios externos. El análisis se actualizó automáticamente.");
        }
        finally
        {
            _isLiveSourceRefreshRunning =
                false;

            if (_liveSourceRefreshPending)
            {
                _liveSourceRefreshPending =
                    false;
                ScheduleLiveSourceRefresh();
            }
        }
    }

    private Dictionary<string, string> CaptureManualAssignments(
        IReadOnlyDictionary<string, string>? renames = null)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var file in _files.Where(file =>
                     file.AssignmentSource ==
                         OrganizeAssignmentSource.IndividualOverride &&
                     file.IsClassified &&
                     !string.IsNullOrWhiteSpace(
                         file.CategoryId)))
        {
            var path =
                renames is null
                    ? file.FullPath
                    : RemapPathByRenames(
                        file.FullPath,
                        renames);

            result[path] =
                file.CategoryId!;
        }

        return result;
    }

    private OrganizeFolderDetailState? CaptureFolderDetailState()
    {
        if (_folderDetailRoot is null ||
            OrganizeFolderContentsPanel.Visibility !=
                Visibility.Visible)
        {
            return null;
        }

        var rootPath =
            _folderDetailRoot.FullPath;

        var currentDirectoryPath =
            string.IsNullOrWhiteSpace(
                _folderDetailCurrentRelativePath)
                ? rootPath
                : Path.GetFullPath(
                    Path.Combine(
                        rootPath,
                        _folderDetailCurrentRelativePath));

        var selectedEntryPaths =
            GetSelectedFolderContentItems()
                .Select(GetFolderContentFullPath)
                .ToList();

        return new OrganizeFolderDetailState(
            rootPath,
            currentDirectoryPath,
            selectedEntryPaths);
    }

    private static OrganizeFolderDetailState RemapFolderDetailState(
        OrganizeFolderDetailState state,
        IReadOnlyDictionary<string, string> renames) =>
        new(
            RemapPathByRenames(
                state.RootPath,
                renames),
            RemapPathByRenames(
                state.CurrentDirectoryPath,
                renames),
            state.SelectedEntryPaths
                .Select(path =>
                    RemapPathByRenames(
                        path,
                        renames))
                .ToList());

    private static string RemapPathByRenames(
        string path,
        IReadOnlyDictionary<string, string> renames)
    {
        var current =
            Path.GetFullPath(
                path);

        for (var pass = 0;
             pass < renames.Count;
             pass++)
        {
            var match =
                renames
                    .Where(pair =>
                        IsSameOrInsidePath(
                            current,
                            pair.Key))
                    .OrderByDescending(pair =>
                        pair.Key.Length)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(
                    match.Key))
            {
                break;
            }

            var oldRoot =
                Path.GetFullPath(
                    match.Key);

            var newRoot =
                Path.GetFullPath(
                    match.Value);

            if (current.Equals(
                    oldRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                current =
                    newRoot;
                continue;
            }

            var relative =
                Path.GetRelativePath(
                    oldRoot,
                    current);

            current =
                Path.GetFullPath(
                    Path.Combine(
                        newRoot,
                        relative));
        }

        return current;
    }

    private void RestoreMainSelectionByPaths(
        IReadOnlyCollection<string> paths)
    {
        var requested =
            paths
                .Select(Path.GetFullPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var matches =
            PreviewFilesList.Items
                .OfType<OrganizePreviewFile>()
                .Where(item =>
                    requested.Contains(
                        Path.GetFullPath(
                            item.FullPath)))
                .ToList();

        _isRefreshingPreview =
            true;

        try
        {
            PreviewFilesList.SelectedItems.Clear();

            foreach (var item in matches)
            {
                PreviewFilesList.SelectedItems.Add(
                    item);
            }
        }
        finally
        {
            _isRefreshingPreview =
                false;
        }

        if (matches.Count == 1)
        {
            ShowOrganizeDetail(
                matches[0]);
        }
        else if (matches.Count > 1)
        {
            ShowMultipleOrganizeDetails(
                matches);
        }
        else
        {
            ShowOrganizeSummary();
        }
    }

    private bool RestoreFolderDetailState(
        OrganizeFolderDetailState state)
    {
        var root =
            _files.FirstOrDefault(file =>
                file.IsDirectory &&
                file.FullPath.Equals(
                    state.RootPath,
                    StringComparison.OrdinalIgnoreCase));

        if (root is null)
        {
            return false;
        }

        _isRefreshingPreview =
            true;

        try
        {
            PreviewFilesList.SelectedItems.Clear();
            PreviewFilesList.SelectedItems.Add(
                root);
        }
        finally
        {
            _isRefreshingPreview =
                false;
        }

        ShowOrganizeDetail(
            root);

        var currentRelativePath =
            ResolveExistingFolderDetailRelativePath(
                root,
                state.CurrentDirectoryPath);

        _folderDetailCurrentRelativePath =
            currentRelativePath;

        RebuildFolderDetailHistory(
            currentRelativePath);

        RefreshFolderDetailView();

        var requestedEntries =
            state.SelectedEntryPaths
                .Select(Path.GetFullPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var selected =
            FolderDetailFilesList.Items
                .OfType<FolderContentPreviewItem>()
                .Where(item =>
                    requestedEntries.Contains(
                        Path.GetFullPath(
                            GetFolderContentFullPath(
                                item))))
                .ToList();

        _syncingFolderDetailSelection =
            true;

        try
        {
            FolderDetailFilesList.SelectedItems.Clear();

            foreach (var item in selected)
            {
                FolderDetailFilesList.SelectedItems.Add(
                    item);
            }
        }
        finally
        {
            _syncingFolderDetailSelection =
                false;
        }

        FolderDetailSelectionText.Text =
            selected.Count switch
            {
                0 =>
                    "Seleccioná para gestionar",
                1 =>
                    "1 seleccionado",
                _ =>
                    $"{selected.Count} seleccionados"
            };

        if (selected.Count == 1)
        {
            ShowFolderContentItemDetails(
                selected[0]);
        }
        else if (selected.Count > 1)
        {
            ShowMultipleFolderContentDetails(
                selected);
        }
        else
        {
            ShowOrganizeDetail(
                root,
                preserveFolderContext:
                    true);
        }

        return true;
    }

    private static string ResolveExistingFolderDetailRelativePath(
        OrganizePreviewFile root,
        string requestedDirectoryPath)
    {
        string relativePath;

        try
        {
            if (!IsSameOrInsidePath(
                    requestedDirectoryPath,
                    root.FullPath))
            {
                return string.Empty;
            }

            relativePath =
                Path.GetRelativePath(
                    root.FullPath,
                    requestedDirectoryPath);

            if (relativePath.Equals(
                    ".",
                    StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }
        catch
        {
            return string.Empty;
        }

        while (!string.IsNullOrWhiteSpace(
                   relativePath))
        {
            if (root.FolderContents.Any(item =>
                    item.IsDirectory &&
                    item.RelativePath.Equals(
                        relativePath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return relativePath;
            }

            relativePath =
                GetParentRelativePath(
                    relativePath);
        }

        return string.Empty;
    }

    private void RebuildFolderDetailHistory(
        string currentRelativePath)
    {
        _folderDetailHistory.Clear();

        if (string.IsNullOrWhiteSpace(
                currentRelativePath))
        {
            return;
        }

        _folderDetailHistory.Push(
            string.Empty);

        var parts =
            currentRelativePath.Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries);

        var accumulated =
            string.Empty;

        for (var index = 0;
             index < parts.Length - 1;
             index++)
        {
            accumulated =
                string.IsNullOrWhiteSpace(
                    accumulated)
                    ? parts[index]
                    : Path.Combine(
                        accumulated,
                        parts[index]);

            _folderDetailHistory.Push(
                accumulated);
        }
    }

    private async Task ReanalyzePreviewAfterSourceActionAsync(
        string? preferredSelectionPath,
        string? renamedFromPath = null,
        string? preferredFolderRelativePath = null)
    {
        var preservedAssignments =
            CaptureManualAssignments();

        var preservedExtensionAssignments =
            CaptureTemporaryExtensionAssignments();

        if (!string.IsNullOrWhiteSpace(
                renamedFromPath) &&
            !string.IsNullOrWhiteSpace(
                preferredSelectionPath) &&
            preservedAssignments.Remove(
                renamedFromPath,
                out var renamedCategoryId))
        {
            preservedAssignments[
                preferredSelectionPath] =
                renamedCategoryId;
        }

        await AnalyzeFilesAsync(
            forcePreview: true,
            preferredSelectionPath:
                preferredSelectionPath,
            preservedAssignments:
                preservedAssignments,
            preservedExtensionAssignments:
                preservedExtensionAssignments);

        if (_folderDetailRoot is null ||
            preferredFolderRelativePath is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                preferredFolderRelativePath) ||
            _folderDetailRoot.FolderContents.Any(item =>
                item.IsDirectory &&
                item.RelativePath.Equals(
                    preferredFolderRelativePath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _folderDetailCurrentRelativePath =
                preferredFolderRelativePath;

            RefreshFolderDetailView();

            ShowOrganizeDetail(
                _folderDetailRoot,
                preserveFolderContext: true);
        }
    }

    private void PreviewCategorySelectorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingPreview ||
            sender is not Button { Tag: string itemId })
        {
            return;
        }

        var folder =
            _files.FirstOrDefault(item =>
                item.ItemId.Equals(
                    itemId,
                    StringComparison.Ordinal));

        if (folder is null ||
            !folder.IsDirectory ||
            !folder.CanAssignFolder)
        {
            return;
        }

        _activeAssignmentExtension =
            null;
        _activeAssignmentFiles =
            [folder];
        _activeAssignmentIsFolder =
            true;
        _pendingAssignmentCategory =
            null;
        _isCreatingAssignmentCategory =
            false;

        AssignmentOverlayTitleText.Text =
            $"Asignar carpeta {folder.FileName}";
        AssignmentHelperText.Text =
            "Elegí la categoría de destino para esta carpeta. " +
            "La asignación se aplicará únicamente a esta organización.";

        AssignmentCategoryValueText.Text =
            "Elegir categoría";

        AssignmentCategorySelectorButton.Visibility =
            Visibility.Visible;
        AssignmentCreateCategoryPanel.Visibility =
            Visibility.Collapsed;
        AssignmentToggleCreateCategoryButton.Content =
            "Crear nueva categoría";
        AssignmentCategoryNameTextBox.Text =
            string.Empty;
        AssignmentCreateCategoryInfoText.Text =
            "La nueva categoría se agregará al final del orden actual. " +
            "Esta carpeta quedará asignada a ella solo en esta organización.";

        AssignmentRememberCheckBox.IsChecked =
            false;
        AssignmentRememberCheckBox.Visibility =
            Visibility.Collapsed;
        AssignmentValidationText.Text =
            string.Empty;
        AssignmentValidationText.Visibility =
            Visibility.Collapsed;

        BuildAssignmentCategoryOptions();
        AssignmentOverlay.Visibility =
            Visibility.Visible;
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
                    conflict.DestinationIsDirectory
                        ? "Ya existe una carpeta con el mismo nombre"
                        : "Ya existe un archivo con el mismo nombre";

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
                    conflict.DestinationIsDirectory
                        ? $"CARPETA · {conflict.DestinationItemCount} archivo{(conflict.DestinationItemCount == 1 ? string.Empty : "s")} · {FormatBytes(conflict.DestinationSizeBytes)} · Modificado {conflict.DestinationModifiedAt:dd/MM/yyyy HH:mm:ss}"
                        : $"ARCHIVO · {FormatBytes(conflict.DestinationSizeBytes)} · Modificado {conflict.DestinationModifiedAt:dd/MM/yyyy HH:mm:ss}";

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

        StopSourceWatcher();

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
            await ReanalyzePreviewAfterSourceActionAsync(
                preferredSelectionPath:
                    null);

            FooterStatusText.Foreground = GetBrush("BandaMutedStrongBrush");
            FooterStatusText.Text =
                "La organización fue cancelada. Los movimientos ya completados quedaron registrados y el análisis se actualizó.";
        }
        catch (Exception ex)
        {
            await ReanalyzePreviewAfterSourceActionAsync(
                preferredSelectionPath:
                    null);

            FooterStatusText.Foreground = GetBrush("BandaDangerBrush");
            FooterStatusText.Text =
                $"No se pudo completar la organización: {ex.Message}. El análisis se actualizó con el estado actual del origen.";
        }
    }

    private void ShowInitialState()
    {
        StopSourceWatcher();
        UpdateInitialStateText();

        if (PreviewFilesList is not null)
        {
            PreviewFilesList.SelectedItem =
                null;
        }

        ShowOrganizeSummary();

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
        StartSourceWatcher();
    }

    private async Task AnalyzeFilesAsync(
        bool forcePreview = false,
        string? preferredSelectionPath = null,
        IReadOnlyDictionary<string, string>? preservedAssignments = null,
        IReadOnlyList<PreservedExtensionAssignment>? preservedExtensionAssignments = null)
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
            "Leyendo la carpeta de origen en modo seguro. No se moverá ni creará ningún elemento.";

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
            ApplyPreservedExtensionAssignments(
                preservedExtensionAssignments);
            ApplyUnknownExtensionBehavior();
            ApplyPreservedAssignments(
                preservedAssignments);

            PreviewDestinationText.Text = result.DestinationFolder;

            if (result.Files.Count == 0)
            {
                ShowInitialState();
                AnalysisStatusText.Foreground = GetBrush("BandaAccentBrush");
                AnalysisStatusText.Text =
                    "No se encontraron elementos pendientes en la carpeta de origen.";
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

            if (forcePreview ||
                settings.PreviewBeforeOrganize ||
                requiresPreviewForUnknown ||
                hasFolderItems)
            {
                ShowPreviewState();

                if (!string.IsNullOrWhiteSpace(
                        preferredSelectionPath))
                {
                    SelectPreviewItemByPath(
                        preferredSelectionPath);
                }

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
                    "No hay elementos clasificables para organizar. Los elementos sin categoría permanecen en origen.";
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

    private void SelectPreviewItemByPath(
        string path)
    {
        var target =
            PreviewFilesList.Items
                .OfType<OrganizePreviewFile>()
                .FirstOrDefault(item =>
                    item.FullPath.Equals(
                        path,
                        StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return;
        }

        _isRefreshingPreview =
            true;

        try
        {
            PreviewFilesList.SelectedItems.Clear();
            PreviewFilesList.SelectedItems.Add(
                target);
        }
        finally
        {
            _isRefreshingPreview =
                false;
        }

        ShowOrganizeDetail(
            target);
    }

    private List<PreservedExtensionAssignment> CaptureTemporaryExtensionAssignments() =>
        _resolvedAssignments
            .Where(assignment =>
                !assignment.WasRemembered)
            .Select(assignment =>
                new PreservedExtensionAssignment(
                    assignment.Extension,
                    assignment.CategoryOrder,
                    assignment.CategoryName))
            .ToList();

    private void ApplyPreservedExtensionAssignments(
        IReadOnlyList<PreservedExtensionAssignment>? assignments)
    {
        if (assignments is null ||
            assignments.Count == 0)
        {
            return;
        }

        foreach (var assignment in assignments)
        {
            var category =
                _categories.FirstOrDefault(item =>
                    item.Order ==
                        assignment.CategoryOrder &&
                    item.Name.Equals(
                        assignment.CategoryName,
                        StringComparison.CurrentCultureIgnoreCase));

            if (category is null)
            {
                continue;
            }

            var affectedFiles =
                _files
                    .Where(file =>
                        !file.IsDirectory &&
                        !file.IsClassified &&
                        file.Extension.Equals(
                            assignment.Extension,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (affectedFiles.Count == 0)
            {
                continue;
            }

            foreach (var file in affectedFiles)
            {
                file.AssignTo(
                    category,
                    OrganizeAssignmentSource.ExtensionRule);
            }

            _resolvedAssignments.Add(
                new ResolvedExtensionAssignment(
                    assignment.Extension,
                    affectedFiles.Count,
                    category,
                    wasRemembered:
                        false,
                    persistenceText:
                        "Asignación aplicada solo a esta organización"));
        }
    }

    private void ApplyPreservedAssignments(
        IReadOnlyDictionary<string, string>? preservedAssignments)
    {
        if (preservedAssignments is null ||
            preservedAssignments.Count == 0)
        {
            return;
        }

        foreach (var file in _files)
        {
            if (!preservedAssignments.TryGetValue(
                    file.FullPath,
                    out var categoryId))
            {
                continue;
            }

            var category =
                _categories.FirstOrDefault(item =>
                    item.Id.Equals(
                        categoryId,
                        StringComparison.OrdinalIgnoreCase));

            if (category is null ||
                (file.IsDirectory &&
                 !file.CanAssignFolder))
            {
                continue;
            }

            file.AssignTo(
                category,
                OrganizeAssignmentSource.IndividualOverride);
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

        var hasSource =
            !string.IsNullOrWhiteSpace(
                settings.SourceFolder);
        var hasDestination =
            !string.IsNullOrWhiteSpace(
                settings.DestinationFolder);

        var source =
            hasSource
                ? settings.SourceFolder
                : "Sin configurar";

        AnalyzeButton.Content =
            settings.PreviewBeforeOrganize
                ? "Analizar archivos"
                : "Organizar archivos";

        AnalyzeButton.IsEnabled =
            hasSource &&
            hasDestination;

        if (!hasSource ||
            !hasDestination)
        {
            InitialAnalysisDescriptionText.Text =
                !hasSource && !hasDestination
                    ? "Configurá una carpeta de origen y una carpeta de destino para comenzar."
                    : !hasSource
                        ? "Configurá una carpeta de origen para comenzar."
                        : "Configurá una carpeta de destino para comenzar.";

            return;
        }

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

    private IReadOnlyDictionary<string, int> GetOrganizeCategoryOrder() =>
        _categories
            .GroupBy(
                category =>
                    category.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                group =>
                    group.Key,
                group =>
                    group.Min(category =>
                        category.Order),
                StringComparer.CurrentCultureIgnoreCase);

    private IEnumerable<OrganizePreviewFile> ApplyOrganizeFilters(
        IEnumerable<OrganizePreviewFile> source)
    {
        var query =
            source;

        if (_organizeExtensionFilters.Count > 0)
        {
            query =
                query.Where(
                    MatchesOrganizeExtensionFilter);
        }

        var now =
            DateTime.Now;

        query =
            _organizeDateFilter switch
            {
                SearchDateFilter.Today =>
                    query.Where(file =>
                        file.ModifiedAt >= now.Date &&
                        file.ModifiedAt <
                            now.Date.AddDays(
                                1)),

                SearchDateFilter.Last7Days =>
                    query.Where(file =>
                        file.ModifiedAt >=
                            now.AddDays(
                                -7)),

                SearchDateFilter.Last30Days =>
                    query.Where(file =>
                        file.ModifiedAt >=
                            now.AddDays(
                                -30)),

                SearchDateFilter.SpecificDate
                    when _organizeSpecificDateFilter.HasValue =>
                        query.Where(file =>
                            file.ModifiedAt >=
                                _organizeSpecificDateFilter.Value.Date &&
                            file.ModifiedAt <
                                _organizeSpecificDateFilter.Value.Date.AddDays(
                                    1)),

                _ =>
                    query
            };

        const long megabyte =
            1024L * 1024L;

        const long gigabyte =
            1024L * megabyte;

        query =
            _organizeSizeFilter switch
            {
                SearchSizeFilter.Under100Mb =>
                    query.Where(file =>
                        file.SizeBytes <
                            100 * megabyte),

                SearchSizeFilter.From100To500Mb =>
                    query.Where(file =>
                        file.SizeBytes >=
                            100 * megabyte &&
                        file.SizeBytes <
                            500 * megabyte),

                SearchSizeFilter.From500MbTo1Gb =>
                    query.Where(file =>
                        file.SizeBytes >=
                            500 * megabyte &&
                        file.SizeBytes <
                            gigabyte),

                SearchSizeFilter.From1To5Gb =>
                    query.Where(file =>
                        file.SizeBytes >=
                            gigabyte &&
                        file.SizeBytes <
                            5 * gigabyte),

                SearchSizeFilter.From5To20Gb =>
                    query.Where(file =>
                        file.SizeBytes >=
                            5 * gigabyte &&
                        file.SizeBytes <
                            20 * gigabyte),

                SearchSizeFilter.Over20Gb =>
                    query.Where(file =>
                        file.SizeBytes >=
                            20 * gigabyte),

                _ =>
                    query
            };

        return query;
    }

    private IEnumerable<FolderContentPreviewItem> ApplyOrganizeFilters(
        IEnumerable<FolderContentPreviewItem> source)
    {
        var query =
            source;

        if (_organizeExtensionFilters.Count > 0)
        {
            query =
                query.Where(
                    MatchesOrganizeFolderExtensionFilter);
        }

        var now =
            DateTime.Now;

        query =
            _organizeDateFilter switch
            {
                SearchDateFilter.Today =>
                    query.Where(item =>
                        item.ModifiedAt >= now.Date &&
                        item.ModifiedAt <
                            now.Date.AddDays(
                                1)),

                SearchDateFilter.Last7Days =>
                    query.Where(item =>
                        item.ModifiedAt >=
                            now.AddDays(
                                -7)),

                SearchDateFilter.Last30Days =>
                    query.Where(item =>
                        item.ModifiedAt >=
                            now.AddDays(
                                -30)),

                SearchDateFilter.SpecificDate
                    when _organizeSpecificDateFilter.HasValue =>
                        query.Where(item =>
                            item.ModifiedAt >=
                                _organizeSpecificDateFilter.Value.Date &&
                            item.ModifiedAt <
                                _organizeSpecificDateFilter.Value.Date.AddDays(
                                    1)),

                _ =>
                    query
            };

        const long megabyte =
            1024L * 1024L;

        const long gigabyte =
            1024L * megabyte;

        query =
            _organizeSizeFilter switch
            {
                SearchSizeFilter.Under100Mb =>
                    query.Where(item =>
                        item.SizeBytes <
                            100 * megabyte),

                SearchSizeFilter.From100To500Mb =>
                    query.Where(item =>
                        item.SizeBytes >=
                            100 * megabyte &&
                        item.SizeBytes <
                            500 * megabyte),

                SearchSizeFilter.From500MbTo1Gb =>
                    query.Where(item =>
                        item.SizeBytes >=
                            500 * megabyte &&
                        item.SizeBytes <
                            gigabyte),

                SearchSizeFilter.From1To5Gb =>
                    query.Where(item =>
                        item.SizeBytes >=
                            gigabyte &&
                        item.SizeBytes <
                            5 * gigabyte),

                SearchSizeFilter.From5To20Gb =>
                    query.Where(item =>
                        item.SizeBytes >=
                            5 * gigabyte &&
                        item.SizeBytes <
                            20 * gigabyte),

                SearchSizeFilter.Over20Gb =>
                    query.Where(item =>
                        item.SizeBytes >=
                            20 * gigabyte),

                _ =>
                    query
            };

        return query;
    }

    private bool MatchesOrganizeExtensionFilter(
        OrganizePreviewFile file)
    {
        if (_organizeExtensionFilters.Count == 0)
        {
            return true;
        }

        if (!file.IsDirectory)
        {
            return _organizeExtensionFilters.Contains(
                file.Extension);
        }

        return file.FolderContents.Any(item =>
            !item.IsDirectory &&
            _organizeExtensionFilters.Contains(
                item.Extension));
    }

    private bool MatchesOrganizeFolderExtensionFilter(
        FolderContentPreviewItem item)
    {
        if (_organizeExtensionFilters.Count == 0)
        {
            return true;
        }

        if (!item.IsDirectory)
        {
            return _organizeExtensionFilters.Contains(
                item.Extension);
        }

        if (_folderDetailRoot is null)
        {
            return false;
        }

        var prefix =
            item.RelativePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return _folderDetailRoot.FolderContents.Any(child =>
            !child.IsDirectory &&
            child.RelativePath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase) &&
            _organizeExtensionFilters.Contains(
                child.Extension));
    }

    private List<OrganizePreviewFile> SortOrganizePreviewFiles(
        IEnumerable<OrganizePreviewFile> source,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        IOrderedEnumerable<OrganizePreviewFile> ordered =
            _organizeSortField switch
            {
                SearchSortField.Name =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenByDescending(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Size =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenBy(file =>
                                file.SizeBytes)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenByDescending(file =>
                                file.SizeBytes)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Category =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenBy(file =>
                                GetOrganizeCategorySortOrder(
                                    file.CategoryName,
                                    categoryOrder,
                                    descending:
                                        false))
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenByDescending(file =>
                                GetOrganizeCategorySortOrder(
                                    file.CategoryName,
                                    categoryOrder,
                                    descending:
                                        true))
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Extension =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenBy(
                                file =>
                                    GetOrganizeSortableExtension(
                                        file),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenByDescending(
                                file =>
                                    GetOrganizeSortableExtension(
                                        file),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                _ =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenBy(file =>
                                file.ModifiedAt)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(file =>
                                file.IsDirectory)
                            .ThenByDescending(file =>
                                file.ModifiedAt)
                            .ThenBy(
                                file =>
                                    file.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
            };

        return ordered.ToList();
    }

    private List<FolderContentPreviewItem> SortOrganizeFolderItems(
        IEnumerable<FolderContentPreviewItem> source,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        IOrderedEnumerable<FolderContentPreviewItem> ordered =
            _organizeSortField switch
            {
                SearchSortField.Name =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenByDescending(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Size =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenBy(item =>
                                item.SizeBytes)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenByDescending(item =>
                                item.SizeBytes)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Category =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenBy(item =>
                                GetOrganizeCategorySortOrder(
                                    item.CategoryName,
                                    categoryOrder,
                                    descending:
                                        false))
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenByDescending(item =>
                                GetOrganizeCategorySortOrder(
                                    item.CategoryName,
                                    categoryOrder,
                                    descending:
                                        true))
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                SearchSortField.Extension =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenBy(
                                item =>
                                    GetOrganizeSortableExtension(
                                        item),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenByDescending(
                                item =>
                                    GetOrganizeSortableExtension(
                                        item),
                                StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase),

                _ =>
                    _organizeSortDirection ==
                        SearchSortDirection.Ascending
                        ? source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenBy(item =>
                                item.ModifiedAt)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
                        : source
                            .OrderByDescending(item =>
                                item.IsDirectory)
                            .ThenByDescending(item =>
                                item.ModifiedAt)
                            .ThenBy(
                                item =>
                                    item.FileName,
                                StringComparer.CurrentCultureIgnoreCase)
            };

        return ordered.ToList();
    }

    private static int GetOrganizeCategorySortOrder(
        string? categoryName,
        IReadOnlyDictionary<string, int> categoryOrder,
        bool descending)
    {
        if (!string.IsNullOrWhiteSpace(
                categoryName) &&
            categoryOrder.TryGetValue(
                categoryName,
                out var order))
        {
            return order;
        }

        return descending
            ? int.MinValue
            : int.MaxValue;
    }

    private static string GetOrganizeSortableExtension(
        OrganizePreviewFile file) =>
        file.IsDirectory
            ? string.Empty
            : Path.GetExtension(
                file.FileName);

    private static string GetOrganizeSortableExtension(
        FolderContentPreviewItem item) =>
        item.IsDirectory
            ? string.Empty
            : Path.GetExtension(
                item.FileName);

    private void ApplyOrganizePreviewView(
        IReadOnlyList<OrganizePreviewFile> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        VisibleOrganizePreviewFiles.Clear();

        foreach (var item in results)
        {
            VisibleOrganizePreviewFiles.Add(
                item);
        }

        _organizePreviewViewSource.Source =
            null;

        if (_organizeGroupField ==
            SearchGroupField.None)
        {
            GroupedOrganizePreviewFiles.Clear();

            _organizePreviewViewSource.IsSourceGrouped =
                false;

            _organizePreviewViewSource.Source =
                VisibleOrganizePreviewFiles;
        }
        else
        {
            BuildOrganizePreviewGroups(
                results,
                categoryOrder);

            _organizePreviewViewSource.IsSourceGrouped =
                true;

            _organizePreviewViewSource.Source =
                GroupedOrganizePreviewFiles;
        }

        PreviewFilesList.ItemsSource =
            _organizePreviewViewSource.View;
    }

    private void ApplyOrganizeFolderView(
        IReadOnlyList<FolderContentPreviewItem> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        VisibleOrganizeFolderItems.Clear();

        foreach (var item in results)
        {
            VisibleOrganizeFolderItems.Add(
                item);
        }

        _organizeFolderViewSource.Source =
            null;

        if (_organizeGroupField ==
            SearchGroupField.None)
        {
            GroupedOrganizeFolderItems.Clear();

            _organizeFolderViewSource.IsSourceGrouped =
                false;

            _organizeFolderViewSource.Source =
                VisibleOrganizeFolderItems;
        }
        else
        {
            BuildOrganizeFolderGroups(
                results,
                categoryOrder);

            _organizeFolderViewSource.IsSourceGrouped =
                true;

            _organizeFolderViewSource.Source =
                GroupedOrganizeFolderItems;
        }

        FolderDetailFilesList.ItemsSource =
            _organizeFolderViewSource.View;
    }

    private void BuildOrganizePreviewGroups(
        IReadOnlyList<OrganizePreviewFile> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        GroupedOrganizePreviewFiles.Clear();

        var now =
            DateTime.Now;

        var groups =
            results.GroupBy(file =>
                GetOrganizePreviewGroupDescriptor(
                    file,
                    now,
                    categoryOrder));

        var orderedGroups =
            _organizeSortDirection ==
                SearchSortDirection.Ascending
                ? groups
                    .OrderBy(group =>
                        group.Key.Order)
                    .ThenBy(
                        group =>
                            group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase)
                : groups
                    .OrderByDescending(group =>
                        group.Key.Order)
                    .ThenByDescending(
                        group =>
                            group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in orderedGroups)
        {
            GroupedOrganizePreviewFiles.Add(
                new OrganizePreviewGroup(
                    group.Key.Label,
                    group.ToList()));
        }
    }

    private void BuildOrganizeFolderGroups(
        IReadOnlyList<FolderContentPreviewItem> results,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        GroupedOrganizeFolderItems.Clear();

        var now =
            DateTime.Now;

        var groups =
            results.GroupBy(item =>
                GetOrganizeFolderGroupDescriptor(
                    item,
                    now,
                    categoryOrder));

        var orderedGroups =
            _organizeSortDirection ==
                SearchSortDirection.Ascending
                ? groups
                    .OrderBy(group =>
                        group.Key.Order)
                    .ThenBy(
                        group =>
                            group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase)
                : groups
                    .OrderByDescending(group =>
                        group.Key.Order)
                    .ThenByDescending(
                        group =>
                            group.Key.Label,
                        StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in orderedGroups)
        {
            GroupedOrganizeFolderItems.Add(
                new OrganizeFolderContentGroup(
                    group.Key.Label,
                    group.ToList()));
        }
    }

    private OrganizeGroupDescriptor GetOrganizePreviewGroupDescriptor(
        OrganizePreviewFile file,
        DateTime now,
        IReadOnlyDictionary<string, int> categoryOrder) =>
        _organizeGroupField switch
        {
            SearchGroupField.Name =>
                GetOrganizeNameGroupDescriptor(
                    file.FileName),

            SearchGroupField.DateModified =>
                GetOrganizeDateGroupDescriptor(
                    file.ModifiedAt,
                    now),

            SearchGroupField.Size =>
                GetOrganizeSizeGroupDescriptor(
                    file.SizeBytes,
                    file.IsDirectory),

            SearchGroupField.Category =>
                new OrganizeGroupDescriptor(
                    file.CategoryName ??
                        "Sin asignar",
                    GetOrganizeCategorySortOrder(
                        file.CategoryName,
                        categoryOrder,
                        descending:
                            false)),

            SearchGroupField.Extension =>
                file.IsDirectory
                    ? new OrganizeGroupDescriptor(
                        "Carpeta de archivos",
                        0)
                    : new OrganizeGroupDescriptor(
                        string.IsNullOrWhiteSpace(
                            Path.GetExtension(
                                file.FileName))
                            ? "Sin extensión"
                            : Path.GetExtension(
                                    file.FileName)
                                .ToUpperInvariant(),
                        1),

            _ =>
                new OrganizeGroupDescriptor(
                    "Elementos",
                    0)
        };

    private OrganizeGroupDescriptor GetOrganizeFolderGroupDescriptor(
        FolderContentPreviewItem item,
        DateTime now,
        IReadOnlyDictionary<string, int> categoryOrder) =>
        _organizeGroupField switch
        {
            SearchGroupField.Name =>
                GetOrganizeNameGroupDescriptor(
                    item.FileName),

            SearchGroupField.DateModified =>
                GetOrganizeDateGroupDescriptor(
                    item.ModifiedAt,
                    now),

            SearchGroupField.Size =>
                GetOrganizeSizeGroupDescriptor(
                    item.SizeBytes,
                    item.IsDirectory),

            SearchGroupField.Category =>
                new OrganizeGroupDescriptor(
                    item.CategoryDisplay,
                    GetOrganizeCategorySortOrder(
                        item.CategoryName,
                        categoryOrder,
                        descending:
                            false)),

            SearchGroupField.Extension =>
                item.IsDirectory
                    ? new OrganizeGroupDescriptor(
                        "Carpeta de archivos",
                        0)
                    : new OrganizeGroupDescriptor(
                        string.IsNullOrWhiteSpace(
                            Path.GetExtension(
                                item.FileName))
                            ? "Sin extensión"
                            : Path.GetExtension(
                                    item.FileName)
                                .ToUpperInvariant(),
                        1),

            _ =>
                new OrganizeGroupDescriptor(
                    "Elementos",
                    0)
        };

    private static OrganizeGroupDescriptor GetOrganizeNameGroupDescriptor(
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            return new OrganizeGroupDescriptor(
                "Otros",
                4);
        }

        var normalized =
            name.Trim()
                .Normalize(
                    System.Text.NormalizationForm.FormD);

        var first =
            char.ToUpperInvariant(
                normalized[0]);

        if (char.IsDigit(
                first))
        {
            return new OrganizeGroupDescriptor(
                "0–9",
                0);
        }

        if (first is >= 'A' and <= 'H')
        {
            return new OrganizeGroupDescriptor(
                "A–H",
                1);
        }

        if (first is >= 'I' and <= 'P')
        {
            return new OrganizeGroupDescriptor(
                "I–P",
                2);
        }

        if (first is >= 'Q' and <= 'Z')
        {
            return new OrganizeGroupDescriptor(
                "Q–Z",
                3);
        }

        return new OrganizeGroupDescriptor(
            "Otros",
            4);
    }

    private static OrganizeGroupDescriptor GetOrganizeDateGroupDescriptor(
        DateTime modifiedAt,
        DateTime now)
    {
        var date =
            modifiedAt.Date;

        var today =
            now.Date;

        if (date >= today)
        {
            return new OrganizeGroupDescriptor(
                "Hoy",
                7);
        }

        if (date >= today.AddDays(
                -1))
        {
            return new OrganizeGroupDescriptor(
                "Ayer",
                6);
        }

        var weekStart =
            GetOrganizeStartOfWeek(
                today);

        if (date >= weekStart)
        {
            return new OrganizeGroupDescriptor(
                "A principios de esta semana",
                5);
        }

        var lastWeekStart =
            weekStart.AddDays(
                -7);

        if (date >= lastWeekStart)
        {
            return new OrganizeGroupDescriptor(
                "La semana pasada",
                4);
        }

        var monthStart =
            new DateTime(
                today.Year,
                today.Month,
                1);

        if (date >= monthStart)
        {
            return new OrganizeGroupDescriptor(
                "A principios de este mes",
                3);
        }

        var lastMonthStart =
            monthStart.AddMonths(
                -1);

        if (date >= lastMonthStart)
        {
            return new OrganizeGroupDescriptor(
                "El mes pasado",
                2);
        }

        var yearStart =
            new DateTime(
                today.Year,
                1,
                1);

        if (date >= yearStart)
        {
            return new OrganizeGroupDescriptor(
                "A principios de este año",
                1);
        }

        return new OrganizeGroupDescriptor(
            "Hace mucho tiempo",
            0);
    }

    private static DateTime GetOrganizeStartOfWeek(
        DateTime date)
    {
        var firstDayOfWeek =
            CultureInfo.CurrentCulture
                .DateTimeFormat
                .FirstDayOfWeek;

        var difference =
            (7 +
             ((int)date.DayOfWeek -
              (int)firstDayOfWeek)) %
            7;

        return date.AddDays(
            -difference);
    }

    private static OrganizeGroupDescriptor GetOrganizeSizeGroupDescriptor(
        long sizeBytes,
        bool isDirectory)
    {
        if (isDirectory)
        {
            return new OrganizeGroupDescriptor(
                "Sin especificar",
                0);
        }

        if (sizeBytes == 0)
        {
            return new OrganizeGroupDescriptor(
                "Vacío",
                1);
        }

        const long kilobyte =
            1024L;

        const long megabyte =
            1024L * kilobyte;

        const long gigabyte =
            1024L * megabyte;

        if (sizeBytes <
            16 * kilobyte)
        {
            return new OrganizeGroupDescriptor(
                "Muy pequeño",
                2);
        }

        if (sizeBytes <
            megabyte)
        {
            return new OrganizeGroupDescriptor(
                "Pequeño",
                3);
        }

        if (sizeBytes <
            128 * megabyte)
        {
            return new OrganizeGroupDescriptor(
                "Mediano",
                4);
        }

        if (sizeBytes <
            gigabyte)
        {
            return new OrganizeGroupDescriptor(
                "Grande",
                5);
        }

        if (sizeBytes <
            4 * gigabyte)
        {
            return new OrganizeGroupDescriptor(
                "Muy grande",
                6);
        }

        return new OrganizeGroupDescriptor(
            "Gigantesco",
            7);
    }

    private sealed record OrganizeGroupDescriptor(
        string Label,
        int Order);

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

        var selectedItemIds =
            PreviewFilesList.SelectedItems
                .OfType<OrganizePreviewFile>()
                .Select(item =>
                    item.ItemId)
                .ToHashSet(
                    StringComparer.Ordinal);

        var categoryOrder =
            GetOrganizeCategoryOrder();

        var visiblePreviewFiles =
            SortOrganizePreviewFiles(
                ApplyOrganizeFilters(
                    _files),
                categoryOrder);

        _isRefreshingPreview =
            true;

        try
        {
            PreviewFilesList.SelectedItems.Clear();

            ApplyOrganizePreviewView(
                visiblePreviewFiles,
                categoryOrder);

            foreach (var item in visiblePreviewFiles.Where(item =>
                         selectedItemIds.Contains(
                             item.ItemId)))
            {
                PreviewFilesList.SelectedItems.Add(
                    item);
            }
        }
        finally
        {
            _isRefreshingPreview =
                false;
        }

        UpdateOrganizeFiltersButtonVisual();

        var restoredSelection =
            GetSelectedOrganizeFiles();

        if (restoredSelection.Count == 1)
        {
            var selected =
                restoredSelection[0];

            var preserveFolderContext =
                selected.IsDirectory &&
                _folderDetailRoot is not null &&
                _folderDetailRoot.FullPath.Equals(
                    selected.FullPath,
                    StringComparison.OrdinalIgnoreCase);

            ShowOrganizeDetail(
                selected,
                preserveFolderContext);
        }
        else if (restoredSelection.Count > 1)
        {
            ShowMultipleOrganizeDetails(
                restoredSelection);
        }
        else
        {
            ShowOrganizeSummary();
        }

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

        SyncUnassignedFolderSelection(
            restoredSelection);
        SyncUnassignedExtensionSelection(
            restoredSelection);

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
                    ? "Las extensiones nuevas se pueden resolver acá. Las carpetas siempre requieren una asignación manual antes de organizarse."
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

    private sealed record PreservedExtensionAssignment(
        string Extension,
        int CategoryOrder,
        string CategoryName);

    private sealed record OrganizeFolderDetailState(
        string RootPath,
        string CurrentDirectoryPath,
        IReadOnlyList<string> SelectedEntryPaths);

    private sealed record OrganizeActionTarget(
        string FullPath,
        string FileName,
        bool IsDirectory,
        long SizeBytes,
        DateTime ModifiedAt,
        string? CategoryName,
        string ExtensionDisplay,
        OrganizePreviewFile RootPreviewFile,
        bool IsRootItem);
}

public sealed class OrganizePreviewGroup
{
    public OrganizePreviewGroup(
        string name,
        IReadOnlyList<OrganizePreviewFile> items)
    {
        Name =
            name;

        Items =
            new ObservableCollection<OrganizePreviewFile>(
                items);
    }

    public string Name { get; }
    public ObservableCollection<OrganizePreviewFile> Items { get; }

    public string HeaderText =>
        $"{Name} ({Items.Count})";
}

public sealed class OrganizeFolderContentGroup
{
    public OrganizeFolderContentGroup(
        string name,
        IReadOnlyList<FolderContentPreviewItem> items)
    {
        Name =
            name;

        Items =
            new ObservableCollection<FolderContentPreviewItem>(
                items);
    }

    public string Name { get; }
    public ObservableCollection<FolderContentPreviewItem> Items { get; }

    public string HeaderText =>
        $"{Name} ({Items.Count})";
}

public enum OrganizeManageMode
{
    None,
    ChangeCategory,
    Rename,
    Delete
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
                        item.CategoryName,
                        item.IsDirectory,
                        item.ContainedFileCount,
                        item.DistinctCategoryCount))
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

    public bool CanAssignFolder =>
        IsDirectory &&
        !ScanIncomplete;

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
            var nestedFolderCount =
                FolderContents.Count(item => item.IsDirectory);

            var parts =
                new List<string>
                {
                    $"{ContainedFileCount} archivo{(ContainedFileCount == 1 ? string.Empty : "s")}"
                };

            if (nestedFolderCount > 0)
            {
                parts.Add(
                    $"{nestedFolderCount} subcarpeta{(nestedFolderCount == 1 ? string.Empty : "s")}");
            }

            parts.Add(
                $"{RecognizedFileCount} reconocido{(RecognizedFileCount == 1 ? string.Empty : "s")}");

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

    public Brush CategoryBrush
    {
        get
        {
            var category =
                SelectedCategory;

            if (category is null ||
                !CategoryColorPalette.TryNormalizeHex(
                    category.ColorHex,
                    out var normalized))
            {
                return (Brush)Application.Current.Resources[
                    IsClassified
                        ? "BandaAccentBrush"
                        : "BandaMutedBrush"];
            }

            return new SolidColorBrush(
                Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(
                        normalized.Substring(1, 2),
                        16),
                    Convert.ToByte(
                        normalized.Substring(3, 2),
                        16),
                    Convert.ToByte(
                        normalized.Substring(5, 2),
                        16)));
        }
    }

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
        string? categoryName,
        bool isDirectory,
        int containedFileCount,
        int distinctCategoryCount)
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

        IsDirectory =
            isDirectory;

        ContainedFileCount =
            containedFileCount;

        DistinctCategoryCount =
            distinctCategoryCount;
    }

    public string RelativePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string? CategoryName { get; }
    public bool IsDirectory { get; }
    public int ContainedFileCount { get; }
    public int DistinctCategoryCount { get; }

    public string ExtensionDisplay =>
        IsDirectory
            ? "CARPETA"
            : Extension.ToUpperInvariant();

    public string CategoryDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(
                    CategoryName))
            {
                return CategoryName;
            }

            if (!IsDirectory)
            {
                return "Sin categoría";
            }

            if (ContainedFileCount == 0)
            {
                return "Vacía";
            }

            if (DistinctCategoryCount > 1)
            {
                return "Varias categorías";
            }

            return "Sin categoría";
        }
    }

    public string SizeDisplay =>
        FormatBytes(
            SizeBytes);

    public string ModifiedDisplay =>
        ModifiedAt == DateTime.MinValue
            ? "—"
            : ModifiedAt.ToString(
                "dd/MM/yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);

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
