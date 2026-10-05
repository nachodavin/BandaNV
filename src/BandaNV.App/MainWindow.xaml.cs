using BandaNV.App.Pages;
using BandaNV.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private Button? _selectedNavigationButton;
    private bool _xamlRootChangedHooked;
    private bool _layoutRefreshQueued;
    private CancellationTokenSource? _updateDownloadCts;
    private UpdateCheckResult? _availableUpdate;
    private bool _isUpdateModalBusy;

    public MainWindow()
    {
        InitializeComponent();

        Title = "BandaNV";
        _selectedNavigationButton = HomeButton;
        ContentFrame.Navigate(typeof(HomePage));

        Activated += MainWindow_Activated;
        ContentFrame.Loaded += ContentFrame_Loaded;
        ContentFrame.SizeChanged += ContentFrame_SizeChanged;
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
            ConfigureTitleBar();

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


    private void ConfigureTitleBar()
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 9, 13, 18);
        titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 160, 170);
        titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 13, 18);

        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 13, 18);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 28, 38, 47);
        titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 37, 49, 60);
        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 160, 170);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 13, 18);
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

    public void ShowQuickUpdate(
        string latestVersion)
    {
        QuickUpdateTitle.Text =
            "Nueva versión";
        QuickUpdateSubtitle.Text =
            $"Disponible: {latestVersion}";
        QuickUpdateButton.Visibility =
            Visibility.Visible;
    }

    public void HideQuickUpdate()
    {
        QuickUpdateButton.Visibility =
            Visibility.Collapsed;
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

        UpdateModalOverlay.Visibility =
            Visibility.Visible;
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
