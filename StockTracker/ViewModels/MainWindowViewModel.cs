using StockTracker.Services;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private string _newSymbol;
        private string _systemMessage;
        private string _selectedGlobalKLineInterval = "日K";
        private string _selectedGlobalKLineCount = "120";
	public MainWindowViewModel(CapitalApiService apiService)
        {
            _apiService = apiService;
            Stocks = new ObservableCollection<StockViewModel>();
            SubscribeCommand = new RelayCommand(async _ => await SubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbolRelativeName));
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
        public ICommand ExportCsvCommand { get; }

        public async Task InitializeAsync()
        {
            await AddOrSubscribeAsync("2330", "台積電");
            await AddOrSubscribeAsync("2317", "鴻海");
            await AddOrSubscribeAsync("0050", "元大台灣50");
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
        }

        private void StockVmOnSignalTriggered(StockViewModel stock, string signal)
        {
            var message = $"{DateTime.Now:HH:mm:ss} {stock.Symbol} {stock.Name} -> {signal}";
            SystemMessage = message;
        }
    }
}
