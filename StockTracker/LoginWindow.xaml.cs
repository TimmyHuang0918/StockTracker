using StockTracker.Models;
using StockTracker.Services;
using StockTracker.ViewModels;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class LoginWindow : Window
    {
        private CapitalApiService _capitalApiService;
        private MainWindowViewModel _mainWindowViewModel;
        private bool _hasSavedCredentials;
        private string CredentialFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "login.dat");

        public LoginWindow()
        {
            InitializeComponent();
            var vm = new LoginViewModel(new CapitalApiService());
            vm.LoginSucceeded += OnLoginSucceeded;
            DataContext = vm;
            LoadSavedCredentials();

            if (_hasSavedCredentials || App.IsNightlyAutomationRestart || App.IsManualScanPublish || App.IsSchedulerAutoLogin)
            {
                Loaded += async (s, e) => await TryAutoLoginAsync();
            }
        }
        private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.Password = PasswordInput.Password;
            }
        }

        private async void OnLoginSucceeded(CapitalApiService apiService)
        {
            if (DataContext is LoginViewModel loginVm)
            {
                PersistCredentials(loginVm.Account?.Trim() ?? string.Empty, loginVm.Password ?? string.Empty);
            }

            _capitalApiService = apiService;
            _capitalApiService.KLineDataReceived += OnNotifyKLineData;

            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(apiService)
            };
            mainWindow.Show();

            if (mainWindow.DataContext is MainWindowViewModel vm)
            {
                _mainWindowViewModel = vm;
                await vm.InitializeAsync();
                RequestInitialKLineData(vm.SelectedGlobalKLineInterval);
            }
            Close();
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_capitalApiService != null)
            {
                _capitalApiService.KLineDataReceived -= OnNotifyKLineData;
            }

            base.OnClosed(e);
        }

        private void LoadSavedCredentials()
        {
            if (!File.Exists(CredentialFilePath) || !(DataContext is LoginViewModel vm))
            {
                return;
            }

            try
            {
                var base64 = File.ReadAllText(CredentialFilePath, Encoding.UTF8);
                var rawBytes = Convert.FromBase64String(base64);
                var usedLegacyEncoding = false;
                byte[] unprotectedBytes;
                try
                {
                    unprotectedBytes = ProtectedData.Unprotect(rawBytes, null, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    // 相容舊版 Base64 檔案；讀取成功後立即升級為 Windows DPAPI 加密。
                    unprotectedBytes = rawBytes;
                    usedLegacyEncoding = true;
                }

                var rawText = Encoding.UTF8.GetString(unprotectedBytes);
                var parts = rawText.Split(new[] { '\n' }, 2);
                if (parts.Length < 2)
                {
                    return;
                }

                vm.Account = parts[0];
                vm.Password = parts[1];
                PasswordInput.Password = parts[1];
                RememberCredentialsCheckBox.IsChecked = true;
                _hasSavedCredentials = true;

                if (usedLegacyEncoding)
                {
                    PersistCredentials(vm.Account, vm.Password);
                }
            }
            catch
            {
                RememberCredentialsCheckBox.IsChecked = false;
                _hasSavedCredentials = false;
            }
        }

        private void PersistCredentials(string account, string password)
        {
            if (RememberCredentialsCheckBox.IsChecked == true)
            {
                var dir = Path.GetDirectoryName(CredentialFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var rawText = $"{account}\n{password}";
                var rawBytes = Encoding.UTF8.GetBytes(rawText);
                var protectedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
                var base64 = Convert.ToBase64String(protectedBytes);
                File.WriteAllText(CredentialFilePath, base64, Encoding.UTF8);
                _hasSavedCredentials = true;
                return;
            }

            if (File.Exists(CredentialFilePath))
            {
                File.Delete(CredentialFilePath);
            }
            _hasSavedCredentials = false;
        }

        private void OnNotifyKLineData(string symbol, CandleData candle)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _mainWindowViewModel?.ApplyKLineData(symbol, candle);
            }));
        }

        private void RequestInitialKLineData(string interval)
        {
            if (_mainWindowViewModel == null)
            {
                return;
            }

            ResolveKLineRequest(interval, out var kLineType, out var minuteNumber);
            foreach (var stock in _mainWindowViewModel.Stocks)
            {
                MainWindow.BuildDateRangeForBars(interval, 300, out var startDate, out var endDate);
                _capitalApiService?.RequestKLineByDate(stock.Symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
            }
        }

        private static void ResolveKLineRequest(string interval, out short kLineType, out short minuteNumber)
        {
            switch (interval)
            {
                case "5分K":
                    kLineType = 0;
                    minuteNumber = 5;
                    break;
                case "3分K":
                    kLineType = 0;
                    minuteNumber = 3;
                    break;
                case "日K":
                    kLineType = 4;
                    minuteNumber = 0;
                    break;
                default:
                    kLineType = 4;
                    minuteNumber = 1;
                    break;
            }
        }

        private async System.Threading.Tasks.Task TryAutoLoginAsync()
        {
            if (!(DataContext is LoginViewModel vm))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.Account) || string.IsNullOrWhiteSpace(vm.Password))
            {
                vm.StatusMessage = "無法自動登入：未儲存帳號密碼";
                return;
            }

            vm.StatusMessage = App.IsNightlyAutomationRestart || App.IsManualScanPublish || App.IsSchedulerAutoLogin
                ? "排程：自動登入中..."
                : "自動登入中...";
            await System.Threading.Tasks.Task.Delay(350);

            if (vm.LoginCommand.CanExecute(null))
            {
                vm.LoginCommand.Execute(null);
            }
        }

    }
}
