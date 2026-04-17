using System.Windows;
using StockManager.Services;

namespace StockTracker
{
    public partial class App : Application
    {
        public static SKAPI Api { get; } = SKAPI.Instance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}
