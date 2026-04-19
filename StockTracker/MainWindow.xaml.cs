using StockTracker.Models;
using StockTracker.Services;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class MainWindow : Window
    {
	private CapitalApiService _apiService;
	public MainWindow()
        {
            InitializeComponent();
	    DataContextChanged += MainWindow_DataContextChanged;
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            DragMove();
        }

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void StockList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                var detailVm = stock.CreateDetailViewModel();
                var detailWindow = new StockDetailWindow
                {
                    Owner = this,
                    DataContext = detailVm
                };
                detailWindow.Closed += (_, __) => stock.DetachDetailViewModel(detailVm);
                detailWindow.Show();
            }
        }

	private void btnSubscribe_Click(object sender, RoutedEventArgs e)
	{
	    var symbol = textBoxSubscribe.Text?.Trim();
	    if (string.IsNullOrWhiteSpace(symbol))
	    {
		return;
	    }

	    if (DataContext is MainWindowViewModel vm)
	    {
		vm.NewSymbol = symbol;
		if (vm.SubscribeCommand.CanExecute(null))
		{
		    vm.SubscribeCommand.Execute(null);
		}
	    }

	    BuildDateRangeForBars(symbol, (DataContext as MainWindowViewModel)?.SelectedGlobalKLineInterval, 120, out var startDate, out var endDate);
	    ResolveKLineRequest((DataContext as MainWindowViewModel)?.SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);
	    _apiService?.RequestKLineByDate(symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
	}

	private void comboBoxChangeInterval_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
	    if (!(DataContext is MainWindowViewModel vm) || vm.Stocks.Count == 0)
	    {
		return;
	    }

	    ResolveKLineRequest(vm.SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);

	    foreach (var stock in vm.Stocks)
	    {
		int.TryParse(vm.SelectedGlobalKLineCount, out var kLineCount);
		BuildDateRangeForBars(stock.Symbol, vm.SelectedGlobalKLineInterval, kLineCount, out var startDate, out var endDate);
		stock.ClearData();
		_apiService?.RequestKLineByDate(stock.Symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
	    }
	}

	private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
	    if (_apiService != null)
	    {
		_apiService.KLineDataReceived -= ApiService_OnKLineDataReceived;
		_apiService.InstantCandleReceived -= ApiService_OnInstantCandleReceived;
	    }

	    _apiService = (DataContext as MainWindowViewModel)?.ApiService;
	    if (_apiService != null)
	    {
		_apiService.KLineDataReceived += ApiService_OnKLineDataReceived;
		_apiService.InstantCandleReceived += ApiService_OnInstantCandleReceived;
	    }
	}

	private void ApiService_OnKLineDataReceived(string symbol, CandleData candle)
	{
	    Dispatcher.BeginInvoke(new Action(() =>
	    {
		(DataContext as MainWindowViewModel)?.ApplyKLineData(symbol, candle);
	    }));
	}

	private void ApiService_OnInstantCandleReceived(string symbol, CandleData candle)
	{
	    Dispatcher.BeginInvoke(new Action(() =>
	    {
		if (!(DataContext is MainWindowViewModel vm) || candle.Close == 0)
		{
		    return;
		}

		var normalized = symbol?.Trim() ?? string.Empty;
		var stock = vm.Stocks.FirstOrDefault(x =>
		    string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase) ||
		    normalized.EndsWith(x.Symbol, StringComparison.OrdinalIgnoreCase));
		stock?.ApplyInstantCandle(candle, stock.SelectedKLineInterval);
	    }));
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

	public static void BuildDateRangeForBars(string symbol, string interval, int requiredBars, out string startDate, out string endDate)
	{
	    // 每日交易分鐘數 (例如台股 9:00~13:30 = 270 分鐘)
	    var minutesPerDay = 270;

	    // 每日可產生多少 K 線
	    int barsPerDay;
	    switch (interval)
	    {
		case "日K":
		    barsPerDay = 1;
		    break;
		case "5分K":
		    barsPerDay = minutesPerDay / 5;
		    break;
		case "3分K":
		    barsPerDay = minutesPerDay / 3;
		    break;
		case "1分K":
		default:
		    barsPerDay = minutesPerDay / 1;
		    break;
	    }

	    // 需要多少交易日
	    int requiredTradingDays = (int)Math.Ceiling((double)requiredBars / barsPerDay);

	    // 取得最近交易日清單 (你需要自己實作，例如從交易所日曆或 API)
	    List<DateTime> tradingDays = GetRecentTradingDays(requiredTradingDays);

	    // 起始日是最早的交易日，結束日是今天或最近交易日
	    startDate = tradingDays.First().ToString("yyyyMMdd");
	    endDate = tradingDays.Last().ToString("yyyyMMdd");
	}

	// 範例：模擬取得最近交易日清單
	private static List<DateTime> GetRecentTradingDays(int requiredDays)
	{
	    var days = new List<DateTime>();
	    var current = DateTime.Today;
	    while (days.Count < requiredDays)
	    {
		if (IsTradingDay(current))
		{
		    days.Insert(0, current); // 往前插入
		}
		current = current.AddDays(-1);
	    }
	    return days;
	}

	// 判斷是否為交易日 (簡單版：排除週六週日)
	private static bool IsTradingDay(DateTime date)
	{
	    return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
	}

	private void ChartViewbox_OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
	    if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
	    {
		stock.UpdateDisplayCapacity(e.NewSize.Width);
	    }
	}

	private void KLineCanvas_OnMouseMove(object sender, MouseEventArgs e)
	{
	    if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
	    {
		var point = e.GetPosition((IInputElement)sender);
		stock.UpdateCrosshair(point.X, point.Y);
	    }
	}

	private void KLineCanvas_OnMouseLeave(object sender, MouseEventArgs e)
	{
	    if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
	    {
		stock.ClearCrosshair();
	    }
	}
    }
}
