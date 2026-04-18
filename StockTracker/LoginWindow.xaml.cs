using SKCOMLib;
using StockTracker.Services;
using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class LoginWindow : Window
    {
	private CapitalApiService _capitalApiService;
	private MainWindowViewModel _mainWindowViewModel;
	private string CredentialFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "login.dat");

	public LoginWindow()
        {
            InitializeComponent();

            var vm = new LoginViewModel(new CapitalApiService());
            vm.LoginSucceeded += OnLoginSucceeded;
            DataContext = vm;
	    LoadSavedCredentials();
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
		var rawText = Encoding.UTF8.GetString(rawBytes);
		var parts = rawText.Split(new[] { '\n' }, 2);
		if (parts.Length < 2)
		{
		    return;
		}

		vm.Account = parts[0];
		vm.Password = parts[1];
		PasswordInput.Password = parts[1];
		RememberCredentialsCheckBox.IsChecked = true;
	    }
	    catch
	    {
		RememberCredentialsCheckBox.IsChecked = false;
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
		var base64 = Convert.ToBase64String(rawBytes);
		File.WriteAllText(CredentialFilePath, base64, Encoding.UTF8);
		return;
	    }

	    if (File.Exists(CredentialFilePath))
	    {
		File.Delete(CredentialFilePath);
	    }
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
		MainWindow.BuildDateRangeForBars(stock.Symbol, interval, 120, out var startDate, out var endDate);
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

    }
}
