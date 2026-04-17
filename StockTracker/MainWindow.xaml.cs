using StockManager.Services;
using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class MainWindow : Window
    {
	private readonly SKAPI m_api = App.Api;
	public MainWindow()
        {
            InitializeComponent();
	    m_api.OnNotifyKLineData += OnNotifyKLineData;
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
                var detailWindow = new StockDetailWindow
                {
                    Owner = this,
                    DataContext = stock
                };
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

	    BuildDateRangeFor120Bars(symbol, (DataContext as MainWindowViewModel)?.SelectedGlobalKLineInterval, out var startDate, out var endDate);
	    ResolveKLineRequest((DataContext as MainWindowViewModel)?.SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);
	    m_api.SKQuoteLib_RequestKLineAMByDate(symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
	}

	private void OnNotifyKLineData(string bstrStockNo, string bstrData)
	{
	    if (!TryParseKLineData(bstrData, out var candle))
	    {
		return;
	    }

	    Dispatcher.BeginInvoke(new Action(() =>
	    {
		(DataContext as MainWindowViewModel)?.ApplyKLineData(bstrStockNo, candle);
	    }));
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

	private void comboBoxChangeInterval_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
	    if (!(DataContext is MainWindowViewModel vm) || vm.Stocks.Count == 0)
	    {
		return;
	    }

	    ResolveKLineRequest(vm.SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);

	    foreach (var stock in vm.Stocks)
	    {
		BuildDateRangeFor120Bars(stock.Symbol, vm.SelectedGlobalKLineInterval, out var startDate, out var endDate);
		stock.ClearData();
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
		    requiredTradingDays = 120;
		    break;
		case "5分K":
		    requiredTradingDays = (int)Math.Ceiling(30d * 5d / minutesPerDay);
		    break;
		case "3分K":
		    requiredTradingDays = (int)Math.Ceiling(30d * 3d / minutesPerDay);
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
    }
}
