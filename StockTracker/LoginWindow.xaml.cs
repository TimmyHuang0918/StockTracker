using SKCOMLib;
using StockManager.Services;
using StockTracker.Services;
using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class LoginWindow : Window
    {
	private readonly SKAPI m_api = App.Api;
	private bool _isSkEventsRegistered = false;
	private bool _isSkQuoteConnectionReady;
	private TaskCompletionSource<bool> _quoteConnectionReadyTcs;
	private MainWindowViewModel _mainWindowViewModel;
	private string CredentialFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "login.dat");

	public LoginWindow()
        {
            InitializeComponent();

            var vm = new LoginViewModel(new FakeCapitalApiService());
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

        private async void OnLoginSucceeded(FakeCapitalApiService apiService)
        {
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

	private async void btnLogin_Click(object sender, RoutedEventArgs e)
	{
	    if (!(DataContext is LoginViewModel vm))
	    {
		return;
	    }

	    var loginId = vm.Account?.Trim();
	    var password = vm.Password ?? string.Empty;
	    if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
	    {
		vm.StatusMessage = "請輸入帳號與密碼";
		return;
	    }

	    RegisterSkEventsIfNeeded();
	    vm.StatusMessage = "登入中...";
	    var resultCode = m_api.SKCenterLib_Login(loginId, password);
	    Console.WriteLine($"登入結果: {resultCode}");

	    if (resultCode == 0)
	    {
		PersistCredentials(loginId, password);

		vm.StatusMessage = "登入成功";
		var nCode = m_api.SKQuoteLib_EnterMonitorLONG();
		if (nCode != 0)
		{
		    var monitorMessage = m_api.SKCenterLib_GetReturnCodeMessage(nCode);
		    vm.StatusMessage = $"報價連線啟動失敗({nCode}) {monitorMessage}";
		    return;
		}

		vm.StatusMessage = "等待報價伺服器...";
		var connected = await WaitForQuoteConnectionReadyAsync(15000);
		if (!connected)
		{
		    vm.StatusMessage = "報價伺服器連線逾時，請稍後重試";
		    return;
		}

		OnLoginSucceeded(new FakeCapitalApiService());
		return;
	    }


	    var message = m_api.SKCenterLib_GetReturnCodeMessage(resultCode);
	    vm.StatusMessage = $"登入失敗({resultCode}) {message}";
	}


	private void RegisterSkEventsIfNeeded()
	{
	    if (_isSkEventsRegistered)
	    {
		return;
	    }

	    m_api.OnReplyMessage += OnAnnouncement;

	    m_api.OnConnection += (nKind, code) =>
	    {
		Console.WriteLine($"[OnConnection] nKind={nKind}, code={code}");

		if (nKind == 3003)
		{
		    _isSkQuoteConnectionReady = true;
		    _quoteConnectionReadyTcs?.TrySetResult(true);
		}
	    };

	    m_api.OnNotifyQuoteLONG += (nMarketNo, nIndex) =>
	    {
		var skStock = new SKSTOCKLONG();
		var code = m_api.SKQuoteLib_GetStockByIndexLONG(nMarketNo, nIndex, ref skStock);
		if (code == 0)
		{
		    
		}
	    };

	    m_api.OnNotifyKLineData += OnNotifyKLineData;

	    _isSkEventsRegistered = true;
	}

	private async Task<bool> WaitForQuoteConnectionReadyAsync(int timeoutMs)
	{
	    if (_isSkQuoteConnectionReady)
	    {
		return true;
	    }

	    _quoteConnectionReadyTcs = new TaskCompletionSource<bool>();
	    var completedTask = await Task.WhenAny(_quoteConnectionReadyTcs.Task, Task.Delay(timeoutMs));
	    return completedTask == _quoteConnectionReadyTcs.Task && _quoteConnectionReadyTcs.Task.Result;
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

	private void OnAnnouncement(string strUserID, string bstrMessage, out short nConfirmCode)
	{
	    nConfirmCode = -1;
	}

	private void OnNotifyKLineData(string bstrStockNo, string bstrData)
	{
	    if (!TryParseKLineData(bstrData, out var candle))
	    {
		return;
	    }

	    Dispatcher.BeginInvoke(new Action(() =>
	    {
		_mainWindowViewModel?.ApplyKLineData(bstrStockNo, candle);
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
		m_api.SKQuoteLib_RequestKLineAMByDate(stock.Symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
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

	private static void BuildDateRangeFor120Bars(string symbol, string interval, out string startDate, out string endDate)
	{
	    var minutesPerDay = ResolveTradingMinutesPerDay(symbol);
	    int requiredTradingDays;
	    switch (interval)
	    {
		case "日K":
		    requiredTradingDays = 30;
		    break;
		case "5分K":
		    requiredTradingDays = (int)Math.Ceiling(30 * 5d / minutesPerDay);
		    break;
		case "3分K":
		    requiredTradingDays = (int)Math.Ceiling(30 * 3d / minutesPerDay);
		    break;
		default:
		    requiredTradingDays = (int)Math.Ceiling(30d * 1d / minutesPerDay);
		    break;
	    }

	    var calendarLookbackDays = Math.Max(5, (int)Math.Ceiling(requiredTradingDays * 7d / 5d) + 5);
	    startDate = DateTime.Today.AddDays(-calendarLookbackDays).ToString("yyyyMMdd");
	    endDate = DateTime.Today.ToString("yyyyMMdd");
	}

	private static int ResolveTradingMinutesPerDay(string symbol)
	{
	    if (!string.IsNullOrWhiteSpace(symbol) && symbol.Length == 4 && char.IsDigit(symbol[0]))
	    {
		return 240;
	    }

	    return 300;
	}

	private static bool TryParseKLineData(string raw, out CandleData candle)
	{
	    candle = null;
	    if (string.IsNullOrWhiteSpace(raw))
	    {
		return false;
	    }

	    var parts = raw.Split(',');
	    if (parts.Length < 6)
	    {
		return false;
	    }

	    string timeText;
	    int valueStartIndex;
	    if (parts.Length >= 7)
	    {
		timeText = $"{parts[0].Trim()} {parts[1].Trim()}";
		valueStartIndex = 2;
	    }
	    else
	    {
		timeText = parts[0].Trim();
		valueStartIndex = 1;
	    }

	    if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
	    {
		return false;
	    }

	    if (!decimal.TryParse(parts[valueStartIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out var open) ||
		!decimal.TryParse(parts[valueStartIndex + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var high) ||
		!decimal.TryParse(parts[valueStartIndex + 2], NumberStyles.Any, CultureInfo.InvariantCulture, out var low) ||
		!decimal.TryParse(parts[valueStartIndex + 3], NumberStyles.Any, CultureInfo.InvariantCulture, out var close) ||
		!long.TryParse(parts[valueStartIndex + 4], NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
	    {
		return false;
	    }

	    candle = new CandleData
	    {
		Time = time,
		Open = open,
		High = high,
		Low = low,
		Close = close,
		Volume = volume
	    };

	    return true;
	}
    }
}
