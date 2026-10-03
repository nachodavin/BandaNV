using BandaNV.App.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BandaNV.App;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;

    public MainWindow()
    {
        InitializeComponent();

        Title = "BandaNV";
        Navigation.SelectedItem = HomeItem;
        ContentFrame.Navigate(typeof(HomePage));

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

    private void Navigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

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
}
