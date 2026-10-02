using PabloDock.Services;

namespace PabloDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\PabloDock.ShowMainWindow");
        using var singleInstance = new Mutex(true, @"Local\PabloDock.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            showRequest.Set();
            return;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var monitorService = new MonitorService();
            var exclusionService = new ProcessExclusionService();
            using var restoreService = new LayoutRestoreService(monitorService);
            using var mainForm = new MainForm(
                new WindowEnumerator(),
                new LayoutCaptureService(monitorService, exclusionService),
                restoreService,
                new ProfileStorageService(),
                new StartupSettingsService(),
                exclusionService,
                args.Contains(StartupSettingsService.StartupArgument,
                    StringComparer.OrdinalIgnoreCase));
            RegisteredWaitHandle? showListener = null;
            void RegisterShowListener(object? _, EventArgs __)
            {
                showListener ??= ThreadPool.RegisterWaitForSingleObject(showRequest,
                    (_, _) =>
                    {
                        try
                        {
                            mainForm.BeginInvoke((MethodInvoker)mainForm.ShowFromSecondInstance);
                        }
                        catch (InvalidOperationException)
                        {
                            // The main window is closing or has closed.
                        }
                    }, null, Timeout.Infinite, false);
            }

            mainForm.HandleCreated += RegisterShowListener;
            if (mainForm.IsHandleCreated)
            {
                RegisterShowListener(mainForm, EventArgs.Empty);
            }
            try
            {
                Application.Run(mainForm);
            }
            finally
            {
                showListener?.Unregister(null);
            }
        }
        finally
        {
            singleInstance.ReleaseMutex();
        }
    }
}
