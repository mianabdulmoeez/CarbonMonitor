using CarbonMonitor.Infrastructure.Logging;
using CarbonMonitor.Infrastructure.SingleInstance;

namespace CarbonMonitor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var guard = new SingleInstanceGuard();
        if (!guard.TryAcquire())
        {
            SingleInstanceGuard.BringExistingInstanceToFront();
            return;
        }

        FileLogger.Instance.Info("Program", "Carbon Monitor starting");

        try
        {
            using var mainForm = new MainForm(guard);
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Program", $"Fatal unhandled exception: {ex}");
            MessageBox.Show(
                $"Carbon Monitor encountered a fatal error:\n{ex.Message}",
                "Carbon Monitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            FileLogger.Instance.Info("Program", "Carbon Monitor exiting");
            FileLogger.Instance.Dispose();
        }
    }
}
