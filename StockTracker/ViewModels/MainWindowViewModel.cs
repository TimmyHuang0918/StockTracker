using StockTracker.Models;
using StockTracker.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace StockTracker.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private readonly TwseT86Repository _twseT86Repository;
        private readonly TwseMarginRepository _twseMarginRepository;
        private readonly TwseMarginMetricRepository _twseMarginMetricRepository;
        private readonly DailyPriceRepository _dailyPriceRepository;
        private readonly MarginMetricCalculator _marginMetricCalculator;
        private readonly List<TwseT86History> _twseT86Histories = new List<TwseT86History>();
        private readonly List<TwseMarginHistory> _twseMarginHistories = new List<TwseMarginHistory>();
        private readonly List<TwseMarginMetricHistory> _twseMarginMetricHistories = new List<TwseMarginMetricHistory>();
        private Dictionary<string, string> _exDividendTagBySymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _newSymbol;
        private string _systemMessage;
        private string _selectedGlobalKLineInterval = "日K";
        private string _selectedGlobalKLineCount = "300";
        private TwseT86Record _latestTwseT86Record;
        private DateTime _latestRankingDate;
        private DateTime _twseHistoryStartDate = DateTime.Today;
        private bool _isUpdatingTwseHistory;
        private bool _isInitializingMainPage;
        private double _mainPageProgressValue;
        private string _updatingTwseText;
        private string _rankingFilter = "全部顯示";
        private ICollectionView _filteredTwseRankingItems;
        private int _trackedHistorySymbolCount;
        private int _trackedHistoryRecordCount;
        private int _rankingDisplayCount;
        private DispatcherTimer _nightlyAutomationTimer;
        private bool _isNightlyAutomationRunning;
        private RankingViewModel _nightlyRankingViewModel;
        private string SubscriptionFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "subscriptions.txt");
        public MainWindowViewModel(CapitalApiService apiService)
        {
            _apiService = apiService;
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "twse_t86.db");
            _twseT86Repository = new TwseT86Repository(dbPath);

            var marginDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "twse_margin.db");
            _twseMarginRepository = new TwseMarginRepository(marginDbPath);

            var marginMetricDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "twse_margin_metric.db");
            _twseMarginMetricRepository = new TwseMarginMetricRepository(marginMetricDbPath);

            var dailyPriceDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "daily_price.db");
            _dailyPriceRepository = new DailyPriceRepository(dailyPriceDbPath);
            _marginMetricCalculator = new MarginMetricCalculator();

            Stocks = new ObservableCollection<StockViewModel>();
            TwseRankingItems = new ObservableCollection<TwseT86RankingItem>();
            SubscribeCommand = new RelayCommand(async _ => await SubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbolRelativeName));
            UnsubscribeCommand = new RelayCommand(async _ => await UnsubscribeSymbolAsync(), _ => !string.IsNullOrWhiteSpace(NewSymbol));
            UpdateTwseHistoryCommand = new RelayCommand(async _ => await UpdateTwseHistoryAsync(), _ => !IsUpdatingTwseHistory);
        }

        public bool IsInitializingMainPage
        {
            get => _isInitializingMainPage;
            private set
            {
                _isInitializingMainPage = value;
                OnPropertyChanged();
            }
        }

        public double MainPageProgressValue
        {
            get => _mainPageProgressValue;
            private set
            {
                _mainPageProgressValue = value;
                OnPropertyChanged();
            }
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

        public int TrackedHistorySymbolCount
        {
            get => _trackedHistorySymbolCount;
            private set
            {
                _trackedHistorySymbolCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CacheStatusText));
            }
        }

        public int TrackedHistoryRecordCount
        {
            get => _trackedHistoryRecordCount;
            private set
            {
                _trackedHistoryRecordCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CacheStatusText));
            }
        }

        public int RankingDisplayCount
        {
            get => _rankingDisplayCount;
            private set
            {
                _rankingDisplayCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CacheStatusText));
            }
        }

        public string CacheStatusText => $"暫存追蹤股票: {TrackedHistorySymbolCount:N0} 檔 / {TrackedHistoryRecordCount:N0} 筆，排行顯示: {RankingDisplayCount:N0} 筆";

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

        public Brush ConnectionBrush
        {
            get
            {
                if (_apiService.ServiceStatus.Item1 == 1)
                    return Brushes.Green;
                else
                {
                    _ = _apiService.ReLogin();
                    return Brushes.Red;
                }
            }
        }

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
            IsInitializingMainPage = true;
            MainPageProgressValue = 0;
            try
            {
                MainPageProgressValue = 20;
                await LoadTwseT86HistoryAsync();
                MainPageProgressValue = 55;

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

                MainPageProgressValue = 75;
                await AddOrSubscribeAsync(symbols, names);
                MainPageProgressValue = 100;
                EnsureNightlyAutomationTimer();
            }
            finally
            {
                IsInitializingMainPage = false;
            }
        }

        private void EnsureNightlyAutomationTimer()
        {
            if (_nightlyAutomationTimer != null)
            {
                return;
            }

            _nightlyAutomationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _nightlyAutomationTimer.Tick += async (_, __) => await TryRunNightlyAutomationAsync();
            _nightlyAutomationTimer.Start();
        }

        private async Task TryRunNightlyAutomationAsync()
        {
            if (_isNightlyAutomationRunning)
            {
                return;
            }

            var now = DateTime.Now;
            if (now.Hour != 22 || now.Minute != 0)
            {
                return;
            }

            _isNightlyAutomationRunning = true;
            try
            {
                await RunNightlyAutomationAsync();
            }
            catch (Exception ex)
            {
                SystemMessage = "排程執行失敗: " + ex.Message;
            }
            finally
            {
                _isNightlyAutomationRunning = false;
            }
        }

        private async Task RunNightlyAutomationAsync()
        {
            SystemMessage = "夜間排程啟動：更新法人、刷新主頁、掃描全市場中...";
            await UpdateTwseHistoryAsync();
            await RefreshAllTrackedStocksAsync();

            if (_nightlyRankingViewModel == null)
            {
                _nightlyRankingViewModel = new RankingViewModel(_apiService, this);
            }

            await _nightlyRankingViewModel.StartScanningAsync();
            var xmlPath = _nightlyRankingViewModel.ExportLatestRankingToXml();
            var htmlPath = _nightlyRankingViewModel.ExportLatestRankingToHtml();
            var websiteUrl = PublishRankingWebsiteToGitHub(htmlPath);
            var recipients = _nightlyRankingViewModel.GetNotificationEmailRecipients();
            SendRankingReportEmail(xmlPath, websiteUrl, recipients);
            SystemMessage = "夜間排程完成。";
        }

        public async Task PublishRankingWebsiteByHandAsync(RankingViewModel rankingViewModel)
        {
            if (rankingViewModel == null)
            {
                return;
            }

            await Task.Run(() =>
            {
                var xmlPath = rankingViewModel.ExportLatestRankingToXml();
                var htmlPath = rankingViewModel.ExportLatestRankingToHtml();
                var websiteUrl = PublishRankingWebsiteToGitHub(htmlPath);
                var recipients = rankingViewModel.GetNotificationEmailRecipients();
                SendRankingReportEmail(xmlPath, websiteUrl, recipients);
            });
        }

        private async Task RefreshAllTrackedStocksAsync()
        {
            if (Stocks == null || Stocks.Count == 0)
            {
                return;
            }

            ResolveKLineRequest(SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);
            int kLineCount;
            if (!int.TryParse(SelectedGlobalKLineCount, out kLineCount) || kLineCount <= 0)
            {
                kLineCount = 300;
            }

            MainWindow.BuildDateRangeForBars(SelectedGlobalKLineInterval, kLineCount, out var startDate, out var endDate);

            foreach (var stock in Stocks)
            {
                stock.ClearData();
                _apiService.RequestKLineByDate(stock.Symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
                await Task.Delay(120);
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

        private string PublishRankingWebsiteToGitHub(string htmlFilePath)
        {
            if (string.IsNullOrWhiteSpace(htmlFilePath) || !File.Exists(htmlFilePath))
            {
                return string.Empty;
            }

            var repoRoot = FindGitRepositoryRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return string.Empty;
            }

            var docsFolder = Path.Combine(repoRoot, "docs", "nightly-ranking");
            Directory.CreateDirectory(docsFolder);
            var targetFile = Path.Combine(docsFolder, "index.html");
            const string websiteUrl = "https://timmyhuang0918.github.io/StockTracker/nightly-ranking/";

            if (File.Exists(targetFile) &&
                string.Equals(File.ReadAllText(htmlFilePath), File.ReadAllText(targetFile), StringComparison.Ordinal))
            {
                return websiteUrl;
            }

            File.Copy(htmlFilePath, targetFile, true);

            if (!RunGitCommand(repoRoot, "add docs/nightly-ranking/index.html"))
            {
                return string.Empty;
            }

            RunGitCommand(repoRoot, $"commit -m \"chore: update nightly ranking website {DateTime.Now:yyyy-MM-dd HH:mm}\"");
            if (!RunGitCommand(repoRoot, "push origin master"))
            {
                return string.Empty;
            }

            return websiteUrl;
        }

        private static string FindGitRepositoryRoot(string startPath)
        {
            try
            {
                var current = new DirectoryInfo(startPath);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool RunGitCommand(string workingDirectory, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit(120000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void SendRankingReportEmail(string attachmentFile, string websiteUrl, IReadOnlyList<string> recipients)
        {
            var from = GetAppSetting("NightlyEmailFrom");
            var host = GetAppSetting("NightlySmtpHost");
            var user = GetAppSetting("NightlySmtpUser");
            var pass = GetAppSetting("NightlySmtpPass");
            var allRecipients = (recipients ?? Array.Empty<string>()).ToList();
            var fallbackRecipient = GetAppSetting("NightlyEmailTo");
            if (!string.IsNullOrWhiteSpace(fallbackRecipient))
            {
                allRecipients.AddRange(fallbackRecipient
                    .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()));
            }

            allRecipients = allRecipients
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Contains("@"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int port;
            if (allRecipients.Count == 0 || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(host) ||
                !int.TryParse(GetAppSetting("NightlySmtpPort"), out port))
            {
                SystemMessage = "夜間排程完成，但未設定郵件參數（已輸出 XML）。";
                return;
            }

            using (var message = new MailMessage())
            {
                message.From = new MailAddress(from);
                foreach (var recipient in allRecipients)
                {
                    message.To.Add(recipient);
                }

                message.Subject = $"StockTracker Nightly Ranking {DateTime.Now:yyyy/MM/dd}";
                message.Body = string.IsNullOrWhiteSpace(websiteUrl)
                    ? "夜間全市場掃描已完成，附件為 XML 報表。"
                    : "夜間全市場掃描已完成。\n網站: " + websiteUrl + "\n附件為 XML 報表。";
                if (!string.IsNullOrWhiteSpace(attachmentFile) && File.Exists(attachmentFile))
                {
                    message.Attachments.Add(new Attachment(attachmentFile));
                }

                using (var client = new SmtpClient(host, port))
                {
                    client.EnableSsl = true;
                    if (!string.IsNullOrWhiteSpace(user))
                    {
                        client.Credentials = new NetworkCredential(user, pass ?? string.Empty);
                    }

                    client.Send(message);
                }
            }
        }

        private static string GetAppSetting(string key)
        {
            try
            {
                var configPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
                if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                {
                    return string.Empty;
                }

                var doc = XDocument.Load(configPath);
                var value = doc.Descendants("add")
                    .FirstOrDefault(x => string.Equals((string)x.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("value")
                    ?.Value;

                return value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void ApplyKLineData(string symbol, CandleData candle)
        {
            var normalized = symbol?.Trim() ?? string.Empty;
            var vm = Stocks.FirstOrDefault(x =>
                string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(x.Symbol, StringComparison.OrdinalIgnoreCase));
            vm?.UpdateFromKLine(candle);
        }

        public async Task SubscribeSymbolAsync()
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

        public void MoveStock(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Stocks.Count || newIndex < 0 || newIndex >= Stocks.Count || oldIndex == newIndex)
            {
                return;
            }

            Stocks.Move(oldIndex, newIndex);
            SaveSubscriptions();
            UpdateCacheStatus();
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

            var stockVm = new StockViewModel(symbol, name);
            stockVm.SelectedKLineInterval = SelectedGlobalKLineInterval;
            stockVm.SignalTriggered += StockVmOnSignalTriggered;
            Stocks.Add(stockVm);

            await _apiService.SubscribeAsync(symbol);
            await EnsureTrackedHistoryLoadedAsync(symbol);
            ApplyTwseRecordsToStock(stockVm);

            UpdateCacheStatus();
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

            foreach (var eachStock in applyStocks)
            {
                var stockVm = new StockViewModel(eachStock.Item1, eachStock.Item2);
                stockVm.SelectedKLineInterval = SelectedGlobalKLineInterval;
                stockVm.SignalTriggered += StockVmOnSignalTriggered;
                Stocks.Add(stockVm);
            }

            await _apiService.SubscribeAsync(subScribeString);

            foreach (var eachStock in applyStocks)
            {
                await EnsureTrackedHistoryLoadedAsync(eachStock.Item1);
                var stockVm = Stocks.FirstOrDefault(x => string.Equals(x.Symbol, eachStock.Item1, StringComparison.OrdinalIgnoreCase));
                if (stockVm != null)
                {
                    ApplyTwseRecordsToStock(stockVm);
                }
            }
            UpdateCacheStatus();
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
                var priceFetcher = new DailyPriceFetcher();

                // Find existing dates from DB
                var existingDatesT86 = _twseT86Repository.GetExistingDates();
                var existingDatesMargin = _twseMarginRepository.GetExistingDates();
                var existingDatesPrice = _dailyPriceRepository.GetExistingDates();

                var progress = new Progress<string>(msg =>
                {
                    Application.Current.Dispatcher.Invoke(() => UpdatingTwseText = msg);
                });

                for (var date = startDt; date <= endDt; date = date.AddDays(1))
                {
                    bool hasT86 = existingDatesT86.Contains(date.Date);
                    bool hasMargin = existingDatesMargin.Contains(date.Date);
                    bool hasPrice = existingDatesPrice.Contains(date.Date);

                    if (hasT86 && hasMargin && hasPrice)
                    {
                        continue;
                    }

                    UpdatingTwseText = $"下載 {date:yyyyMMdd} …";
                    var (t86Records, marginRecords) = await fetcher.FetchAsync(date, progress);

                    if (t86Records.Count > 0)
                        await _twseT86Repository.UpsertAsync(t86Records);

                    if (marginRecords.Count > 0)
                        await _twseMarginRepository.UpsertAsync(marginRecords);

                    if (!hasPrice)
                    {
                        var priceRecords = await priceFetcher.FetchAsync(date);
                        if (priceRecords.Count > 0)
                            await _dailyPriceRepository.UpsertAsync(priceRecords);
                    }

                    await Task.Delay(2000); // 避免過快被鎖
                }

                _exDividendTagBySymbol = await LoadUpcomingExDividendTagsAsync();
                ApplyExDividendTagsToStocks();

                // 從 SQLite 全量讀取並重建排行（帶進度條）
                await LoadTwseT86HistoryAsync(true);
                foreach (var stock in Stocks)
                    ApplyTwseRecordsToStock(stock);

                foreach (var stock in Stocks)
                {
                    stock.RebuildForLatestTwseData();
                }

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

        private async Task LoadTwseT86HistoryAsync(bool rebuildMarginMetrics = false)
        {
            IsUpdatingTwseHistory = true;
            UpdatingTwseText = "讀取資料庫…";
            try
            {
                var trackedSymbols = LoadSavedSubscriptions();
                if (trackedSymbols.Count == 0)
                {
                    trackedSymbols.Add("2330");
                    trackedSymbols.Add("2317");
                    trackedSymbols.Add("0050");
                }

                // 從 SQLite 讀取全量資料（有進度回報）
                var dbProgress = new Progress<(int current, int total)>(p =>
                    Application.Current.Dispatcher.Invoke(() =>
                        UpdatingTwseText = p.total > 0
                            ? $"載入中 {p.current:N0} / {p.total:N0}"
                            : "載入中…"));

                UpdatingTwseText = "載入三大法人排行…";
                await BuildTwseRankingListAsync();

                UpdatingTwseText = "載入已追蹤股票三大法人資料…";
                var histories = await _twseT86Repository.LoadHistoriesBySymbolsAsync(trackedSymbols);
                ReplaceTrackedHistories(_twseT86Histories, histories);

                UpdatingTwseText = "載入已追蹤股票融資融券…";
                var marginHistories = await _twseMarginRepository.LoadHistoriesBySymbolsAsync(trackedSymbols);
                ReplaceTrackedHistories(_twseMarginHistories, marginHistories);

                UpdatingTwseText = rebuildMarginMetrics ? "載入已追蹤股票日收盤價…" : UpdatingTwseText;

                if (rebuildMarginMetrics)
                {
                    UpdatingTwseText = "計算融資維持率…";
                    var dailyPriceHistories = await _dailyPriceRepository.LoadHistoriesBySymbolsAsync(trackedSymbols);
                    RebuildMarginMetricHistories(marginHistories, dailyPriceHistories);
                    await _twseMarginMetricRepository.ReplaceAllAsync(_twseMarginMetricHistories);
                }
                else
                {
                    UpdatingTwseText = "載入已追蹤股票融資維持率…";
                    var marginMetricHistories = await _twseMarginMetricRepository.LoadHistoriesBySymbolsAsync(trackedSymbols);
                    ReplaceTrackedHistories(_twseMarginMetricHistories, marginMetricHistories);
                }

                UpdateCacheStatus();
            }
            finally
            {
                UpdatingTwseText = string.Empty;
                IsUpdatingTwseHistory = false;
            }
        }

        private async Task BuildTwseRankingListAsync()
        {
            TwseRankingItems.Clear();
            var latestDates = _twseT86Repository.GetLatestTradeDates(2);
            if (latestDates.Count == 0)
            {
                LatestRankingDate = DateTime.MinValue;
                LatestTwseT86Record = null;
                return;
            }

            var latestDate = latestDates[0];
            var prevDate = latestDates.Count > 1 ? latestDates[1] : DateTime.MinValue;
            LatestRankingDate = latestDate;
            OnPropertyChanged(nameof(LatestRankingDate));

            var latestRecords = (await _twseT86Repository.LoadByDateAsync(latestDate)).ToList();
            LatestTwseT86Record = latestRecords.OrderBy(x => x.Symbol).FirstOrDefault();

            var previousRecords = prevDate == DateTime.MinValue
                ? new List<TwseT86Record>()
                : (await _twseT86Repository.LoadByDateAsync(prevDate)).ToList();

            var allRecords = latestRecords;
            if (allRecords.Count == 0)
            {
                LatestRankingDate = DateTime.MinValue;
                return;
            }

            latestRecords = latestRecords
                .OrderByDescending(x => x.ThreeMajorNet)
                .ThenBy(x => x.Symbol)
                .ToList();

            var marginRecords = await _twseMarginRepository.LoadByDateAsync(latestDate);
            var marginMetricRecords = await _twseMarginMetricRepository.LoadByDateAsync(latestDate);
            var latestCloseBySymbol = await _dailyPriceRepository.LoadByDateAsync(latestDate);
            var prevMarginRecords = prevDate == DateTime.MinValue
                ? new List<TwseMarginRecord>()
                : (await _twseMarginRepository.LoadByDateAsync(prevDate)).ToList();

            var prevRanks = previousRecords
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

                var marginRecord = marginRecords.FirstOrDefault(x => string.Equals(x.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase));
                var prevMarginRecord = prevDate != DateTime.MinValue
                    ? prevMarginRecords.FirstOrDefault(x => string.Equals(x.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase))
                    : null;

                var marginNet = marginRecord != null ? marginRecord.MarginPurchaseSales : 0;
                var investmentTrustNet = record.InvestmentTrustNet;
                var threeMajorNetLots = record.ThreeMajorNet / 1000d;
                var investmentTrustNetLots = investmentTrustNet / 1000d;
                var marginNetLots = marginNet / 1000d;
                double closePrice;
                var hasClosePrice = latestCloseBySymbol.TryGetValue(record.Symbol, out closePrice);
                var threeMajorNetAmount = hasClosePrice ? (double)record.ThreeMajorNet * closePrice : 0d;
                var prevMarginBal = prevMarginRecord != null ? prevMarginRecord.MarginBalance : 0;
                var marginBal = marginRecord != null ? marginRecord.MarginBalance : 0;
                var marginMetric = marginMetricRecords.FirstOrDefault(x => x.Record != null && string.Equals(x.Record.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase));
                var maintenanceRatio = marginMetric != null ? marginMetric.MarginMaintenanceRatio : 0d;
                var averageCost = marginMetric != null ? marginMetric.MarginAverageCost : 0d;
                var totalLoan = marginMetric != null ? marginMetric.TotalLoan : 0d;
                var maintenanceStatus = maintenanceRatio <= 0d
                    ? "無資料"
                    : maintenanceRatio < 130d
                        ? "警戒"
                        : maintenanceRatio < 166.7d
                            ? "留意"
                            : "安全";

                TwseRankingItems.Add(new TwseT86RankingItem
                {
                    Rank = rank,
                    Symbol = record.Symbol,
                    Name = record.Name,
                    Market = record.Market ?? string.Empty,
                    ThreeMajorNet = record.ThreeMajorNet,
                    ThreeMajorNetDisplayText = threeMajorNetLots > 0 ? $"▲{threeMajorNetLots:N0}" : threeMajorNetLots < 0 ? $"▼{Math.Abs(threeMajorNetLots):N0}" : "-",
                    ThreeMajorNetAmount = threeMajorNetAmount,
                    ThreeMajorNetAmountText = hasClosePrice
                        ? (threeMajorNetAmount > 0 ? $"▲{threeMajorNetAmount:N0}" : threeMajorNetAmount < 0 ? $"▼{Math.Abs(threeMajorNetAmount):N0}" : "-")
                        : "-",
                    ThreeMajorNetAmountBrush = !hasClosePrice
                        ? Brushes.Gainsboro
                        : threeMajorNetAmount > 0 ? Brushes.IndianRed : threeMajorNetAmount < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    InvestmentTrustNet = investmentTrustNet,
                    InvestmentTrustNetText = investmentTrustNetLots > 0 ? $"▲{investmentTrustNetLots:N0}" : investmentTrustNetLots < 0 ? $"▼{Math.Abs(investmentTrustNetLots):N0}" : "-",
                    InvestmentTrustNetBrush = investmentTrustNet > 0 ? Brushes.IndianRed : investmentTrustNet < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    MarginNet = marginNet,
                    MarginNetText = marginNetLots > 0 ? $"▲{marginNetLots:N0}" : marginNetLots < 0 ? $"▼{Math.Abs(marginNetLots):N0}" : "-",
                    MarginNetBrush = marginNet > 0 ? Brushes.IndianRed : marginNet < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    RankDeltaText = !hasPrev ? "NEW" : rankDelta > 0 ? $"▲{rankDelta}" : rankDelta < 0 ? $"▼{Math.Abs(rankDelta)}" : "-",
                    RankDeltaBrush = !hasPrev ? Brushes.SkyBlue : rankDelta > 0 ? Brushes.IndianRed : rankDelta < 0 ? Brushes.MediumSeaGreen : Brushes.Gainsboro,
                    TooltipText =
                        $"日期: {record.TradeDate:yyyy/MM/dd}" +
                        $"\n代號: {record.Symbol} {record.Name}" +
                        $"\n外資 買:{record.ForeignBuy / 1000d:N0}張 賣:{record.ForeignSell / 1000d:N0}張 淨:{record.ForeignNet / 1000d:N0}張" +
                        $"\n投信 買:{record.InvestmentTrustBuy / 1000d:N0}張 賣:{record.InvestmentTrustSell / 1000d:N0}張 淨:{record.InvestmentTrustNet / 1000d:N0}張" +
                        $"\n自營商 淨:{record.DealerNet / 1000d:N0}張 (自買:{record.DealerSelfNet / 1000d:N0}張 / 避險:{record.DealerHedgeNet / 1000d:N0}張)" +
                        $"\n三大法人買賣超: {record.ThreeMajorNet / 1000d:N0}張" +
                        $"\n前日融資餘額: {prevMarginBal:N0}" +
                        $"\n今日融資變化: {marginNet / 1000d:N0}張" +
                        $"\n今日融資餘額: {marginBal:N0}" +
                        $"\n融資維持率: {(maintenanceRatio > 0d ? maintenanceRatio.ToString("F2") + "%" : "0.00%")}" +
                        $" ({maintenanceStatus})" +
                        $"\n融資平均成本: {averageCost:F2}" +
                        $"\n融資借款估值: {totalLoan:N0}"
                });
            }

            // 建立 / 刷新 CollectionView
            if (FilteredTwseRankingItems == null)
            {
                FilteredTwseRankingItems = CollectionViewSource.GetDefaultView(TwseRankingItems);
            }
            ApplyRankingFilter();
            UpdateCacheStatus();
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

            var marginMetricHistory = _twseMarginMetricHistories.FirstOrDefault(x => string.Equals(x.Symbol, stockVm.Symbol, StringComparison.OrdinalIgnoreCase));
            stockVm.SetTwseMarginMetricRecords(marginMetricHistory == null ? null : marginMetricHistory.RecordsByDate.Values);
        }

        public async Task<IReadOnlyDictionary<string, TwseT86History>> LoadAllTwseT86HistoriesForScanAsync(DateTime? startDate = null)
        {
            var latestRecords = await _twseT86Repository.LoadByDateAsync(LatestRankingDate == DateTime.MinValue ? DateTime.Today : LatestRankingDate);
            var allSymbols = latestRecords
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol))
                .Select(x => x.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Dictionary<string, TwseT86History>(StringComparer.OrdinalIgnoreCase);
            const int batchSize = 250;
            for (var i = 0; i < allSymbols.Count; i += batchSize)
            {
                var batchSymbols = allSymbols.Skip(i).Take(batchSize).ToList();
                var histories = await _twseT86Repository.LoadHistoriesBySymbolsAsync(batchSymbols, startDate);
                foreach (var history in histories.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol)))
                {
                    result[history.Symbol] = history;
                }
            }

            return result;
        }

        private void RebuildMarginMetricHistories(IEnumerable<TwseMarginHistory> marginHistories, IEnumerable<DailyCloseHistory> dailyPriceHistories)
        {
            _twseMarginMetricHistories.Clear();

            var priceBySymbol = (dailyPriceHistories ?? Enumerable.Empty<DailyCloseHistory>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol))
                .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (var marginHistory in (marginHistories ?? Enumerable.Empty<TwseMarginHistory>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol)))
            {
                DailyCloseHistory closeHistory;
                priceBySymbol.TryGetValue(marginHistory.Symbol, out closeHistory);

                var orderedRecords = marginHistory.RecordsByDate.Values
                    .OrderBy(x => x.TradeDate)
                    .ToList();

                var priceDict = closeHistory == null
                    ? new Dictionary<DateTime, double>()
                    : closeHistory.RecordsByDate.ToDictionary(x => x.Key, x => x.Value.Close);

                var metricResults = _marginMetricCalculator.CalculateMarginMetrics(orderedRecords, priceDict);
                if (metricResults.Count == 0)
                {
                    continue;
                }

                _twseMarginMetricHistories.Add(new TwseMarginMetricHistory
                {
                    Symbol = marginHistory.Symbol,
                    Name = marginHistory.Name,
                    RecordsByDate = metricResults
                        .Where(x => x.Record != null)
                        .GroupBy(x => x.Record.TradeDate.Date)
                        .ToDictionary(x => x.Key, x => x.Last())
                });
            }
        }

        private static void ReplaceTrackedHistories<T>(List<T> target, IEnumerable<T> source)
        {
            target.Clear();
            target.AddRange((source ?? Enumerable.Empty<T>()).Where(x => x != null));
        }

        private async Task EnsureTrackedHistoryLoadedAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var symbols = new[] { symbol };

            var t86History = await _twseT86Repository.LoadHistoriesBySymbolsAsync(symbols);
            _twseT86Histories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            _twseT86Histories.AddRange(t86History.Where(x => x != null));

            var stockVm = Stocks.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (stockVm != null)
            {
                string exDividendTagText;
                stockVm.ExDividendTagText = _exDividendTagBySymbol.TryGetValue(symbol, out exDividendTagText)
                    ? exDividendTagText
                    : null;
            }

            var marginHistory = await _twseMarginRepository.LoadHistoriesBySymbolsAsync(symbols);
            _twseMarginHistories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            _twseMarginHistories.AddRange(marginHistory.Where(x => x != null));

            var marginMetricHistory = await _twseMarginMetricRepository.LoadHistoriesBySymbolsAsync(symbols);
            _twseMarginMetricHistories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            _twseMarginMetricHistories.AddRange(marginMetricHistory.Where(x => x != null));
        }

        private void RemoveTrackedHistory(string symbol)
        {
            _twseT86Histories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            _twseMarginHistories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            _twseMarginMetricHistories.RemoveAll(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateCacheStatus()
        {
            TrackedHistorySymbolCount = Stocks.Count;
            TrackedHistoryRecordCount =
                _twseT86Histories.Sum(x => x?.RecordsByDate.Count ?? 0) +
                _twseMarginHistories.Sum(x => x?.RecordsByDate.Count ?? 0) +
                _twseMarginMetricHistories.Sum(x => x?.RecordsByDate.Count ?? 0);
            RankingDisplayCount = TwseRankingItems.Count;
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

        private static async Task<Dictionary<string, string>> LoadUpcomingExDividendTagsAsync()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var httpClient = new HttpClient())
                {
                    var json = await httpClient.GetStringAsync("https://www.twse.com.tw/exchangeReport/TWT48U?response=json");
                    var payload = JObject.Parse(json);
                    var rows = payload["data"] as JArray;
                    if (rows == null)
                    {
                        return result;
                    }

                    var today = DateTime.Today;
                    foreach (var row in rows)
                    {
                        var columns = row as JArray;
                        if (columns == null || columns.Count < 2)
                        {
                            continue;
                        }

                        var exDividendDateText = columns[0] == null ? string.Empty : columns[0].ToString();
                        var symbol = (columns[1] == null ? string.Empty : columns[1].ToString()).Trim();
                        if (string.IsNullOrWhiteSpace(symbol))
                        {
                            continue;
                        }

                        DateTime exDividendDate;
                        if (!TryParseRocDate(exDividendDateText, out exDividendDate))
                        {
                            continue;
                        }

                        var days = (exDividendDate.Date - today).Days;
                        if (days < 0 || days > 7)
                        {
                            continue;
                        }

                        result[symbol] = $"除息 {days}天 {exDividendDate:MM月dd號}";
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private void ApplyExDividendTagsToStocks()
        {
            foreach (var stock in Stocks)
            {
                string exDividendTagText;
                stock.ExDividendTagText = _exDividendTagBySymbol.TryGetValue(stock.Symbol, out exDividendTagText)
                    ? exDividendTagText
                    : null;
            }
        }

        private static bool TryParseRocDate(string input, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var text = input.Trim();
            var yearSplit = text.Split(new[] { '年' }, StringSplitOptions.RemoveEmptyEntries);
            if (yearSplit.Length != 2)
            {
                return false;
            }

            int rocYear;
            if (!int.TryParse(yearSplit[0], out rocYear))
            {
                return false;
            }

            var monthDay = yearSplit[1].Split(new[] { '月', '日' }, StringSplitOptions.RemoveEmptyEntries);
            if (monthDay.Length < 2)
            {
                return false;
            }

            int month;
            int day;
            if (!int.TryParse(monthDay[0], out month) || !int.TryParse(monthDay[1], out day))
            {
                return false;
            }

            try
            {
                date = new DateTime(rocYear + 1911, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public class TwseT86RankingItem
        {
            public int Rank { get; set; }
            public string Symbol { get; set; }
            public string Name { get; set; }
            public string Market { get; set; }
            public long ThreeMajorNet { get; set; }
            public string ThreeMajorNetDisplayText { get; set; }
            public double ThreeMajorNetAmount { get; set; }
            public string ThreeMajorNetAmountText { get; set; }
            public Brush ThreeMajorNetAmountBrush { get; set; }
            public long InvestmentTrustNet { get; set; }
            public string InvestmentTrustNetText { get; set; }
            public Brush InvestmentTrustNetBrush { get; set; }
            public long MarginNet { get; set; }
            public string MarginNetText { get; set; }
            public Brush MarginNetBrush { get; set; }
            public string RankDeltaText { get; set; }
            public Brush RankDeltaBrush { get; set; }
            public string TooltipText { get; set; }
        }
    }
}
