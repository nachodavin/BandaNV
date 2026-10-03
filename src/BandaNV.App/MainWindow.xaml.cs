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

    public MainWindow()
    {
        InitializeComponent();

        Title = "BandaNV";
        _selectedNavigationButton = HomeButton;
        ContentFrame.Navigate(typeof(HomePage));

        Activated += MainWindow_Activated;
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


    private void QuickUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        // Hasta integrar el motor de actualizaciones de v2.0, este acceso rápido
        // lleva a Configuración. Más adelante el mismo bloque reflejará estados
        // como actualización disponible, descargando y reinicio pendiente.
        NavigateTo("settings");
        SetSelectedNavigationButton(SettingsButton);
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
