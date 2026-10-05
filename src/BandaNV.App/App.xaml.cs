using BandaNV.Core.Services;
using Microsoft.UI.Xaml;

namespace BandaNV.App;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }

    public static SettingsService Settings { get; } = new();
    public static CategoryService Categories { get; } = new(Settings);
    public static CategoryFolderSyncService CategoryFolders { get; } = new();
    public static OrganizationAnalysisService OrganizationAnalysis { get; } = new();
    public static OrganizationExecutionService OrganizationExecution { get; } = new();
    public static HistoryService History { get; } = new();
    public static SearchIndexService SearchIndex { get; } = new();
    public static SearchFileActionService SearchActions { get; } = new();
    public static UndoService Undo { get; } = new();
    public static RecoveryService Recovery { get; } = new();
    public static UpdateService Updates { get; } = new();

    public static StartupRecoveryResult? LastStartupRecovery { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Settings.LoadAsync();

        try
        {
            LastStartupRecovery =
                await Recovery.RecoverAsync();
        }
        catch
        {
            // La recuperación nunca debe impedir que BandaNV abra.
            // Cualquier journal que no pueda reconciliarse queda intacto
            // para un próximo inicio o una revisión manual.
            LastStartupRecovery = null;
        }

        try
        {
            await History.ApplyRetentionAsync(
                Settings.Current);
        }
        catch
        {
            // La conservación es mantenimiento secundario. Si falla, BandaNV
            // abre normalmente y conserva los archivos para el próximo intento.
        }

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();

        try
        {
            await Updates.ConfirmPendingUpdateAsync(
                Environment.GetCommandLineArgs());
        }
        catch
        {
            // Si falla el handshake, NVupdate hará rollback automáticamente.
        }

        if (Settings.Current.AutoUpdate)
        {
            _ = CheckForUpdatesOnStartupAsync();
        }
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result =
                await Updates.CheckAsync();

            if (result.Status ==
                BandaNV.Core.Models.UpdateCheckStatus.Available)
            {
                MainWindowInstance?.ShowQuickUpdate(
                    result.AvailableVersion);
            }
        }
        catch
        {
            // La red o GitHub nunca deben bloquear ni ensuciar el inicio.
        }
    }
}