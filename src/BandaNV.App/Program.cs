using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace BandaNV.App;

public static class Program
{
#if BANDANV_UPDATER_E2E_BASE || BANDANV_UPDATER_E2E_TARGET
    // Las builds de ensayo no se redirigen a BandaNV oficial.
    private const string InstanceKey = "BandaNV.UpdaterE2ETestInstance";
#else
    private const string InstanceKey = "BandaNV.MainInstance";
#endif

    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var currentInstance =
            AppInstance.GetCurrent();
        var activationArgs =
            currentInstance.GetActivatedEventArgs();

        var mainInstance =
            AppInstance.FindOrRegisterForKey(
                InstanceKey);

        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(
                activationArgs);
            return;
        }

        mainInstance.Activated +=
            (_, _) =>
            {
                App.HandleRedirectedActivation();
            };

        Application.Start(
            initializationCallbackParams =>
            {
                var context =
                    new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());

                SynchronizationContext.SetSynchronizationContext(
                    context);

                _ = new App();
            });
    }
}
