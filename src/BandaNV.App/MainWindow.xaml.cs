using BandaNV.App.Pages;
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



    public void ShowQuickUpdate(string latestVersion)
    {
        QuickUpdateTitle.Text = "Nueva actualización";
        QuickUpdateSubtitle.Text = $"Última versión: {latestVersion}";
        QuickUpdateButton.Visibility = Visibility.Visible;
    }

    public void HideQuickUpdate()
    {
        QuickUpdateButton.Visibility = Visibility.Collapsed;
    }

    private void QuickUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("settings");
        SetSelectedNavigationButton(SettingsButton);

        if (ContentFrame.Content is SettingsPage settingsPage)
        {
            settingsPage.OpenUpdatesSection();
        }
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
