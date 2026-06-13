using StockManager.Library;
using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    public class RankedStockScorePoint
    {
        public DateTime Date { get; set; }
        public int Score { get; set; }
    }

    public class RankedStock
    {
        public int Rank { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public decimal LatestPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public int Score { get; set; }
        public DateTime ScoreDate { get; set; }
        public int CrashRiskScore { get; set; }
        public int PatternTagCount { get; set; }
        public string Suggestion { get; set; }
        public long ThreeMajorNet { get; set; }
        public decimal ThreeMajorNetAmount { get; set; }
        public List<RankedStockScorePoint> RecentScores { get; set; } = new List<RankedStockScorePoint>();
        public string RecentScoresText => RecentScores == null || RecentScores.Count == 0
            ? Score.ToString(CultureInfo.InvariantCulture)
            : string.Join(" / ", RecentScores
                .OrderByDescending(x => x.Date)
                .Select(x => x.Date == DateTime.MinValue
                    ? x.Score.ToString(CultureInfo.InvariantCulture)
                    : $"{x.Date:MM/dd}:{x.Score}"));
        public int ScoreDay0 => GetRecentScoreByOffset(0);
        public int ScoreDay1 => GetRecentScoreByOffset(1);
        public int ScoreDay2 => GetRecentScoreByOffset(2);
        public int ScoreDay3 => GetRecentScoreByOffset(3);
        public int ScoreDay4 => GetRecentScoreByOffset(4);
        public string ScoreDateText => ScoreDate == DateTime.MinValue ? string.Empty : ScoreDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        public double AverageRecentScore => RecentScores == null || RecentScores.Count == 0 ? Score : RecentScores.Average(x => x.Score);
        public int ScoreTrend => ScoreDay0 - ScoreDay4;
        public string NetDisplay
        {
            get
            {
                var lots = ThreeMajorNet / 1000m;
                return lots > 0 ? $"+{lots:N0}" : lots.ToString("N0", CultureInfo.InvariantCulture);
            }
        }
        public System.Windows.Media.Brush ChangePercentBrush => ChangePercent > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                                  ChangePercent < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                                  System.Windows.Media.Brushes.Gray;
        public System.Windows.Media.Brush NetDisplayBrush => ThreeMajorNet > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                               ThreeMajorNet < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                               System.Windows.Media.Brushes.Gray;
        public string NetAmountDisplay => ThreeMajorNetAmount > 0 ? $"+{ThreeMajorNetAmount:N0}" : ThreeMajorNetAmount.ToString("N0", CultureInfo.InvariantCulture);
        public System.Windows.Media.Brush NetAmountDisplayBrush => ThreeMajorNetAmount > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                                     ThreeMajorNetAmount < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                                     System.Windows.Media.Brushes.Gray;

        public int GetConsecutiveScoreDays(int minScore)
        {
            if (RecentScores == null || RecentScores.Count == 0)
            {
                return Score >= minScore ? 1 : 0;
            }

            var streak = 0;
            foreach (var recentScore in RecentScores.OrderByDescending(x => x.Date))
            {
                if (recentScore.Score < minScore)
                {
                    break;
                }

                streak++;
            }

            return streak;
        }

        private int GetRecentScoreByOffset(int offset)
        {
            if (RecentScores == null || offset < 0 || offset >= RecentScores.Count)
            {
                return 0;
            }

            return RecentScores.OrderByDescending(x => x.Date).ElementAt(offset).Score;
        }
    }

    public class RankingViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private readonly MainWindowViewModel _mainViewModel;
        private readonly string _dbPath;
        private double _progressValue;
        private string _progressText = "準備就緒";
        private ObservableCollection<RankedStock> _rankedStocks = new ObservableCollection<RankedStock>();
        private System.ComponentModel.ICollectionView _rankedStocksView;
        private bool _isScanning;
        private string _searchText;
        private decimal? _minPrice;
        private decimal? _maxPrice;
        private decimal? _minChangePercentFilter;
        private decimal? _maxChangePercentFilter;
        private long? _minThreeMajorNetFilter;
        private long? _maxThreeMajorNetFilter;
        private int? _minLatestScoreFilter;
        private int? _minCrashRiskScoreFilter;
        private int? _minPatternTagCountFilter;
        private double? _minAverageScoreFilter;
        private bool _requireScoreTrendUp;
        private int _minConsecutiveDays;
        private int _minConsecutiveScore = 60;
        private int _topCount = 100;
        private string _scoreDay0Header = "D0";
        private string _scoreDay1Header = "D1";
        private string _scoreDay2Header = "D2";
        private string _scoreDay3Header = "D3";
        private string _scoreDay4Header = "D4";

        public RankingViewModel(CapitalApiService apiService, MainWindowViewModel mainViewModel)
        {
            _apiService = apiService;
            _mainViewModel = mainViewModel;
            _dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "Ranking.db");
            EnsureDatabase();
            StartScanningCommand = new RelayCommand(async _ => await ScanAllStocksAsync(), _ => !_isScanning);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            ApplyStrongMomentumFilterCommand = new RelayCommand(_ => ApplyStrongMomentumFilter());
            ApplyLowPriceHighScoreFilterCommand = new RelayCommand(_ => ApplyLowPriceHighScoreFilter());
            ApplyInstitutionalMomentumFilterCommand = new RelayCommand(_ => ApplyInstitutionalMomentumFilter());
            ApplyScoreReboundFilterCommand = new RelayCommand(_ => ApplyScoreReboundFilter());

            _rankedStocksView = System.Windows.Data.CollectionViewSource.GetDefaultView(RankedStocks);
            _rankedStocksView.Filter = FilterRankedStocks;

            LoadSavedRanking();
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public decimal? MinPrice
        {
            get => _minPrice;
            set { _minPrice = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public decimal? MaxPrice
        {
            get => _maxPrice;
            set { _maxPrice = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public decimal? MinChangePercentFilter
        {
            get => _minChangePercentFilter;
            set { _minChangePercentFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public decimal? MaxChangePercentFilter
        {
            get => _maxChangePercentFilter;
            set { _maxChangePercentFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public long? MinThreeMajorNetFilter
        {
            get => _minThreeMajorNetFilter;
            set { _minThreeMajorNetFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public long? MaxThreeMajorNetFilter
        {
            get => _maxThreeMajorNetFilter;
            set { _maxThreeMajorNetFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public int? MinLatestScoreFilter
        {
            get => _minLatestScoreFilter;
            set { _minLatestScoreFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public int? MinCrashRiskScoreFilter
        {
            get => _minCrashRiskScoreFilter;
            set { _minCrashRiskScoreFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public int? MinPatternTagCountFilter
        {
            get => _minPatternTagCountFilter;
            set { _minPatternTagCountFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public double? MinAverageScoreFilter
        {
            get => _minAverageScoreFilter;
            set { _minAverageScoreFilter = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public bool RequireScoreTrendUp
        {
            get => _requireScoreTrendUp;
            set { _requireScoreTrendUp = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public int MinConsecutiveDays
        {
            get => _minConsecutiveDays;
            set
            {
                _minConsecutiveDays = Math.Max(0, value);
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
        }

        public int MinConsecutiveScore
        {
            get => _minConsecutiveScore;
            set
            {
                _minConsecutiveScore = value;
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
        }

        public int TopCount
        {
            get => _topCount;
            set { _topCount = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public string ScoreDay0Header
        {
            get => _scoreDay0Header;
            private set { _scoreDay0Header = value; OnPropertyChanged(); }
        }

        public string ScoreDay1Header
        {
            get => _scoreDay1Header;
            private set { _scoreDay1Header = value; OnPropertyChanged(); }
        }

        public string ScoreDay2Header
        {
            get => _scoreDay2Header;
            private set { _scoreDay2Header = value; OnPropertyChanged(); }
        }

        public string ScoreDay3Header
        {
            get => _scoreDay3Header;
            private set { _scoreDay3Header = value; OnPropertyChanged(); }
        }

        public string ScoreDay4Header
        {
            get => _scoreDay4Header;
            private set { _scoreDay4Header = value; OnPropertyChanged(); }
        }

        public ICommand ClearFiltersCommand { get; }
        public ICommand ApplyStrongMomentumFilterCommand { get; }
        public ICommand ApplyLowPriceHighScoreFilterCommand { get; }
        public ICommand ApplyInstitutionalMomentumFilterCommand { get; }
        public ICommand ApplyScoreReboundFilterCommand { get; }

        private bool FilterRankedStocks(object item)
        {
            if (item is RankedStock stock)
            {
                if (stock.Rank > TopCount) return false;

                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    !stock.Symbol.Contains(SearchText) &&
                    !stock.Name.Contains(SearchText))
                {
                    return false;
                }

                if (MinPrice.HasValue && stock.LatestPrice < MinPrice.Value) return false;
                if (MaxPrice.HasValue && stock.LatestPrice > MaxPrice.Value) return false;
                if (MinChangePercentFilter.HasValue && stock.ChangePercent < MinChangePercentFilter.Value) return false;
                if (MaxChangePercentFilter.HasValue && stock.ChangePercent > MaxChangePercentFilter.Value) return false;
                if (MinThreeMajorNetFilter.HasValue && stock.ThreeMajorNet < MinThreeMajorNetFilter.Value) return false;
                if (MaxThreeMajorNetFilter.HasValue && stock.ThreeMajorNet > MaxThreeMajorNetFilter.Value) return false;
                if (MinLatestScoreFilter.HasValue && stock.Score < MinLatestScoreFilter.Value) return false;
                if (MinCrashRiskScoreFilter.HasValue && stock.CrashRiskScore < MinCrashRiskScoreFilter.Value) return false;
                if (MinPatternTagCountFilter.HasValue && stock.PatternTagCount < MinPatternTagCountFilter.Value) return false;
                if (MinAverageScoreFilter.HasValue && stock.AverageRecentScore < MinAverageScoreFilter.Value) return false;
                if (RequireScoreTrendUp && stock.ScoreTrend <= 0) return false;
                if (MinConsecutiveDays > 0 && stock.GetConsecutiveScoreDays(MinConsecutiveScore) < MinConsecutiveDays) return false;

                return true;
            }
            return false;
        }

        private void EnsureDatabase()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_dbPath));
            using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();

                // 檢查是否包含延伸欄位，因為這支 DB 可能是舊版建立的
                bool hasThreeMajorNetColumn = false;
                bool hasRecentScoresColumn = false;
                bool hasScoreDateColumn = false;
                bool hasThreeMajorNetAmountColumn = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(LatestRanking);";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var colName = reader["name"].ToString();
                            if (colName == "ThreeMajorNet")
                            {
                                hasThreeMajorNetColumn = true;
                            }

                            if (colName == "RecentScores")
                            {
                                hasRecentScoresColumn = true;
                            }

                            if (colName == "ScoreDate")
                            {
                                hasScoreDateColumn = true;
                            }

                            if (colName == "ThreeMajorNetAmount")
                            {
                                hasThreeMajorNetAmountColumn = true;
                            }
                        }
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS LatestRanking (
                            Rank INTEGER PRIMARY KEY,
                            Symbol TEXT NOT NULL,
                            Name TEXT NOT NULL,
                            LatestPrice REAL NOT NULL,
                            ChangePercent REAL NOT NULL,
                            Score INTEGER NOT NULL,
                            ScoreDate TEXT NOT NULL DEFAULT '',
                            Suggestion TEXT NOT NULL,
                            ThreeMajorNet INTEGER NOT NULL DEFAULT 0,
                            ThreeMajorNetAmount REAL NOT NULL DEFAULT 0,
                            RecentScores TEXT NOT NULL DEFAULT ''
                        );";
                    cmd.ExecuteNonQuery();
                }

                if (!hasThreeMajorNetColumn)
                {
                    // 若存在舊表又沒有這個欄位，手動補上
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN ThreeMajorNet INTEGER NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                        // table 尚未建立時的 Alter Table 可能報錯，可忽略
                    }
                }

                if (!hasRecentScoresColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN RecentScores TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasScoreDateColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN ScoreDate TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasThreeMajorNetAmountColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN ThreeMajorNetAmount REAL NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private void LoadSavedRanking()
        {
            try
            {
                var loaded = new List<RankedStock>();
                using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, Suggestion, ThreeMajorNet, ThreeMajorNetAmount, RecentScores FROM LatestRanking ORDER BY Rank ASC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var recentScoresRaw = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
                                DateTime scoreDate;
                                var scoreDateText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                                if (!DateTime.TryParse(scoreDateText, out scoreDate))
                                {
                                    scoreDate = DateTime.MinValue;
                                }

                                loaded.Add(new RankedStock
                                {
                                    Rank = reader.GetInt32(0),
                                    Symbol = reader.GetString(1),
                                    Name = reader.GetString(2),
                                    LatestPrice = reader.GetDecimal(3),
                                    ChangePercent = reader.GetDecimal(4),
                                    Score = reader.GetInt32(5),
                                    ScoreDate = scoreDate,
                                    Suggestion = reader.GetString(7),
                                    ThreeMajorNet = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                                    ThreeMajorNetAmount = reader.IsDBNull(9) ? 0m : Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                                    RecentScores = DeserializeRecentScores(recentScoresRaw, reader.GetInt32(5))
                                });
                            }
                        }
                    }
                }

                if (loaded.Count > 0)
                {
                    foreach (var s in loaded)
                        RankedStocks.Add(s);
                    UpdateScoreHeaders(loaded);
                    ProgressText = $"已載入上次儲存的排行 ({loaded.Count} 筆)";
                }
                else
                {
                    UpdateScoreHeaders(null);
                }
            }
            catch (Exception ex)
            {
                ProgressText = $"讀取存檔時發生錯誤: {ex.Message}";
            }
        }

        private void SaveRankingToDb(IEnumerable<RankedStock> rankingResults)
        {
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM LatestRanking";
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = @"
                                INSERT INTO LatestRanking (Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, Suggestion, ThreeMajorNet, ThreeMajorNetAmount, RecentScores)
                                VALUES (@rank, @sym, @name, @price, @change, @score, @scoreDate, @sugg, @net, @netAmount, @recentScores)";
                            foreach (var s in rankingResults ?? Enumerable.Empty<RankedStock>())
                            {
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@rank", s.Rank);
                                cmd.Parameters.AddWithValue("@sym", s.Symbol);
                                cmd.Parameters.AddWithValue("@name", s.Name);
                                cmd.Parameters.AddWithValue("@price", s.LatestPrice);
                                cmd.Parameters.AddWithValue("@change", s.ChangePercent);
                                cmd.Parameters.AddWithValue("@score", s.Score);
                                cmd.Parameters.AddWithValue("@scoreDate", s.ScoreDate == DateTime.MinValue ? string.Empty : s.ScoreDate.ToString("yyyy-MM-dd"));
                                cmd.Parameters.AddWithValue("@sugg", s.Suggestion);
                                cmd.Parameters.AddWithValue("@net", s.ThreeMajorNet);
                                cmd.Parameters.AddWithValue("@netAmount", s.ThreeMajorNetAmount);
                                cmd.Parameters.AddWithValue("@recentScores", SerializeRecentScores(s.RecentScores));
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save ranking: {ex.Message}");
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RankedStock> RankedStocks
        {
            get => _rankedStocks;
            set { _rankedStocks = value; OnPropertyChanged(); }
        }

        public System.ComponentModel.ICollectionView RankedStocksView
        {
            get => _rankedStocksView;
        }

        public ICommand StartScanningCommand { get; }

        private async Task ScanAllStocksAsync()
        {
            _isScanning = true;
            CommandManager.InvalidateRequerySuggested();
            RankedStocks.Clear();

            try
            {
                ProgressText = "正在獲取代號列表...";
                ProgressValue = 0;

                var results = new List<RankedStock>();
                var distinctSymbols = new List<string>();

                // 改用群益 API 內建快取撈取 0001 ~ 9999 的所有台股四碼股票
                for (int i = 1; i <= 9999; i++)
                {
                    string sym = i.ToString("D4");
                    var info = _apiService.GetRelativeStockMessage(sym);
                    if (!string.IsNullOrWhiteSpace(info.bstrStockName))
                    {
                        distinctSymbols.Add(sym);
                    }
                }

                ProgressText = $"找到 {distinctSymbols.Count} 檔 4 碼股票，開始分析...";

                int totalChecked = 0;

                int scanBarCount;
                if (!int.TryParse(_mainViewModel.SelectedGlobalKLineCount, out scanBarCount) || scanBarCount <= 0)
                {
                    scanBarCount = 300;
                }

                MainWindow.BuildDateRangeForBars("日K", scanBarCount, out var startDate, out var endDate);
                DateTime scanHistoryStartDate;
                DateTime.TryParseExact(startDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out scanHistoryStartDate);

                int kLineCount = -1;

                // 第一階段：單緒獲取所有K線資料
                var symbolDataMap = new Dictionary<string, (string Name, List<CandleData> Candles)>();

                foreach (var symbol in distinctSymbols)
                {
                    var stockInfo = _apiService.GetRelativeStockMessage(symbol);

                    if (!string.IsNullOrEmpty(stockInfo.bstrStockName))
                    {
                        var candles = new List<CandleData>();
                        Action<string, CandleData> onKLineReceived = null;

                        onKLineReceived = (incomingSymbol, candle) =>
                        {
                            if (incomingSymbol == symbol)
                            {
                                candles.Add(candle);
                            }
                        };

                        _apiService.KLineDataReceived += onKLineReceived;

                        _apiService.RequestKLineByDate(symbol, 4, 1, 0, startDate, endDate, 0);

                        if (kLineCount == -1)
                        {
                            await Task.Delay(2000);
                            kLineCount = candles.Count;
                        }
                        else
                        {
                            var start = DateTime.UtcNow;
                            while (kLineCount >= candles.Count)
                            {
                                await Task.Delay(50);
                                if ((DateTime.UtcNow - start).TotalSeconds > 2)
                                {
                                    kLineCount = Math.Min(kLineCount, candles.Count);
                                    break;
                                }
                            }
                        }

                        _apiService.KLineDataReceived -= onKLineReceived;
                        symbolDataMap[symbol] = (stockInfo.bstrStockName, candles);
                    }

                    totalChecked++;
                    ProgressValue = ((double)totalChecked / distinctSymbols.Count) * 50; // 下載佔 50%
                    ProgressText = $"下載K線資料至第 {totalChecked} 檔股票，共 {distinctSymbols.Count} 檔 4 碼股票";

                    await System.Windows.Threading.Dispatcher.Yield();
                }

                // 第二階段：多執行緒計算推薦指標
                ProgressText = "分析K線資料計算分數中...";
                await System.Windows.Threading.Dispatcher.Yield();

                int analyzeChecked = 0;
                var lockObj = new object();
                var t86HistoryMap = await _mainViewModel.LoadAllTwseT86HistoriesForScanAsync(scanHistoryStartDate);

                await Task.Run(() =>
                {
                    Parallel.ForEach(symbolDataMap, kvp =>
                    {
                        var symbol = kvp.Key;
                        var name = kvp.Value.Name;
                        var candles = kvp.Value.Candles;

                        if (candles.Any())
                        {
                            candles.Sort((a, b) => a.Time.CompareTo(b.Time));

                            var dummyVm = new StockViewModel(symbol, name)
                            {
                                SelectedKLineInterval = "日K"
                            };
                            foreach (var c in candles)
                            {
                                dummyVm.UpdateFromKLine(c);
                            }

                            var enrichedCandles = dummyVm.GetPublicCandles().ToList();

                            TwseT86History t86History;
                            t86HistoryMap.TryGetValue(symbol, out t86History);

                            var recentScores = BuildRecentScores(enrichedCandles, t86History, symbol, name);
                            var latestScore = recentScores.Count > 0 ? recentScores[0].Score : 0;
                            var scoreDate = recentScores.Count > 0
                                ? recentScores[0].Date.Date
                                : enrichedCandles.Last().Time.Date;
                            var latestRecommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                                enrichedCandles,
                                (double)dummyVm.LatestPrice,
                                (double?)dummyVm.ChangePercent,
                                enrichedCandles.Count > 1 ? (double)enrichedCandles[enrichedCandles.Count - 2].Close : (double)dummyVm.LatestPrice,
                                t86History,
                                enrichedCandles.Last().Time);

                            long latestNet = ResolveThreeMajorNetByDate(t86History, scoreDate);

                            lock (lockObj)
                            {
                                results.Add(new RankedStock
                                {
                                    Symbol = symbol,
                                    Name = name,
                                    LatestPrice = dummyVm.LatestPrice,
                                    ChangePercent = dummyVm.ChangePercent,
                                    Score = latestScore,
                                    ScoreDate = scoreDate,
                                    CrashRiskScore = latestRecommendation.CrashRiskScore,
                                    PatternTagCount = (latestRecommendation.PatternTags ?? new List<PatternTag>()).Count,
                                    ThreeMajorNet = latestNet,
                                    ThreeMajorNetAmount = latestNet * dummyVm.LatestPrice,
                                    RecentScores = recentScores
                                });

                                analyzeChecked++;
                                ProgressValue = 50 + (((double)analyzeChecked / symbolDataMap.Count) * 50);
                                if (analyzeChecked % 50 == 0) // Reduce update frequency to improve performance
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        ProgressText = $"分析K線資料計算分數中... ({analyzeChecked}/{symbolDataMap.Count})";
                                    });
                                }
                            }
                        }
                    });
                });

                // 依 Score 由高至低排序
                results = results.OrderByDescending(r => r.Score).ToList();

                symbolDataMap.Clear();
                t86HistoryMap = null;
                distinctSymbols.Clear();

                RankedStocks.Clear();
                for (int i = 0; i < results.Count; i++)
                {
                    results[i].Rank = i + 1;
                    results[i].Suggestion = TradingRecommendationLibrary.GetAdvancedSuggestion(results[i].Score);
                    RankedStocks.Add(results[i]);
                }

                UpdateScoreHeaders(results);
                SaveRankingToDb(results);

                ProgressText = $"分析完成，找到 {RankedStocks.Count} 檔優質股票";
            }
            catch (Exception ex)
            {
                ProgressText = $"發生錯誤：{ex.Message}";
            }
            finally
            {
                _isScanning = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static List<RankedStockScorePoint> BuildRecentScores(List<CandleData> candles, TwseT86History t86History, string symbol, string name)
        {
            var recentScores = new List<RankedStockScorePoint>();
            if (candles == null || candles.Count == 0)
            {
                return recentScores;
            }

            var startIndex = Math.Max(0, candles.Count - 5);
            for (var i = startIndex; i < candles.Count; i++)
            {
                var subset = candles.Take(i + 1).ToList();
                if (subset.Count == 0)
                {
                    continue;
                }

                var latestCandle = subset[subset.Count - 1];
                var previousClose = subset.Count > 1 ? (double)subset[subset.Count - 2].Close : (double)latestCandle.Close;
                var filteredT86History = new TwseT86History
                {
                    Symbol = symbol,
                    Name = name,
                    RecordsByDate = (t86History?.RecordsByDate ?? new Dictionary<DateTime, TwseT86Record>())
                        .Where(x => x.Key.Date <= latestCandle.Time.Date)
                        .ToDictionary(x => x.Key, x => x.Value)
                };

                var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                    subset,
                    (double)latestCandle.Close,
                    (double?)latestCandle.PercentageChange,
                    previousClose,
                    filteredT86History,
                    latestCandle.Time);

                recentScores.Add(new RankedStockScorePoint
                {
                    Date = latestCandle.Time.Date,
                    Score = recommendation.Score
                });
            }

            return recentScores
                .OrderByDescending(x => x.Date)
                .ToList();
        }

        private static long ResolveThreeMajorNetByDate(TwseT86History t86History, DateTime targetDate)
        {
            if (t86History == null || t86History.RecordsByDate == null || t86History.RecordsByDate.Count == 0)
            {
                return 0;
            }

            TwseT86Record exactRecord;
            if (t86History.RecordsByDate.TryGetValue(targetDate.Date, out exactRecord) && exactRecord != null)
            {
                return exactRecord.ThreeMajorNet;
            }

            return 0;
        }

        private static string SerializeRecentScores(IEnumerable<RankedStockScorePoint> recentScores)
        {
            return string.Join("|", (recentScores ?? Enumerable.Empty<RankedStockScorePoint>())
                .OrderByDescending(x => x.Date)
                .Select(x => $"{x.Date:yyyyMMdd}:{x.Score}"));
        }

        private static List<RankedStockScorePoint> DeserializeRecentScores(string raw, int fallbackScore)
        {
            var result = new List<RankedStockScorePoint>();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (var part in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var values = part.Split(':');
                    if (values.Length != 2)
                    {
                        continue;
                    }

                    DateTime tradeDate;
                    int score;
                    if (!DateTime.TryParseExact(values[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out tradeDate) ||
                        !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out score))
                    {
                        continue;
                    }

                    result.Add(new RankedStockScorePoint
                    {
                        Date = tradeDate.Date,
                        Score = score
                    });
                }
            }

            if (result.Count == 0)
            {
                result.Add(new RankedStockScorePoint
                {
                    Date = DateTime.MinValue,
                    Score = fallbackScore
                });
            }

            return result
                .OrderByDescending(x => x.Date)
                .Take(5)
                .ToList();
        }

        private void UpdateScoreHeaders(IEnumerable<RankedStock> stocks)
        {
            var scoreDates = (stocks ?? Enumerable.Empty<RankedStock>())
                .Where(x => x != null && x.RecentScores != null)
                .OrderByDescending(x => x.RecentScores.Count)
                .Select(x => x.RecentScores.OrderByDescending(r => r.Date).Select(r => r.Date).ToList())
                .FirstOrDefault();

            ScoreDay0Header = FormatScoreHeader(scoreDates, 0);
            ScoreDay1Header = FormatScoreHeader(scoreDates, 1);
            ScoreDay2Header = FormatScoreHeader(scoreDates, 2);
            ScoreDay3Header = FormatScoreHeader(scoreDates, 3);
            ScoreDay4Header = FormatScoreHeader(scoreDates, 4);
        }

        private static string FormatScoreHeader(IReadOnlyList<DateTime> dates, int offset)
        {
            if (dates == null || offset < 0 || offset >= dates.Count)
            {
                return "D" + offset.ToString(CultureInfo.InvariantCulture);
            }

            return dates[offset] == DateTime.MinValue
                ? "D" + offset.ToString(CultureInfo.InvariantCulture)
                : dates[offset].ToString("MM/dd", CultureInfo.InvariantCulture);
        }

        private void ClearFilters()
        {
            ApplyFilterPreset(() =>
            {
                _searchText = null;
                _minPrice = null;
                _maxPrice = null;
                _minChangePercentFilter = null;
                _maxChangePercentFilter = null;
                _minThreeMajorNetFilter = null;
                _maxThreeMajorNetFilter = null;
                _minLatestScoreFilter = null;
                _minCrashRiskScoreFilter = null;
                _minPatternTagCountFilter = null;
                _minAverageScoreFilter = null;
                _requireScoreTrendUp = false;
                _minConsecutiveDays = 0;
                _minConsecutiveScore = 60;
            });
        }

        private void ApplyStrongMomentumFilter()
        {
            ApplyFilterPreset(() =>
            {
                _minConsecutiveDays = 3;
                _minConsecutiveScore = 70;
                _minLatestScoreFilter = 75;
                _minAverageScoreFilter = 70d;
                _minChangePercentFilter = 0m;
                _requireScoreTrendUp = true;
                _minThreeMajorNetFilter = null;
                _maxThreeMajorNetFilter = null;
            });
        }

        private void ApplyLowPriceHighScoreFilter()
        {
            ApplyFilterPreset(() =>
            {
                _minPrice = null;
                _maxPrice = 100m;
                _minLatestScoreFilter = 75;
                _minAverageScoreFilter = 70d;
                _minChangePercentFilter = null;
                _maxChangePercentFilter = null;
                _minConsecutiveDays = 2;
                _minConsecutiveScore = 65;
                _requireScoreTrendUp = true;
            });
        }

        private void ApplyInstitutionalMomentumFilter()
        {
            ApplyFilterPreset(() =>
            {
                _minThreeMajorNetFilter = 1;
                _maxThreeMajorNetFilter = null;
                _minLatestScoreFilter = 70;
                _minAverageScoreFilter = 65d;
                _minConsecutiveDays = 2;
                _minConsecutiveScore = 65;
                _requireScoreTrendUp = false;
            });
        }

        private void ApplyScoreReboundFilter()
        {
            ApplyFilterPreset(() =>
            {
                _minLatestScoreFilter = 65;
                _minAverageScoreFilter = 55d;
                _minChangePercentFilter = null;
                _maxChangePercentFilter = null;
                _minConsecutiveDays = 0;
                _requireScoreTrendUp = true;
            });
        }

        private void ApplyFilterPreset(Action applyAction)
        {
            applyAction?.Invoke();

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(MinPrice));
            OnPropertyChanged(nameof(MaxPrice));
            OnPropertyChanged(nameof(MinChangePercentFilter));
            OnPropertyChanged(nameof(MaxChangePercentFilter));
            OnPropertyChanged(nameof(MinThreeMajorNetFilter));
            OnPropertyChanged(nameof(MaxThreeMajorNetFilter));
            OnPropertyChanged(nameof(MinLatestScoreFilter));
            OnPropertyChanged(nameof(MinCrashRiskScoreFilter));
            OnPropertyChanged(nameof(MinPatternTagCountFilter));
            OnPropertyChanged(nameof(MinAverageScoreFilter));
            OnPropertyChanged(nameof(RequireScoreTrendUp));
            OnPropertyChanged(nameof(MinConsecutiveDays));
            OnPropertyChanged(nameof(MinConsecutiveScore));
            _rankedStocksView.Refresh();
        }
    }
}
