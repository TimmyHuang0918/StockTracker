using StockManager.Services;
using System.Linq;
using System.Windows;

namespace StockTracker
{
    public partial class App : Application
    {
        public static SKAPI Api { get; } = SKAPI.Instance;

        public static bool IsNightlyAutomationRestart { get; private set; }
        public static bool IsManualScanPublish { get; private set; }
        public static bool IsSchedulerAutoLogin { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            IsNightlyAutomationRestart = e.Args.Contains("--nightly-automation");
            IsManualScanPublish = e.Args.Contains("--scan-and-publish");
            IsSchedulerAutoLogin = e.Args.Contains("--scheduler-autologin");

            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}
