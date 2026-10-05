using BandaNV.Core.Services;
using Microsoft.UI.Xaml;

namespace BandaNV.App;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }

    public static SettingsService Settings { get; } = new();
    public static CategoryService Categories { get; } = new(Settings);
    public static OrganizationAnalysisService OrganizationAnalysis { get; } = new();
    public static OrganizationExecutionService OrganizationExecution { get; } = new();
    public static HistoryService History { get; } = new();
    public static SearchIndexService SearchIndex { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Settings.LoadAsync();

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}