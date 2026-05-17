using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace StockTracker.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private readonly TwseT86Repository _twseT86Repository;
        private readonly TwseMarginRepository _twseMarginRepository;
        private readonly List<TwseT86History> _twseT86Histories = new List<TwseT86History>();
        private readonly List<TwseMarginHistory> _twseMarginHistories = new List<TwseMarginHistory>();
        private string _newSymbol;
        private string _systemMessage;
        private string _selectedGlobalKLineInterval = "日K";
        private string _selectedGlobalKLineCount = "300";
        private TwseT86Record _latestTwseT86Record;
        private DateTime _latestRankingDate;
        private DateTime _twseHistoryStartDate = DateTime.Today;
        private bool _isUpdatingTwseHistory;
        private string _updatingTwseText;
        private string _rankingFilter = "全部顯示";
        private ICollectionView _filteredTwseRankingItems;
        private string SubscriptionFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "subscriptions.txt");
        public MainWindowViewModel(CapitalApiService apiService)
        {
            _apiService = apiService;
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "twse_t86.db");
            _twseT86Repository = new TwseT86Repository(dbPath);

            var marginDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "twse_margin.db");
            _twseMarginRepository = new TwseMarginRepository(marginDbPath);

            Stocks = new ObservableCollection<StockViewModel>();
            TwseRankingItems = new ObservableCollection<TwseT86RankingItem>();
            SubscribeCommand = new RelayCommand(async _ => await SubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbolRelativeName));
            UnsubscribeCommand = new RelayCommand(async _ => await UnsubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbol));
            UpdateTwseHistoryCommand = new RelayCommand(async _ => await UpdateTwseHistoryAsync(), _ => !IsUpdatingTwseHistory);
        }

        public ICommand UpdateTwseHistoryCommand { get; }

        public DateTime TwseHistoryStartDate
        {
            get => _twseHistoryStartDate;
            set
            {
                _twseHistoryStartDate = value;
                OnPropertyChanged();
            }
        }

        public bool IsUpdatingTwseHistory
        {
            get => _isUpdatingTwseHistory;
            private set
            {
                _isUpdatingTwseHistory = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpdateTwseHistoryButtonText));
            }
        }

        public string UpdatingTwseText
        {
            get => _updatingTwseText;
            set
            {
                _updatingTwseText = value;
                OnPropertyChanged();
            }
        }

        public string UpdateTwseHistoryButtonText => IsUpdatingTwseHistory ? "更新中…" : "更新資料";

        public ObservableCollection<TwseT86RankingItem> TwseRankingItems { get; }

        public ICollectionView FilteredTwseRankingItems
        {
            get => _filteredTwseRankingItems;
            private set
            {
                _filteredTwseRankingItems = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> RankingFilterOptions { get; } = new[]
        {
            "全部顯示", "上市櫃（4碼）", "上市櫃（5碼）", "僅上市", "僅上櫃"
        };

        public string RankingFilter
        {
            get => _rankingFilter;
            set
            {
                if (_rankingFilter == value) return;
                _rankingFilter = value;
                OnPropertyChanged();
                ApplyRankingFilter();
            }
        }

        public DateTime LatestRankingDate
        {
            get => _latestRankingDate;
            private set
            {
                _latestRankingDate = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<StockViewModel> Stocks { get; }
        public CapitalApiService ApiService => _apiService;
        public IReadOnlyList<string> GlobalKLineIntervals { get; } = new[] { "日K", "5分K", "3分K", "1分K" };
        public IReadOnlyList<string> GlobalKLineCount { get; } = new[] { "30", "60", "120", "150", "240", "300" };

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

        public TwseT86Record LatestTwseT86Record
        {
            get => _latestTwseT86Record;
            private set
            {
                _latestTwseT86Record = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<TwseT86History> TwseT86Histories => _twseT86Histories;

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
                var stockInfo = _apiService.GetRelativeStockMessage(_newSymbol);
                var isEmpty = stockInfo.bstrStockName == "";
                return isEmpty ? "None" : stockInfo.bstrStockName;
            }
        }

        public ICommand SubscribeCommand { get; }
        public ICommand UnsubscribeCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public async Task InitializeAsync()
        {
            await LoadTwseT86HistoryAsync();

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
            ApplyTwseRecordsToStock(stockVm);
            stockVm.SignalTriggered += StockVmOnSignalTriggered;
            Stocks.Add(stockVm);
            SaveSubscriptions();
        }

        private async Task AddOrSubscribeAsync(List<string> symbolList, List<string> nameList)
        {
            int count = symbolList.Count;
            if (count != nameList.Count)
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

            foreach (var eachStock in applyStocks)
            {
                var stockVm = new StockViewModel(eachStock.Item1, eachStock.Item2);
                stockVm.SelectedKLineInterval = SelectedGlobalKLineInterval;
                ApplyTwseRecordsToStock(stockVm);
                stockVm.SignalTriggered += StockVmOnSignalTriggered;
                Stocks.Add(stockVm);
            }
            SaveSubscriptions();
        }

        private async Task UpdateTwseHistoryAsync()
        {
            if (IsUpdatingTwseHistory) return;

            IsUpdatingTwseHistory = true;
            SystemMessage = "三大法人資料更新中…";
            try
            {
                var startDt = TwseHistoryStartDate;
                var endDt = DateTime.Today;

                UpdatingTwseText = "下載中…";
                var fetcher = new InstitutionalDataFetcher();

                // Find existing dates from DB
                var existingDatesT86 = _twseT86Repository.GetExistingDates();
                var existingDatesMargin = _twseMarginRepository.GetExistingDates();

                var progress = new Progress<string>(msg =>
                {
                    Application.Current.Dispatcher.Invoke(() => UpdatingTwseText = msg);
                });

                for (var date = startDt; date <= endDt; date = date.AddDays(1))
                {
                    bool hasT86 = existingDatesT86.Contains(date.Date);
                    bool hasMargin = existingDatesMargin.Contains(date.Date);

                    if (hasT86 && hasMargin)
                    {
                        continue;
                    }

                    UpdatingTwseText = $"下載 {date:yyyyMMdd} …";
                    var (t86Records, marginRecords) = await fetcher.FetchAsync(date, progress);

                    if (t86Records.Count > 0)
                        await _twseT86Repository.UpsertAsync(t86Records);

                    if (marginRecords.Count > 0)
                        await _twseMarginRepository.UpsertAsync(marginRecords);

                    await Task.Delay(2000); // 避免過快被鎖
                }

                // 從 SQLite 全量讀取並重建排行（帶進度條）
                await LoadTwseT86HistoryAsync();
                foreach (var stock in Stocks)
                    ApplyTwseRecordsToStock(stock);

                UpdatingTwseText = string.Empty;
                SystemMessage = $"三大法人資料已更新至 {DateTime.Today:yyyy/MM/dd}";
            }
            catch (Exception ex)
            {
                SystemMessage = "更新失敗: " + ex.Message;
            }
            finally
            {
                IsUpdatingTwseHistory = false;
            }
        }

        private async Task LoadTwseT86HistoryAsync()
        {
            IsUpdatingTwseHistory = true;
            UpdatingTwseText = "讀取資料庫…";
            try
            {
                // 從 SQLite 讀取全量資料（有進度回報）
                var dbProgress = new Progress<(int current, int total)>(p =>
                    Application.Current.Dispatcher.Invoke(() =>
                        UpdatingTwseText = p.total > 0
                            ? $"載入中 {p.current:N0} / {p.total:N0}"
                            : "載入中…"));

                var histories = await _twseT86Repository.LoadAllHistoriesAsync(dbProgress);
                _twseT86Histories.Clear();
                _twseT86Histories.AddRange(histories.Where(x => x != null));

                UpdatingTwseText = "載入融資融券…";
                var marginHistories = await _twseMarginRepository.LoadAllHistoriesAsync(dbProgress);
                _twseMarginHistories.Clear();
                _twseMarginHistories.AddRange(marginHistories.Where(x => x != null));

                var latest = _twseT86Histories
                    .SelectMany(x => x.RecordsByDate.Values)
                    .OrderByDescending(x => x.TradeDate)
                    .ThenBy(x => x.Symbol)
                    .FirstOrDefault();
                LatestTwseT86Record = latest;
                BuildTwseRankingList();
            }
            finally
            {
                UpdatingTwseText = string.Empty;
                IsUpdatingTwseHistory = false;
            }
        }

        private void BuildTwseRankingList()
        {
            TwseRankingItems.Clear();
            var allRecords = _twseT86Histories.SelectMany(x => x.RecordsByDate.Values).ToList();
            if (allRecords.Count == 0)
            {
                LatestRankingDate = DateTime.MinValue;
                return;
            }

            var latestDate = allRecords.Max(x => x.TradeDate.Date);
            LatestRankingDate = latestDate;
            OnPropertyChanged(nameof(LatestRankingDate));

            var latestRecords = allRecords
                .Where(x => x.TradeDate.Date == latestDate)
                .OrderByDescending(x => x.ThreeMajorNet)
                .ThenBy(x => x.Symbol)
                .ToList();

            var marginRecords = _twseMarginHistories.SelectMany(x => x.RecordsByDate.Values).ToList();

            var prevDate = allRecords.Where(x => x.TradeDate.Date < latestDate).Select(x => x.TradeDate.Date).DefaultIfEmpty(DateTime.MinValue).Max();
            var prevRanks = allRecords
                .Where(x => prevDate != DateTime.MinValue && x.TradeDate.Date == prevDate)
                .OrderByDescending(x => x.ThreeMajorNet)
                .ThenBy(x => x.Symbol)
                .Select((x, idx) => new { x.Symbol, Rank = idx + 1 })
                .ToDictionary(x => x.Symbol, x => x.Rank, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < latestRecords.Count; i++)
            {
                var record = latestRecords[i];
                int prevRank;
                var hasPrev = prevRanks.TryGetValue(record.Symbol, out prevRank);
                var rank = i + 1;
                var rankDelta = hasPrev ? prevRank - rank : 0;

                var marginRecord = marginRecords.FirstOrDefault(x => x.TradeDate.Date == latestDate && string.Equals(x.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase));
                var prevMarginRecord = prevDate != DateTime.MinValue
                    ? marginRecords.FirstOrDefault(x => x.TradeDate.Date == prevDate && string.Equals(x.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase))
                    : null;

                var marginNet = marginRecord != null ? marginRecord.MarginPurchaseSales : 0;
                var prevMarginBal = prevMarginRecord != null ? prevMarginRecord.MarginBalance : 0;
                var marginBal = marginRecord != null ? marginRecord.MarginBalance : 0;

                TwseRankingItems.Add(new TwseT86RankingItem
                {
                    Rank = rank,
                    Symbol = record.Symbol,
                    Name = record.Name,
                    Market = record.Market ?? string.Empty,
                    ThreeMajorNet = record.ThreeMajorNet,
                    MarginNet = marginNet,
                    MarginNetText = marginNet > 0 ? $"▲{marginNet:N0}" : marginNet < 0 ? $"▼{Math.Abs(marginNet):N0}" : "-",
                    MarginNetBrush = marginNet > 0 ? Brushes.IndianRed : marginNet < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    RankDeltaText = !hasPrev ? "NEW" : rankDelta > 0 ? $"▲{rankDelta}" : rankDelta < 0 ? $"▼{Math.Abs(rankDelta)}" : "-",
                    RankDeltaBrush = !hasPrev ? Brushes.SkyBlue : rankDelta > 0 ? Brushes.IndianRed : rankDelta < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    TooltipText =
                        $"日期: {record.TradeDate:yyyy/MM/dd}" +
                        $"\n代號: {record.Symbol} {record.Name}" +
                        $"\n外資 買:{record.ForeignBuy:N0} 賣:{record.ForeignSell:N0} 淨:{record.ForeignNet:N0}" +
                        $"\n投信 買:{record.InvestmentTrustBuy:N0} 賣:{record.InvestmentTrustSell:N0} 淨:{record.InvestmentTrustNet:N0}" +
                        $"\n自營商 淨:{record.DealerNet:N0} (自買:{record.DealerSelfNet:N0} / 避險:{record.DealerHedgeNet:N0})" +
                        $"\n三大法人買賣超: {record.ThreeMajorNet:N0}" +
                        $"\n前日融資餘額: {prevMarginBal:N0}" +
                        $"\n今日融資變化: {marginNet:N0}" +
                        $"\n今日融資餘額: {marginBal:N0}"
                });
            }

            // 建立 / 刷新 CollectionView
            if (FilteredTwseRankingItems == null)
            {
                FilteredTwseRankingItems = CollectionViewSource.GetDefaultView(TwseRankingItems);
            }
            ApplyRankingFilter();
        }

        private void ApplyRankingFilter()
        {
            if (FilteredTwseRankingItems == null)
            {
                FilteredTwseRankingItems = CollectionViewSource.GetDefaultView(TwseRankingItems);
            }

            switch (_rankingFilter)
            {
                case "上市櫃（4碼）":
                    FilteredTwseRankingItems.Filter = o => o is TwseT86RankingItem item && item.Symbol.Length == 4;
                    break;
                case "上市櫃（5碼）":
                    FilteredTwseRankingItems.Filter = o => o is TwseT86RankingItem item && item.Symbol.Length == 5;
                    break;
                case "僅上市":
                    FilteredTwseRankingItems.Filter = o => o is TwseT86RankingItem item &&
                        (item.Market == "上市" || item.Market.Contains("上市"));
                    break;
                case "僅上櫃":
                    FilteredTwseRankingItems.Filter = o => o is TwseT86RankingItem item &&
                        (item.Market == "上櫃" || item.Market.Contains("上櫃"));
                    break;
                default:
                    FilteredTwseRankingItems.Filter = null;
                    break;
            }
        }

        private void ApplyTwseRecordsToStock(StockViewModel stockVm)
        {
            if (stockVm == null)
            {
                return;
            }

            var history = _twseT86Histories.FirstOrDefault(x => string.Equals(x.Symbol, stockVm.Symbol, StringComparison.OrdinalIgnoreCase));
            stockVm.SetTwseT86Records(history == null ? null : history.RecordsByDate.Values);

            var marginHistory = _twseMarginHistories.FirstOrDefault(x => string.Equals(x.Symbol, stockVm.Symbol, StringComparison.OrdinalIgnoreCase));
            stockVm.SetTwseMarginRecords(marginHistory == null ? null : marginHistory.RecordsByDate.Values);
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

        public class TwseT86RankingItem
        {
            public int Rank { get; set; }
            public string Symbol { get; set; }
            public string Name { get; set; }
            public string Market { get; set; }
            public long ThreeMajorNet { get; set; }
            public long MarginNet { get; set; }
            public string MarginNetText { get; set; }
            public Brush MarginNetBrush { get; set; }
            public string RankDeltaText { get; set; }
            public Brush RankDeltaBrush { get; set; }
            public string TooltipText { get; set; }
        }
    }
}
