using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

namespace StockTracker.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private string _newSymbol;
        private string _systemMessage;
        private string _selectedGlobalKLineInterval = "日K";
        private string _selectedGlobalKLineCount = "120";
	private string SubscriptionFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "subscriptions.txt");
	public MainWindowViewModel(CapitalApiService apiService)
        {
            _apiService = apiService;
            Stocks = new ObservableCollection<StockViewModel>();
            SubscribeCommand = new RelayCommand(async _ => await SubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbolRelativeName));
	    UnsubscribeCommand = new RelayCommand(async _ => await UnsubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbol));
        }

        public ObservableCollection<StockViewModel> Stocks { get; }
        public CapitalApiService ApiService => _apiService;
        public IReadOnlyList<string> GlobalKLineIntervals { get; } = new[] { "日K", "5分K", "3分K", "1分K" };
	public IReadOnlyList<string> GlobalKLineCount { get; } = new[] { "30","60", "120", "150","240" };

	public string SelectedGlobalKLineInterval
        {
            get => _selectedGlobalKLineInterval;
            set
            {
                if (_selectedGlobalKLineInterval == value || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                _selectedGlobalKLineInterval = value;
                OnPropertyChanged();

                foreach (var stock in Stocks)
                {
                    stock.SelectedKLineInterval = value;
                }
            }
        }


	public string SelectedGlobalKLineCount
	{
	    get => _selectedGlobalKLineCount;
	    set
	    {
		if (_selectedGlobalKLineCount == value || string.IsNullOrWhiteSpace(value))
		{
		    return;
		}

		_selectedGlobalKLineCount = value;
		OnPropertyChanged();

		foreach (var stock in Stocks)
		{
		    stock.SelectedKLineCount = value;
		}
	    }
	}

	public string SystemMessage
        {
            get => _systemMessage;
            set
            {
                _systemMessage = value;
                OnPropertyChanged();
            }
        }

        public string NewSymbol
        {
            get => _newSymbol;
            set
            {
                _newSymbol = value;
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(NewSymbolRelativeName));
	    }
        }

        public string NewSymbolRelativeName
        {
            get
            {
                var stockInfo =  _apiService.GetRelativeStockMessage(_newSymbol);
                var isEmpty = stockInfo.bstrStockName == "";
		return isEmpty ? "None" : stockInfo.bstrStockName;
	    }
	}

        public ICommand SubscribeCommand { get; }
        public ICommand UnsubscribeCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public async Task InitializeAsync()
        {
	    var savedSymbols = LoadSavedSubscriptions();
	    if (savedSymbols.Count == 0)
	    {
		savedSymbols.Add("2330");
		savedSymbols.Add("2317");
		savedSymbols.Add("0050");
	    }
            List<string> symbols = new List<string>();
            List<string> names = new List<string>();
	    foreach (var symbol in savedSymbols)
	    {
                symbols.Add(symbol);
                names.Add(_apiService.GetRelativeStockMessage(symbol).bstrStockName);
	    }
	    await AddOrSubscribeAsync(symbols, names);
	}

        public void ApplyKLineData(string symbol, CandleData candle)
        {
            var normalized = symbol?.Trim() ?? string.Empty;
            var vm = Stocks.FirstOrDefault(x =>
                string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(x.Symbol, StringComparison.OrdinalIgnoreCase));
            vm?.UpdateFromKLine(candle);
        }

        private async Task SubscribeSymbolAsync()
        {
            var symbol = NewSymbol?.Trim();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            await AddOrSubscribeAsync(symbol, NewSymbolRelativeName);
            NewSymbol = string.Empty;
	    SaveSubscriptions();
        }

	private async Task UnsubscribeSymbolAsync()
	{
	    var symbol = NewSymbol?.Trim();
	    if (string.IsNullOrWhiteSpace(symbol))
	    {
		return;
	    }

	    var target = Stocks.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
	    if (target == null)
	    {
		return;
	    }

	    Stocks.Remove(target);
	    await _apiService.UnsubscribeAsync(symbol);
	    SaveSubscriptions();
	    NewSymbol = string.Empty;
	}

        private async Task AddOrSubscribeAsync(string symbol, string name)
        {
            if (Stocks.Any(x => x.Symbol == symbol))
            {
                return;
            }

            await _apiService.SubscribeAsync(symbol);
            var stockVm = new StockViewModel(symbol, name);
            stockVm.SelectedKLineInterval = SelectedGlobalKLineInterval;
            stockVm.SignalTriggered += StockVmOnSignalTriggered;
            Stocks.Add(stockVm);
	    SaveSubscriptions();
        }

        private async Task AddOrSubscribeAsync(List<string> symbolList, List<string> nameList)
        {
            int count = symbolList.Count;
            if (count  != nameList.Count)
                return;

            List<Tuple<string, string>> applyStocks = new List<Tuple<string, string>>();
	    for (int symbolIndex = 0; symbolIndex < count; symbolIndex++)
            {
		if (Stocks.Any(x => x.Symbol == symbolList[symbolIndex]))
		{
                    continue;
		}
                applyStocks.Add(new Tuple<string, string>(symbolList[symbolIndex], nameList[symbolIndex]));
	    }

            string subScribeString = string.Join(",", from s in applyStocks select s.Item1);
	    await _apiService.SubscribeAsync(subScribeString);

            foreach(var eachStock in applyStocks)
            {
		var stockVm = new StockViewModel(eachStock.Item1, eachStock.Item2);
		stockVm.SelectedKLineInterval = SelectedGlobalKLineInterval;
		stockVm.SignalTriggered += StockVmOnSignalTriggered;
		Stocks.Add(stockVm);
	    }
	    SaveSubscriptions();
	}


	private void StockVmOnSignalTriggered(StockViewModel stock, string signal)
        {
            var message = $"{DateTime.Now:HH:mm:ss} {stock.Symbol} {stock.Name} -> {signal}";
            SystemMessage = message;
        }

	private List<string> LoadSavedSubscriptions()
	{
	    if (!File.Exists(SubscriptionFilePath))
	    {
		return new List<string>();
	    }

	    return File.ReadAllLines(SubscriptionFilePath, Encoding.UTF8)
		.Select(x => x.Trim())
		.Where(x => !string.IsNullOrWhiteSpace(x))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToList();
	}

	private void SaveSubscriptions()
	{
	    var dir = Path.GetDirectoryName(SubscriptionFilePath);
	    if (!Directory.Exists(dir))
	    {
		Directory.CreateDirectory(dir);
	    }

	    var lines = Stocks.Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	    File.WriteAllLines(SubscriptionFilePath, lines, Encoding.UTF8);
	}
    }
}
