using StockManager.Services;
using System.Linq;
using System.Windows;

namespace StockTracker
{
    public partial class App : Application
    {
        public static SKAPI Api { get; } = SKAPI.Instance;

        public static bool IsNightlyAutomationRestart { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            IsNightlyAutomationRestart = e.Args.Contains("--nightly-automation");

            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}
