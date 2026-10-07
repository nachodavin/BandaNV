using BandaNV.App.Pages;
using BandaNV.App.Services;
using BandaNV.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private TrayIconService? _trayIconService;
    private Button? _selectedNavigationButton;
    private string _activeTheme = "Oscuro";
    private bool _xamlRootChangedHooked;
    private bool _layoutRefreshQueued;
    private bool _syncingUpdateStartupNoticeToggle;
    private CancellationTokenSource? _updateDownloadCts;
    private UpdateCheckResult? _availableUpdate;
    private bool _isUpdateModalBusy;

    public MainWindow()
    {
        InitializeComponent();

        _activeTheme =
            App.Settings.Current.Theme;

        RootLayout.RequestedTheme =
            AppearanceService.GetElementTheme(
                _activeTheme);

        Title = "BandaNV";
        ApplyStartupPage();

        Activated += MainWindow_Activated;
        ContentFrame.Loaded += ContentFrame_Loaded;
        ContentFrame.SizeChanged += ContentFrame_SizeChanged;
    }

    private void ApplyStartupPage()
    {
        var startupPage =
            App.Settings.Current.StartupPage?
                .Trim();

        var (pageType, button) =
            startupPage?.ToLowerInvariant() switch
            {
                "organizar" =>
                    (typeof(OrganizePage), OrganizeButton),
                "buscar" =>
                    (typeof(SearchPage), SearchButton),
                _ =>
                    (typeof(HomePage), HomeButton)
            };

        _selectedNavigationButton =
            button;

        ContentFrame.Navigate(
            pageType);

        SetSelectedNavigationButton(
            button);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        TryMaximizeWindow();
    }

    private void TryMaximizeWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;
            ConfigureTitleBar();
            ConfigureWindowIcon();
            ConfigureTrayIcon(hwnd);

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch
        {
            // Si Windows no permite maximizar en este punto, la app sigue siendo usable.
        }
    }


    private void ConfigureTrayIcon(
        nint windowHandle)
    {
        if (_trayIconService is not null)
        {
            return;
        }

        try
        {
            var iconPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "assets",
                    "BandaNV.ico");

            _trayIconService =
                new TrayIconService(
                    windowHandle,
                    iconPath);

            _trayIconService.RestoreRequested +=
                TrayIcon_RestoreRequested;
        }
        catch
        {
            _trayIconService =
                null;
        }
    }

    private void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (!App.Settings.Current.CloseBehavior.Equals(
                "Minimizar a bandeja",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_trayIconService?.Show() != true)
        {
            return;
        }

        args.Cancel =
            true;

        sender.Hide();
    }

    private void TrayIcon_RestoreRequested(
        object? sender,
        EventArgs e)
    {
        DispatcherQueue.TryEnqueue(
            () =>
            {
                _trayIconService?.Hide();

                try
                {
                    _appWindow?.Show();
                    Activate();
                }
                catch
                {
                }
            });
    }

    public void BringToFront()
    {
        _trayIconService?.Hide();

        try
        {
            _appWindow?.Show();

            if (_appWindow?.Presenter is OverlappedPresenter presenter &&
                presenter.State ==
                    OverlappedPresenterState.Minimized)
            {
                presenter.Restore(
                    activateWindow: true);
            }

            Activate();
        }
        catch
        {
            try
            {
                Activate();
            }
            catch
            {
            }
        }
    }

    private void ContentFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncCurrentPageToViewport(e.NewSize.Width);
    }

    private void SyncCurrentPageToViewport(double width)
    {
        if (width <= 0 || ContentFrame.Content is not FrameworkElement currentPage)
        {
            return;
        }

        // El Frame conoce el ancho lógico correcto del monitor actual.
        // Mantener la página visible sincronizada con ese viewport evita que
        // conserve el ancho del monitor anterior al cambiar de 1440p a 1080p.
        currentPage.Width = width;
        currentPage.MaxWidth = width;
        currentPage.HorizontalAlignment = HorizontalAlignment.Stretch;
        currentPage.InvalidateMeasure();
        currentPage.InvalidateArrange();
    }

    private void ContentFrame_Loaded(object sender, RoutedEventArgs e)
    {
        if (_xamlRootChangedHooked || ContentFrame.XamlRoot is null)
        {
            return;
        }

        ContentFrame.XamlRoot.Changed += XamlRoot_Changed;
        _xamlRootChangedHooked = true;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        QueueLayoutRefresh();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange)
        {
            QueueLayoutRefresh();
        }
    }

    private void QueueLayoutRefresh()
    {
        if (_layoutRefreshQueued)
        {
            return;
        }

        _layoutRefreshQueued = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            _layoutRefreshQueued = false;

            // Al mover BandaNV entre monitores con distinta resolución o escala,
            // WinUI puede actualizar el DPI antes de volver a medir la página actual.
            // Forzamos una nueva medición en el siguiente ciclo de UI para que la
            // sección visible se adapte sin tener que navegar a otra página.
            RootLayout.InvalidateMeasure();
            RootLayout.InvalidateArrange();
            ContentFrame.InvalidateMeasure();
            ContentFrame.InvalidateArrange();
            RootLayout.UpdateLayout();

            // Después de que el shell adopta el tamaño del monitor destino,
            // copiamos el viewport real del Frame a la página actualmente visible.
            SyncCurrentPageToViewport(ContentFrame.ActualWidth);
            ContentFrame.UpdateLayout();
        });
    }


    private void ConfigureWindowIcon()
    {
        if (_appWindow is null)
        {
            return;
        }

        try
        {
            var iconPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "assets",
                    "BandaNV.ico");

            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(
                    iconPath);
            }
        }
        catch
        {
            // El icono embebido del ejecutable sigue disponible como fallback.
        }
    }

    public void ApplyThemeSetting(
        string? theme)
    {
        _activeTheme =
            string.IsNullOrWhiteSpace(
                theme)
                ? "Oscuro"
                : theme.Trim();

        RootLayout.RequestedTheme =
            AppearanceService.GetElementTheme(
                _activeTheme);

        ConfigureTitleBar();
    }

    private void ConfigureTitleBar()
    {
        if (_appWindow is null ||
            !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var light =
            AppearanceService.ResolveLightMode(
                _activeTheme);

        var titleBar =
            _appWindow.TitleBar;

        var foreground =
            light
                ? Windows.UI.Color.FromArgb(
                    255,
                    0x17,
                    0x22,
                    0x28)
                : Windows.UI.Color.FromArgb(
                    255,
                    255,
                    255,
                    255);

        var background =
            light
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xF4,
                    0xF7,
                    0xF8)
                : Windows.UI.Color.FromArgb(
                    255,
                    9,
                    13,
                    18);

        var inactiveForeground =
            light
                ? Windows.UI.Color.FromArgb(
                    255,
                    0x68,
                    0x75,
                    0x7C)
                : Windows.UI.Color.FromArgb(
                    255,
                    150,
                    160,
                    170);

        var hoverBackground =
            light
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xE5,
                    0xEA,
                    0xEC)
                : Windows.UI.Color.FromArgb(
                    255,
                    28,
                    38,
                    47);

        var pressedBackground =
            light
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xD7,
                    0xE0,
                    0xE3)
                : Windows.UI.Color.FromArgb(
                    255,
                    37,
                    49,
                    60);

        titleBar.ForegroundColor =
            foreground;
        titleBar.BackgroundColor =
            background;
        titleBar.InactiveForegroundColor =
            inactiveForeground;
        titleBar.InactiveBackgroundColor =
            background;

        titleBar.ButtonForegroundColor =
            foreground;
        titleBar.ButtonBackgroundColor =
            background;
        titleBar.ButtonHoverForegroundColor =
            foreground;
        titleBar.ButtonHoverBackgroundColor =
            hoverBackground;
        titleBar.ButtonPressedForegroundColor =
            foreground;
        titleBar.ButtonPressedBackgroundColor =
            pressedBackground;
        titleBar.ButtonInactiveForegroundColor =
            inactiveForeground;
        titleBar.ButtonInactiveBackgroundColor =
            background;
    }


    public void SetAvailableUpdate(
        UpdateCheckResult result,
        bool showModal)
    {
        _availableUpdate = result;

        QuickUpdateTitle.Text =
            "Nueva versión";
        QuickUpdateSubtitle.Text =
            $"Disponible: {result.AvailableVersion}";
        QuickUpdateButton.Visibility =
            Visibility.Visible;

        if (showModal)
        {
            ShowAvailableUpdateModal(
                result);
        }
    }

    public void ClearAvailableUpdate()
    {
        _availableUpdate = null;
        QuickUpdateButton.Visibility =
            Visibility.Collapsed;

        if (!_isUpdateModalBusy)
        {
            UpdateModalOverlay.Visibility =
                Visibility.Collapsed;
        }
    }

    private void QuickUpdateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_availableUpdate is { } result)
        {
            ShowAvailableUpdateModal(
                result);
            return;
        }

        QuickUpdateButton.Visibility =
            Visibility.Collapsed;
    }

    private void ShowAvailableUpdateModal(
        UpdateCheckResult result)
    {
        _availableUpdate = result;
        _isUpdateModalBusy = false;

        _updateDownloadCts?.Cancel();
        _updateDownloadCts?.Dispose();
        _updateDownloadCts = null;

        UpdateModalTitleText.Text =
            "Actualización disponible";

        UpdateModalSubtitleText.Text =
            result.CanInstall
                ? "Hay una nueva versión de BandaNV lista para instalar."
                : string.IsNullOrWhiteSpace(
                    result.Message)
                    ? "Hay una nueva versión disponible, pero no puede instalarse automáticamente."
                    : result.Message;

        UpdateInstalledVersionText.Text =
            result.InstalledVersion;
        UpdateAvailableVersionText.Text =
            result.AvailableVersion;
        UpdateReleaseNotesText.Text =
            FormatReleaseNotesForDisplay(
                result.ReleaseNotes);

        UpdateNotesPanel.Visibility =
            Visibility.Visible;
        UpdateProgressPanel.Visibility =
            Visibility.Collapsed;
        UpdateProgressBar.Visibility =
            Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressStateText.Text =
            "Preparando descarga...";

        UpdateModalPrimaryButton.Content =
            result.CanInstall
                ? "Actualizar"
                : "No disponible";
        UpdateModalPrimaryButton.Visibility =
            Visibility.Visible;
        UpdateModalPrimaryButton.IsEnabled =
            result.CanInstall;

        UpdateModalSecondaryButton.Content =
            "Más tarde";
        UpdateModalSecondaryButton.Visibility =
            Visibility.Visible;
        UpdateModalSecondaryButton.IsEnabled =
            true;

        UpdateModalCloseButton.IsEnabled =
            true;
        UpdateModalCloseButton.Opacity = 1;

        UpdateStartupNoticeToggle.IsEnabled =
            true;

        SyncUpdateStartupNoticeToggle(
            App.Settings.Current.AutoUpdate);

        UpdateModalOverlay.Visibility =
            Visibility.Visible;
    }

    public void SyncUpdateStartupNoticeToggle(
        bool enabled)
    {
        _syncingUpdateStartupNoticeToggle =
            true;

        try
        {
            UpdateStartupNoticeToggle.IsOn =
                enabled;
        }
        finally
        {
            _syncingUpdateStartupNoticeToggle =
                false;
        }
    }

    private async void UpdateStartupNoticeToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_syncingUpdateStartupNoticeToggle)
        {
            return;
        }

        var enabled =
            UpdateStartupNoticeToggle.IsOn;

        App.Settings.Current.AutoUpdate =
            enabled;

        try
        {
            await App.Settings.SaveAsync(
                App.Settings.Current);

            if (ContentFrame.Content is SettingsPage settingsPage)
            {
                settingsPage.SyncUpdateStartupNoticeToggle(
                    enabled);
            }
        }
        catch
        {
            SyncUpdateStartupNoticeToggle(
                App.Settings.Current.AutoUpdate);
        }
    }

    private async void UpdateModalPrimaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isUpdateModalBusy ||
            _availableUpdate is not { } result ||
            !result.CanInstall)
        {
            return;
        }

        await DownloadAndInstallUpdateAsync(
            result);
    }

    private void UpdateModalSecondaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isUpdateModalBusy)
        {
            _updateDownloadCts?.Cancel();
            UpdateProgressStateText.Text =
                "Cancelando descarga...";
            UpdateModalSecondaryButton.IsEnabled =
                false;
            return;
        }

        CloseUpdateModal();
    }

    private void UpdateModalCloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseUpdateModal();
    }

    private void UpdateModalBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseUpdateModal();
    }

    private void CloseUpdateModal()
    {
        if (_isUpdateModalBusy)
        {
            return;
        }

        UpdateModalOverlay.Visibility =
            Visibility.Collapsed;

        _updateDownloadCts?.Cancel();
        _updateDownloadCts?.Dispose();
        _updateDownloadCts = null;
    }

    private void SetUpdateModalBusy(
        UpdateCheckResult result)
    {
        _isUpdateModalBusy = true;

        UpdateModalTitleText.Text =
            "Preparando actualización";
        UpdateModalSubtitleText.Text =
            $"{result.InstalledVersion} → {result.AvailableVersion}";

        UpdateNotesPanel.Visibility =
            Visibility.Collapsed;
        UpdateProgressPanel.Visibility =
            Visibility.Visible;
        UpdateProgressBar.Visibility =
            Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressStateText.Text =
            "Preparando descarga...";

        UpdateModalPrimaryButton.Visibility =
            Visibility.Collapsed;

        UpdateModalSecondaryButton.Content =
            "Cancelar";
        UpdateModalSecondaryButton.IsEnabled =
            true;

        UpdateModalCloseButton.IsEnabled =
            false;
        UpdateModalCloseButton.Opacity = 0.45;
        UpdateStartupNoticeToggle.IsEnabled =
            false;
    }

    private void SetUpdateModalError(
        string message)
    {
        _isUpdateModalBusy = false;

        UpdateModalTitleText.Text =
            "No se pudo preparar la actualización";
        UpdateModalSubtitleText.Text =
            message;

        UpdateNotesPanel.Visibility =
            Visibility.Collapsed;
        UpdateProgressPanel.Visibility =
            Visibility.Visible;
        UpdateProgressBar.Visibility =
            Visibility.Collapsed;
        UpdateProgressStateText.Text =
            "BandaNV no fue modificado. Podés volver a intentarlo o cerrar este aviso.";

        UpdateModalPrimaryButton.Content =
            "Reintentar";
        UpdateModalPrimaryButton.Visibility =
            Visibility.Visible;
        UpdateModalPrimaryButton.IsEnabled =
            true;

        UpdateModalSecondaryButton.Content =
            "Cerrar";
        UpdateModalSecondaryButton.IsEnabled =
            true;

        UpdateModalCloseButton.IsEnabled =
            true;
        UpdateModalCloseButton.Opacity = 1;
        UpdateStartupNoticeToggle.IsEnabled =
            true;
    }

    private async Task DownloadAndInstallUpdateAsync(
        UpdateCheckResult result)
    {
        _updateDownloadCts?.Cancel();
        _updateDownloadCts?.Dispose();

        var cancellation =
            new CancellationTokenSource();

        _updateDownloadCts =
            cancellation;

        SetUpdateModalBusy(
            result);

        var progress =
            new Progress<UpdateDownloadProgress>(
                state =>
                {
                    UpdateProgressBar.Value =
                        state.Percentage;

                    UpdateProgressStateText.Text =
                        state.TotalBytes.HasValue
                            ? $"Descargando paquete... {state.Percentage}%"
                            : $"Descargando paquete... {FormatUpdateBytes(state.BytesReceived)}";
                });

        PreparedUpdate? prepared =
            null;

        try
        {
            prepared =
                await App.Updates.PrepareAsync(
                    result,
                    progress,
                    cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();

            UpdateProgressStateText.Text =
                "Paquete verificado. Preparando reinicio...";
            UpdateProgressBar.Value =
                100;
            UpdateModalSecondaryButton.IsEnabled =
                false;

            App.Updates.LaunchPreparedUpdate(
                prepared);

            await Task.Delay(
                200);

            Environment.Exit(
                0);
        }
        catch (OperationCanceledException)
        {
            App.Updates.TryDeleteWorkspace(
                prepared);

            _isUpdateModalBusy =
                false;
            CloseUpdateModal();
        }
        catch (Exception ex)
        {
            App.Updates.TryDeleteWorkspace(
                prepared);

            SetUpdateModalError(
                ex.Message);
        }
        finally
        {
            if (ReferenceEquals(
                    _updateDownloadCts,
                    cancellation))
            {
                _updateDownloadCts =
                    null;
            }

            cancellation.Dispose();
        }
    }

    private static string FormatReleaseNotesForDisplay(
        string releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(
                releaseNotes))
        {
            return "La Release no incluye notas adicionales.";
        }

        var lines =
            releaseNotes
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Split(
                    '\n');

        var formatted =
            new List<string>(
                lines.Length);

        foreach (var rawLine in lines)
        {
            var line =
                rawLine.Trim();

            while (line.StartsWith(
                       '#'))
            {
                line =
                    line[1..]
                        .TrimStart();
            }

            if (line.StartsWith(
                    "- ",
                    StringComparison.Ordinal))
            {
                line =
                    "• " +
                    line[2..];
            }

            line =
                line.Replace(
                        "**",
                        string.Empty,
                        StringComparison.Ordinal)
                    .Replace(
                        "`",
                        string.Empty,
                        StringComparison.Ordinal);

            formatted.Add(
                line);
        }

        return string.Join(
                Environment.NewLine,
                formatted)
            .Trim();
    }

    private static string FormatUpdateBytes(
        long bytes)
    {
        string[] units =
        [
            "B",
            "KB",
            "MB",
            "GB"
        ];

        var value =
            (double)Math.Max(
                0,
                bytes);
        var index = 0;

        while (value >= 1024 &&
               index <
               units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0
            ? $"{value:0} {units[index]}"
            : $"{value:0.##} {units[index]}";
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
        SetSelectedNavigationButton(button);
    }

    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "home" => typeof(HomePage),
            "organize" => typeof(OrganizePage),
            "search" => typeof(SearchPage),
            "history" => typeof(HistoryPage),
            "categories" => typeof(CategoriesPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void SetSelectedNavigationButton(Button button)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var activeBrush = (Brush)Application.Current.Resources["BandaNavActiveBrush"];
        var activeForeground = (Brush)Application.Current.Resources["BandaTextBrush"];
        var mutedForeground = (Brush)Application.Current.Resources["BandaMutedStrongBrush"];

        foreach (var navButton in new[]
                 {
                     HomeButton,
                     OrganizeButton,
                     SearchButton,
                     HistoryButton,
                     CategoriesButton,
                     SettingsButton
                 })
        {
            navButton.Background = transparent;
            navButton.Foreground = mutedForeground;
        }

        button.Background = activeBrush;
        button.Foreground = activeForeground;
        _selectedNavigationButton = button;
    }
}
