using BandaNV.Core.Services;
using Microsoft.UI.Xaml;

namespace BandaNV.App;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }

    public static SettingsService Settings { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings.LoadAsync().GetAwaiter().GetResult();

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}