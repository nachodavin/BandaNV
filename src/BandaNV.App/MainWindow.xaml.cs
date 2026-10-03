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
