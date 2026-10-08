using BandaNV.App.Services;
using BandaNV.Core.Services;
using Microsoft.UI.Xaml;

namespace BandaNV.App;

public partial class App : Application
{
    private static int _bringToFrontPending;

    public static MainWindow? MainWindowInstance { get; private set; }

    public static SettingsService Settings { get; } = new();
    public static CategoryService Categories { get; } = new(Settings);
    public static CategoryFolderSyncService CategoryFolders { get; } = new();
    public static OrganizationAnalysisService OrganizationAnalysis { get; } = new();
    public static OrganizationExecutionService OrganizationExecution { get; } = new();
    public static OrganizationSourceActionService OrganizationSourceActions { get; } = new();
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

    public static void HandleRedirectedActivation()
    {
        var window =
            MainWindowInstance;

        if (window is null)
        {
            Interlocked.Exchange(
                ref _bringToFrontPending,
                1);
            return;
        }

        window.DispatcherQueue.TryEnqueue(
            window.BringToFront);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Settings.LoadAsync();

        AppearanceService.ApplySettings(
            Settings.Current);

        try
        {
            var trackedOrphansBefore =
                Settings.Current.OrphanedCategoryFolders.Count;

            var deletedUnusedCategoryFolders =
                await CategoryFolders.CleanupUnusedCategoryFoldersAsync(
                    Settings.Current);

            if (deletedUnusedCategoryFolders > 0 ||
                Settings.Current.OrphanedCategoryFolders.Count !=
                    trackedOrphansBefore)
            {
                await Settings.SaveAsync(
                    Settings.Current);
            }
        }
        catch
        {
            // La limpieza de carpetas huérfanas nunca debe impedir el inicio.
        }

        try
        {
            WindowsStartupService.Apply(
                Settings.Current.StartWithWindows);
        }
        catch
        {
            // El inicio automático es integración secundaria con Windows.
            // Si el registro no está disponible, BandaNV abre normalmente.
        }

        try
        {
            LastStartupRecovery =
                await Recovery.RecoverAsync(
                    Settings.Current);
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

        if (Interlocked.Exchange(
                ref _bringToFrontPending,
                0) == 1)
        {
            MainWindowInstance.BringToFront();
        }

        try
        {
            await Updates.ConfirmPendingUpdateAsync(
                Environment.GetCommandLineArgs());
        }
        catch
        {
            // Si falla el handshake, NVupdate hará rollback automáticamente.
        }

        _ = Updates.CleanupStaleRunnerDirectoriesAsync();
        _ = CheckForUpdatesOnStartupAsync();
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
                MainWindowInstance?.SetAvailableUpdate(
                    result,
                    showModal:
                        Settings.Current.AutoUpdate);
            }
            else if (result.Status is
                     BandaNV.Core.Models.UpdateCheckStatus.Current or
                     BandaNV.Core.Models.UpdateCheckStatus.LocalNewer or
                     BandaNV.Core.Models.UpdateCheckStatus.FailedSuppressed)
            {
                MainWindowInstance?.ClearAvailableUpdate();
            }
        }
        catch
        {
            // La red o GitHub nunca deben bloquear ni ensuciar el inicio.
        }
    }
}