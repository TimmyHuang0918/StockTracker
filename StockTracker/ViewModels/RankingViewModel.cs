using Microsoft.Win32;
using StockManager.Library;
using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;

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
        public string PatternTagsText { get; set; }
        public string Suggestion { get; set; }
        public string StrategyDecision { get; set; }
        public string StrategyActionText { get; set; }
        public string StrategyStageLabel { get; set; }
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
        public System.Windows.Media.Brush StrategyActionBrush =>
            string.Equals(StrategyDecision, "CLEAR", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Brushes.IndianRed
                : string.Equals(StrategyDecision, "BUY_STAGE1", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(StrategyDecision, "BUY_STAGE2", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(StrategyDecision, "BUY_STAGE3", StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Brushes.MediumSeaGreen
                    : string.Equals(StrategyDecision, "EXIT_STAGE1", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(StrategyDecision, "EXIT_STAGE2", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(StrategyDecision, "EXIT_STAGE3", StringComparison.OrdinalIgnoreCase)
                        ? System.Windows.Media.Brushes.Goldenrod
                    : System.Windows.Media.Brushes.Gainsboro;

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
        private readonly string _notificationEmailListPath;
        private double _progressValue;
        private string _progressText = "準備就緒";
        private string _notificationEmailList;
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
        private string _selectedPatternTag = "全部";
        private string _selectedStrategyAction = "全部";
        private string _selectedStrategyHolding = "全部";
        private string _selectedSuggestion = "全部";
        private double? _minAverageScoreFilter;
        private bool _requireScoreTrendUp;
        private int _minConsecutiveDays;
        private int _minConsecutiveScore = 60;
        private int _topCount = 100;
        private bool _isControlPanelExpanded = true;
        private bool _isPublishingWebsite;
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
            _notificationEmailListPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "RankingEmailList.txt");
            EnsureDatabase();
            StartScanningCommand = new RelayCommand(async _ => await ScanAllStocksAsync(), _ => !_isScanning);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            ApplyStrongMomentumFilterCommand = new RelayCommand(_ => ApplyStrongMomentumFilter());
            ApplyLowPriceHighScoreFilterCommand = new RelayCommand(_ => ApplyLowPriceHighScoreFilter());
            ApplyInstitutionalMomentumFilterCommand = new RelayCommand(_ => ApplyInstitutionalMomentumFilter());
            ApplyScoreReboundFilterCommand = new RelayCommand(_ => ApplyScoreReboundFilter());
            ToggleControlPanelCommand = new RelayCommand(_ => IsControlPanelExpanded = !IsControlPanelExpanded);
            ToggleExportCsvCommand = new RelayCommand(_ => ExportLatestRankingToXmlSaveFile());
            PublishWebsiteCommand = new RelayCommand(async _ => await PublishWebsiteByHandAsync(), _ => !_isPublishingWebsite);
            PatternTagOptions = new ObservableCollection<string> { "全部" };
            StrategyActionOptions = new ObservableCollection<string> { "全部" };
            StrategyHoldingOptions = new ObservableCollection<string> { "全部" };
            SuggestionOptions = new ObservableCollection<string> { "全部" };

            _rankedStocksView = System.Windows.Data.CollectionViewSource.GetDefaultView(RankedStocks);
            _rankedStocksView.Filter = FilterRankedStocks;

            LoadSavedRanking();
            LoadNotificationEmailList();
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

        public string NotificationEmailList
        {
            get => _notificationEmailList;
            set
            {
                _notificationEmailList = value ?? string.Empty;
                OnPropertyChanged();
                SaveNotificationEmailList();
            }
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

        public ObservableCollection<string> PatternTagOptions { get; }

        public ObservableCollection<string> StrategyActionOptions { get; }

        public ObservableCollection<string> StrategyHoldingOptions { get; }

        public ObservableCollection<string> SuggestionOptions { get; }

        public string SelectedPatternTag
        {
            get => _selectedPatternTag;
            set
            {
                _selectedPatternTag = string.IsNullOrWhiteSpace(value) ? "全部" : value;
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
        }

        public string SelectedStrategyHolding
        {
            get => _selectedStrategyHolding;
            set
            {
                _selectedStrategyHolding = string.IsNullOrWhiteSpace(value) ? "全部" : value;
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
        }

        public string SelectedSuggestion
        {
            get => _selectedSuggestion;
            set
            {
                _selectedSuggestion = string.IsNullOrWhiteSpace(value) ? "全部" : value;
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
        }

        public bool IsControlPanelExpanded
        {
            get => _isControlPanelExpanded;
            set
            {
                _isControlPanelExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ControlPanelVisibility));
                OnPropertyChanged(nameof(ControlPanelToggleText));
            }
        }

        public System.Windows.Visibility ControlPanelVisibility => IsControlPanelExpanded ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string ControlPanelToggleText => IsControlPanelExpanded ? "收起條件面板" : "展開條件面板";

        public string SelectedStrategyAction
        {
            get => _selectedStrategyAction;
            set
            {
                _selectedStrategyAction = string.IsNullOrWhiteSpace(value) ? "全部" : value;
                OnPropertyChanged();
                _rankedStocksView.Refresh();
            }
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
        public ICommand ToggleControlPanelCommand { get; }
        public ICommand ToggleExportCsvCommand { get; }
        public ICommand PublishWebsiteCommand { get; }

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
                if (!string.IsNullOrWhiteSpace(SelectedPatternTag) && SelectedPatternTag != "全部")
                {
                    if (string.IsNullOrWhiteSpace(stock.PatternTagsText) || stock.PatternTagsText.IndexOf(SelectedPatternTag, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
                if (!string.IsNullOrWhiteSpace(SelectedStrategyAction) && SelectedStrategyAction != "全部")
                {
                    if (string.IsNullOrWhiteSpace(stock.StrategyActionText) ||
                        stock.StrategyActionText.IndexOf(SelectedStrategyAction, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
                if (!string.IsNullOrWhiteSpace(SelectedStrategyHolding) && SelectedStrategyHolding != "全部")
                {
                    if (string.IsNullOrWhiteSpace(stock.StrategyStageLabel) ||
                        stock.StrategyStageLabel.IndexOf(SelectedStrategyHolding, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
                if (!string.IsNullOrWhiteSpace(SelectedSuggestion) && SelectedSuggestion != "全部")
                {
                    if (string.IsNullOrWhiteSpace(stock.Suggestion) ||
                        stock.Suggestion.IndexOf(SelectedSuggestion, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
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
                bool hasCrashRiskScoreColumn = false;
                bool hasPatternTagCountColumn = false;
                bool hasPatternTagsColumn = false;
                bool hasStrategyDecisionColumn = false;
                bool hasStrategyActionTextColumn = false;
                bool hasStrategyStageLabelColumn = false;
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

                            if (colName == "CrashRiskScore")
                            {
                                hasCrashRiskScoreColumn = true;
                            }

                            if (colName == "PatternTagCount")
                            {
                                hasPatternTagCountColumn = true;
                            }

                            if (colName == "PatternTags")
                            {
                                hasPatternTagsColumn = true;
                            }

                            if (colName == "StrategyDecision")
                            {
                                hasStrategyDecisionColumn = true;
                            }

                            if (colName == "StrategyActionText")
                            {
                                hasStrategyActionTextColumn = true;
                            }

                            if (colName == "StrategyStageLabel")
                            {
                                hasStrategyStageLabelColumn = true;
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
                            CrashRiskScore INTEGER NOT NULL DEFAULT 0,
                            PatternTagCount INTEGER NOT NULL DEFAULT 0,
                            PatternTags TEXT NOT NULL DEFAULT '',
                            Suggestion TEXT NOT NULL,
                            StrategyDecision TEXT NOT NULL DEFAULT '',
                            StrategyActionText TEXT NOT NULL DEFAULT '',
                            StrategyStageLabel TEXT NOT NULL DEFAULT '',
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

                if (!hasCrashRiskScoreColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN CrashRiskScore INTEGER NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasPatternTagCountColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN PatternTagCount INTEGER NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasPatternTagsColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN PatternTags TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasStrategyDecisionColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN StrategyDecision TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasStrategyActionTextColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN StrategyActionText TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasStrategyStageLabelColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN StrategyStageLabel TEXT NOT NULL DEFAULT '';";
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
                        cmd.CommandText = "SELECT Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, CrashRiskScore, PatternTagCount, PatternTags, Suggestion, StrategyDecision, StrategyActionText, StrategyStageLabel, ThreeMajorNet, ThreeMajorNetAmount, RecentScores FROM LatestRanking ORDER BY Rank ASC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var recentScoresRaw = reader.IsDBNull(16) ? string.Empty : reader.GetString(16);
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
                                    CrashRiskScore = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                                    PatternTagCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                                    PatternTagsText = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                    Suggestion = reader.GetString(10),
                                    StrategyDecision = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                                    StrategyActionText = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                                    StrategyStageLabel = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                                    ThreeMajorNet = reader.IsDBNull(14) ? 0 : reader.GetInt64(14),
                                    ThreeMajorNetAmount = reader.IsDBNull(15) ? 0m : Convert.ToDecimal(reader.GetValue(15), CultureInfo.InvariantCulture),
                                    RecentScores = DeserializeRecentScores(recentScoresRaw, reader.GetInt32(5))
                                });
                            }
                        }
                    }
                }

                foreach (var stock in loaded)
                {
                    if (stock.PatternTagCount <= 0 && !string.IsNullOrWhiteSpace(stock.PatternTagsText))
                    {
                        stock.PatternTagCount = stock.PatternTagsText
                            .Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries)
                            .Length;
                    }
                }

                if (loaded.Count > 0)
                {
                    foreach (var s in loaded)
                        RankedStocks.Add(s);
                    UpdateScoreHeaders(loaded);
                    UpdatePatternTagOptions(loaded);
                    UpdateStrategyActionOptions(loaded);
                    UpdateStrategyHoldingOptions(loaded);
                    UpdateSuggestionOptions(loaded);
                    ProgressText = $"已載入上次儲存的排行 ({loaded.Count} 筆)";
                }
                else
                {
                    UpdateScoreHeaders(null);
                    UpdatePatternTagOptions(null);
                    UpdateStrategyActionOptions(null);
                    UpdateStrategyHoldingOptions(null);
                    UpdateSuggestionOptions(null);
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
                                INSERT INTO LatestRanking (Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, CrashRiskScore, PatternTagCount, PatternTags, Suggestion, StrategyDecision, StrategyActionText, StrategyStageLabel, ThreeMajorNet, ThreeMajorNetAmount, RecentScores)
                                VALUES (@rank, @sym, @name, @price, @change, @score, @scoreDate, @crashRiskScore, @patternTagCount, @patternTags, @sugg, @strategyDecision, @strategyActionText, @strategyStageLabel, @net, @netAmount, @recentScores)";
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
                                cmd.Parameters.AddWithValue("@crashRiskScore", s.CrashRiskScore);
                                cmd.Parameters.AddWithValue("@patternTagCount", s.PatternTagCount);
                                cmd.Parameters.AddWithValue("@patternTags", s.PatternTagsText ?? string.Empty);
                                cmd.Parameters.AddWithValue("@sugg", s.Suggestion);
                                cmd.Parameters.AddWithValue("@strategyDecision", s.StrategyDecision ?? string.Empty);
                                cmd.Parameters.AddWithValue("@strategyActionText", s.StrategyActionText ?? string.Empty);
                                cmd.Parameters.AddWithValue("@strategyStageLabel", s.StrategyStageLabel ?? string.Empty);
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

        public Task StartScanningAsync()
        {
            return ScanAllStocksAsync();
        }

        private async Task PublishWebsiteByHandAsync()
        {
            if (_isPublishingWebsite)
            {
                return;
            }

            _isPublishingWebsite = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                ProgressText = "手動發佈網站中...";
                await _mainViewModel.PublishRankingWebsiteByHandAsync(this);
                ProgressText = "手動發佈完成。";
            }
            catch (Exception ex)
            {
                ProgressText = "手動發佈失敗: " + ex.Message;
            }
            finally
            {
                _isPublishingWebsite = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public void ExportLatestRankingToXmlSaveFile()
        {
            var sfg = new Microsoft.Win32.SaveFileDialog();
            sfg.Filter = "XML file (*.xml)|*.xml";
            sfg.DefaultExt = ".xml";
            if (sfg.ShowDialog() == true)
            {
                ExportLatestRankingToXml(sfg.FileName);
                MessageBox.Show("Save success!");
            }
            else
            {
                MessageBox.Show("Save canceled.");
            }
        }


        public string ExportLatestRankingToXml(string outputDirectory = null)
        {
            var filePath = ResolveExportFilePath(outputDirectory, "xml");
            var exportStocks = GetCurrentViewStocks();

            var doc = new XDocument(
                new XElement("RankingSnapshot",
                    new XAttribute("generatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    exportStocks.Select(s => new XElement("Stock",
                        new XAttribute("rank", s.Rank),
                        new XElement("Symbol", s.Symbol ?? string.Empty),
                        new XElement("Name", s.Name ?? string.Empty),
                        new XElement("Score", s.Score),
                        new XElement("ScoreDate", s.ScoreDate == DateTime.MinValue ? string.Empty : s.ScoreDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement("CrashRiskScore", s.CrashRiskScore),
                        new XElement("PatternTagCount", s.PatternTagCount),
                        new XElement("PatternTags", s.PatternTagsText ?? string.Empty),
                        new XElement("StrategyDecision", s.StrategyDecision ?? string.Empty),
                        new XElement("StrategyActionText", s.StrategyActionText ?? string.Empty),
                        new XElement("StrategyHolding", s.StrategyStageLabel ?? string.Empty),
                        new XElement("Suggestion", s.Suggestion ?? string.Empty),
                        new XElement("LatestPrice", s.LatestPrice.ToString(CultureInfo.InvariantCulture)),
                        new XElement("ChangePercent", s.ChangePercent.ToString(CultureInfo.InvariantCulture)),
                        new XElement("ThreeMajorNet", s.ThreeMajorNet),
                        new XElement("ThreeMajorNetAmount", s.ThreeMajorNetAmount.ToString(CultureInfo.InvariantCulture))
                    ))));

            doc.Save(filePath);
            return filePath;
        }

        public string ExportLatestRankingToHtml(string outputDirectory = null)
        {
            var filePath = ResolveExportFilePath(outputDirectory, "html");
            File.WriteAllText(filePath, BuildRankingWebsiteHtml(), Encoding.UTF8);
            return filePath;
        }

        public string BuildRankingWebsiteHtml()
        {
            var exportStocks = GetCurrentViewStocks();
            var rows = string.Join("\n", exportStocks.Select(s =>
                $"<tr>" +
                $"<td>{s.Rank}</td>" +
                $"<td>{HtmlEncode(s.Symbol)}</td>" +
                $"<td>{HtmlEncode(s.Name)}</td>" +
                $"<td>{s.Score}</td>" +
                $"<td>{s.CrashRiskScore}</td>" +
                $"<td>{s.PatternTagCount}</td>" +
                $"<td>{HtmlEncode(s.PatternTagsText)}</td>" +
                $"<td>{s.ScoreDay0}</td>" +
                $"<td>{s.ScoreDay1}</td>" +
                $"<td>{s.ScoreDay2}</td>" +
                $"<td>{s.ScoreDay3}</td>" +
                $"<td>{s.ScoreDay4}</td>" +
                $"<td>{s.AverageRecentScore.ToString("F1", CultureInfo.InvariantCulture)}</td>" +
                $"<td>{s.ScoreTrend}</td>" +
                $"<td>{s.ThreeMajorNet}</td>" +
                $"<td>{s.ThreeMajorNetAmount.ToString(CultureInfo.InvariantCulture)}</td>" +
                $"<td>{HtmlEncode(s.StrategyActionText)}</td>" +
                $"<td>{HtmlEncode(s.StrategyStageLabel)}</td>" +
                $"<td>{HtmlEncode(s.Suggestion)}</td>" +
                $"<td>{s.LatestPrice.ToString("F2", CultureInfo.InvariantCulture)}</td>" +
                $"<td>{s.ChangePercent.ToString("F2", CultureInfo.InvariantCulture)}</td>" +
                $"</tr>"));

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"zh-Hant\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\" />");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            html.AppendLine("<title>StockTracker 全市場排名</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{background:#121212;color:#e6e6e6;font-family:'Segoe UI',sans-serif;margin:0;padding:20px;}");
            html.AppendLine(".panel{background:#1e1e1e;border:1px solid #303030;border-radius:8px;padding:12px;margin-bottom:12px;}");
            html.AppendLine(".row{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:8px;}");
            html.AppendLine("label{font-size:12px;color:#c8c8c8;}");
            html.AppendLine("input,select{background:#222;color:#fff;border:1px solid #444;border-radius:4px;padding:6px 8px;min-width:78px;}");
            html.AppendLine("input[type=checkbox]{min-width:auto;}");
            html.AppendLine("table{width:100%;border-collapse:collapse;font-size:13px;}");
            html.AppendLine("th,td{border:1px solid #2f2f2f;padding:6px 8px;text-align:left;}");
            html.AppendLine("th{background:#2b2b2b;cursor:pointer;position:sticky;top:0;}");
            html.AppendLine("tr:nth-child(even){background:#1a1a1a;}");
            html.AppendLine(".muted{color:#9aa0a6;font-size:12px;}");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine($"<h2>全市場掃描排名</h2><div class=\"muted\">更新時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");
            html.AppendLine("<div class=\"panel\">");
            html.AppendLine("<div class='row'><label>搜尋</label><input id='searchInput' placeholder='代號/名稱/策略/建議/型態' /><label>Top</label><input id='topCount' type='number' min='1' placeholder='100' /><label>價格</label><input id='minPrice' type='number' step='0.01' placeholder='Min' /><input id='maxPrice' type='number' step='0.01' placeholder='Max' /><label>漲跌幅%</label><input id='minChange' type='number' step='0.01' placeholder='Min' /><input id='maxChange' type='number' step='0.01' placeholder='Max' /></div>");
            html.AppendLine("<div class='row'><label>三大法人買賣超</label><input id='minNet' type='number' step='1' placeholder='Min' /><input id='maxNet' type='number' step='1' placeholder='Max' /><label>買賣超金額</label><input id='minNetAmount' type='number' step='1' placeholder='Min' /><input id='maxNetAmount' type='number' step='1' placeholder='Max' /><label>最新分數≥</label><input id='minScore' type='number' step='1' placeholder='0' /><label>風險分≥</label><input id='minCrash' type='number' step='1' placeholder='0' /><label>型態數≥</label><input id='minPatternCount' type='number' step='1' placeholder='0' /></div>");
            html.AppendLine("<div class='row'><label>指定型態</label><select id='patternFilter'><option value=''>全部</option></select><label>策略動作</label><select id='actionFilter'><option value=''>全部</option></select><label>建議倉位</label><select id='holdingFilter'><option value=''>全部</option></select><label>建議</label><select id='suggestionFilter'><option value=''>全部</option></select><label>近5日均分≥</label><input id='minAvg' type='number' step='0.1' placeholder='0' /><label><input id='trendUp' type='checkbox' /> 近5日分數趨勢上升</label></div>");
            html.AppendLine("<div class='row'><label>連續天數</label><input id='minConDays' type='number' step='1' placeholder='0' /><label>分數≥</label><input id='minConScore' type='number' step='1' placeholder='60' value='60' /></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class=\"panel\"><table id=\"rankingTable\"><thead><tr>");
            html.AppendLine("<th data-type='num'>排名</th><th data-type='text'>代號</th><th data-type='text'>名稱</th><th data-type='num'>分數</th><th data-type='num'>風險分</th><th data-type='num'>型態數</th><th data-type='text'>型態</th><th data-type='num'>D0</th><th data-type='num'>D1</th><th data-type='num'>D2</th><th data-type='num'>D3</th><th data-type='num'>D4</th><th data-type='num'>近5日均分</th><th data-type='num'>趨勢</th><th data-type='num'>三大法人買賣超</th><th data-type='num'>三大法人買賣超金額</th><th data-type='text'>策略動作</th><th data-type='text'>建議倉位</th><th data-type='text'>建議</th><th data-type='num'>最新價</th><th data-type='num'>漲跌幅</th>");
            html.AppendLine("</tr></thead><tbody>");
            html.AppendLine(rows);
            html.AppendLine("</tbody></table></div>");
            html.AppendLine("<script>");
            html.AppendLine("const table=document.getElementById('rankingTable');const tbody=table.tBodies[0];const $=id=>document.getElementById(id);const f={search:$('searchInput'),top:$('topCount'),minPrice:$('minPrice'),maxPrice:$('maxPrice'),minChange:$('minChange'),maxChange:$('maxChange'),minNet:$('minNet'),maxNet:$('maxNet'),minNetAmount:$('minNetAmount'),maxNetAmount:$('maxNetAmount'),minScore:$('minScore'),minCrash:$('minCrash'),minPatternCount:$('minPatternCount'),pattern:$('patternFilter'),action:$('actionFilter'),holding:$('holdingFilter'),suggestion:$('suggestionFilter'),minAvg:$('minAvg'),trendUp:$('trendUp'),minConDays:$('minConDays'),minConScore:$('minConScore')};");
            html.AppendLine("function num(v){const n=parseFloat(v);return Number.isFinite(n)?n:null;}function txt(cell){return (cell.textContent||'').trim();}function lower(cell){return txt(cell).toLowerCase();}");
            html.AppendLine("function fillSelect(col,sel){const vals=[...new Set([...tbody.rows].map(r=>txt(r.cells[col])).filter(x=>x))].sort((a,b)=>a.localeCompare(b,'zh-Hant'));vals.forEach(v=>{const o=document.createElement('option');o.value=v;o.textContent=v;sel.appendChild(o);});}");
            html.AppendLine("fillSelect(6,f.pattern);fillSelect(16,f.action);fillSelect(17,f.holding);fillSelect(18,f.suggestion);");
            html.AppendLine("function passRange(value,min,max){if(min!==null&&value<min)return false;if(max!==null&&value>max)return false;return true;}");
            html.AppendLine("function consecutiveDays(row,minScore){const d=[num(txt(row.cells[7]))||0,num(txt(row.cells[8]))||0,num(txt(row.cells[9]))||0,num(txt(row.cells[10]))||0,num(txt(row.cells[11]))||0];let s=0;for(const v of d){if(v<minScore)break;s++;}return s;}");
            html.AppendLine("function applyFilter(){const kw=(f.search.value||'').trim().toLowerCase();const top=num(f.top.value);const minPrice=num(f.minPrice.value),maxPrice=num(f.maxPrice.value),minChange=num(f.minChange.value),maxChange=num(f.maxChange.value),minNet=num(f.minNet.value),maxNet=num(f.maxNet.value),minNetAmount=num(f.minNetAmount.value),maxNetAmount=num(f.maxNetAmount.value),minScore=num(f.minScore.value),minCrash=num(f.minCrash.value),minPatternCount=num(f.minPatternCount.value),minAvg=num(f.minAvg.value),minConDays=Math.max(0,num(f.minConDays.value)||0),minConScore=num(f.minConScore.value)??60;const pattern=f.pattern.value,action=f.action.value,holding=f.holding.value,suggestion=f.suggestion.value,trendUp=f.trendUp.checked;[...tbody.rows].forEach(r=>{const rank=num(txt(r.cells[0]))||0;const score=num(txt(r.cells[3]))||0;const crash=num(txt(r.cells[4]))||0;const pcount=num(txt(r.cells[5]))||0;const avg=num(txt(r.cells[12]))||0;const trend=num(txt(r.cells[13]))||0;const net=num(txt(r.cells[14]))||0;const netAmount=num(txt(r.cells[15]))||0;const price=num(txt(r.cells[19]))||0;const chg=num(txt(r.cells[20]))||0;const text=r.textContent.toLowerCase();let ok=true;if(top!==null&&rank>top)ok=false;if(kw&&text.indexOf(kw)<0)ok=false;if(!passRange(price,minPrice,maxPrice))ok=false;if(!passRange(chg,minChange,maxChange))ok=false;if(!passRange(net,minNet,maxNet))ok=false;if(!passRange(netAmount,minNetAmount,maxNetAmount))ok=false;if(minScore!==null&&score<minScore)ok=false;if(minCrash!==null&&crash<minCrash)ok=false;if(minPatternCount!==null&&pcount<minPatternCount)ok=false;if(pattern&&lower(r.cells[6]).indexOf(pattern.toLowerCase())<0)ok=false;if(action&&txt(r.cells[16])!==action)ok=false;if(holding&&txt(r.cells[17])!==holding)ok=false;if(suggestion&&txt(r.cells[18])!==suggestion)ok=false;if(minAvg!==null&&avg<minAvg)ok=false;if(trendUp&&trend<=0)ok=false;if(minConDays>0&&consecutiveDays(r,minConScore)<minConDays)ok=false;r.style.display=ok?'':'none';});}");
            html.AppendLine("Object.values(f).forEach(el=>{if(!el)return;const evt=(el.type==='checkbox'||el.tagName==='SELECT')?'change':'input';el.addEventListener(evt,applyFilter);});");
            html.AppendLine("let sortState={idx:0,asc:true};[...table.tHead.rows[0].cells].forEach((th,idx)=>{th.addEventListener('click',()=>{const type=th.dataset.type||'text';sortState.asc=(sortState.idx===idx)?!sortState.asc:true;sortState.idx=idx;const rows=[...tbody.rows];rows.sort((a,b)=>{let va=txt(a.cells[idx]),vb=txt(b.cells[idx]);if(type==='num'){va=parseFloat(va)||0;vb=parseFloat(vb)||0;return sortState.asc?va-vb:vb-va;}return sortState.asc?va.localeCompare(vb,'zh-Hant'):vb.localeCompare(va,'zh-Hant');});rows.forEach(r=>tbody.appendChild(r));applyFilter();});});");
            html.AppendLine("</script>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        public IReadOnlyList<string> GetNotificationEmailRecipients()
        {
            var raw = NotificationEmailList ?? string.Empty;
            return raw.Split(new[] { ';', ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Contains("@"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

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

                            // 與主頁分數邏輯一致：先把法人歷史注入，再用同一組輸入計算最新分數
                            dummyVm.SetTwseT86Records(t86History?.RecordsByDate?.Values);
                            var latestRecommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                                enrichedCandles,
                                (double)dummyVm.LatestPrice,
                                (double?)dummyVm.ChangePercent,
                                enrichedCandles.Count > 1 ? (double)enrichedCandles[enrichedCandles.Count - 2].Close : (double)dummyVm.LatestPrice,
                                t86History,
                                enrichedCandles.Last().Time);

                            var recentAnalysis = BuildRecentAnalysis(enrichedCandles, t86History, symbol, name);
                            var recentScores = recentAnalysis.RecentScores;
                            var recentRecommendations = recentAnalysis.RecentRecommendations;
                            if (recentScores.Count > 0)
                            {
                                recentScores[0].Score = latestRecommendation.Score;
                                recentScores[0].Date = enrichedCandles.Last().Time.Date;
                            }
                            else
                            {
                                recentScores.Add(new RankedStockScorePoint
                                {
                                    Date = enrichedCandles.Last().Time.Date,
                                    Score = latestRecommendation.Score
                                });
                            }

                            var latestScore = latestRecommendation.Score;
                            var scoreDate = enrichedCandles.Last().Time.Date;
                            var previousMa20 = enrichedCandles.Count > 1 ? (double?)enrichedCandles[enrichedCandles.Count - 2].MA20 : null;
                            var strategyOutput = AdvancedTradingStrategyEngine.EvaluateStrategy(
                                latestRecommendation,
                                recentRecommendations,
                                0d,
                                (double)dummyVm.LatestPrice,
                                dummyVm.MA5,
                                dummyVm.MA20,
                                previousMa20,
                                0d);

                            long latestNet = ResolveThreeMajorNetByDate(t86History, scoreDate);

                            lock (lockObj)
                            {
                                var latestPatternTags = latestRecommendation.PatternTags ?? new List<PatternTag>();
                                results.Add(new RankedStock
                                {
                                    Symbol = symbol,
                                    Name = name,
                                    LatestPrice = dummyVm.LatestPrice,
                                    ChangePercent = dummyVm.ChangePercent,
                                    Score = latestScore,
                                    ScoreDate = scoreDate,
                                    CrashRiskScore = latestRecommendation.CrashRiskScore,
                                    PatternTagCount = latestPatternTags.Count,
                                    PatternTagsText = string.Join("、", latestPatternTags.Select(x => x.Label).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                                    StrategyDecision = strategyOutput.GlobalDecision,
                                    StrategyActionText = strategyOutput.ActionText,
                                    StrategyStageLabel = strategyOutput.StageLabel,
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
                UpdatePatternTagOptions(results);
                UpdateStrategyActionOptions(results);
                UpdateStrategyHoldingOptions(results);
                UpdateSuggestionOptions(results);
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

        private sealed class RecentAnalysisResult
        {
            public List<RankedStockScorePoint> RecentScores { get; set; } = new List<RankedStockScorePoint>();
            public List<TrendRecommendationResult> RecentRecommendations { get; set; } = new List<TrendRecommendationResult>();
            public TrendRecommendationResult LatestRecommendation => RecentRecommendations.Count == 0 ? null : RecentRecommendations[RecentRecommendations.Count - 1];
        }

        private static RecentAnalysisResult BuildRecentAnalysis(List<CandleData> candles, TwseT86History t86History, string symbol, string name)
        {
            var result = new RecentAnalysisResult();
            if (candles == null || candles.Count == 0)
            {
                return result;
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

                result.RecentRecommendations.Add(recommendation);
                result.RecentScores.Add(new RankedStockScorePoint
                {
                    Date = latestCandle.Time.Date,
                    Score = recommendation.Score
                });
            }

            result.RecentScores = result.RecentScores
                .OrderByDescending(x => x.Date)
                .ToList();
            return result;
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

        private void UpdatePatternTagOptions(IEnumerable<RankedStock> stocks)
        {
            var selected = SelectedPatternTag;
            var tags = (stocks ?? Enumerable.Empty<RankedStock>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.PatternTagsText))
                .SelectMany(x => x.PatternTagsText.Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            PatternTagOptions.Clear();
            PatternTagOptions.Add("全部");
            foreach (var tag in tags)
            {
                PatternTagOptions.Add(tag);
            }

            SelectedPatternTag = PatternTagOptions.Contains(selected) ? selected : "全部";
        }

        private void UpdateStrategyActionOptions(IEnumerable<RankedStock> stocks)
        {
            var selected = SelectedStrategyAction;
            var actions = (stocks ?? Enumerable.Empty<RankedStock>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StrategyActionText))
                .Select(x => x.StrategyActionText.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            StrategyActionOptions.Clear();
            StrategyActionOptions.Add("全部");
            foreach (var action in actions)
            {
                StrategyActionOptions.Add(action);
            }

            SelectedStrategyAction = StrategyActionOptions.Contains(selected) ? selected : "全部";
        }

        private void UpdateStrategyHoldingOptions(IEnumerable<RankedStock> stocks)
        {
            var selected = SelectedStrategyHolding;
            var holdings = (stocks ?? Enumerable.Empty<RankedStock>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StrategyStageLabel))
                .Select(x => x.StrategyStageLabel.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            StrategyHoldingOptions.Clear();
            StrategyHoldingOptions.Add("全部");
            foreach (var holding in holdings)
            {
                StrategyHoldingOptions.Add(holding);
            }

            SelectedStrategyHolding = StrategyHoldingOptions.Contains(selected) ? selected : "全部";
        }

        private void UpdateSuggestionOptions(IEnumerable<RankedStock> stocks)
        {
            var selected = SelectedSuggestion;
            var suggestions = (stocks ?? Enumerable.Empty<RankedStock>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Suggestion))
                .Select(x => x.Suggestion.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            SuggestionOptions.Clear();
            SuggestionOptions.Add("全部");
            foreach (var suggestion in suggestions)
            {
                SuggestionOptions.Add(suggestion);
            }

            SelectedSuggestion = SuggestionOptions.Contains(selected) ? selected : "全部";
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
                _selectedPatternTag = "全部";
                _selectedStrategyAction = "全部";
                _selectedStrategyHolding = "全部";
                _selectedSuggestion = "全部";
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
            OnPropertyChanged(nameof(SelectedPatternTag));
            OnPropertyChanged(nameof(SelectedStrategyAction));
            OnPropertyChanged(nameof(SelectedStrategyHolding));
            OnPropertyChanged(nameof(SelectedSuggestion));
            OnPropertyChanged(nameof(MinAverageScoreFilter));
            OnPropertyChanged(nameof(RequireScoreTrendUp));
            OnPropertyChanged(nameof(MinConsecutiveDays));
            OnPropertyChanged(nameof(MinConsecutiveScore));
            _rankedStocksView.Refresh();
        }

        private void LoadNotificationEmailList()
        {
            try
            {
                if (File.Exists(_notificationEmailListPath))
                {
                    _notificationEmailList = File.ReadAllText(_notificationEmailListPath, Encoding.UTF8);
                }
                else
                {
                    _notificationEmailList = string.Empty;
                }
            }
            catch
            {
                _notificationEmailList = string.Empty;
            }

            OnPropertyChanged(nameof(NotificationEmailList));
        }

        private void SaveNotificationEmailList()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_notificationEmailListPath));
                File.WriteAllText(_notificationEmailListPath, _notificationEmailList ?? string.Empty, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string ResolveExportFilePath(string outputPathOrDirectory, string extension)
        {
            var ext = "." + extension.TrimStart('.');
            if (string.IsNullOrWhiteSpace(outputPathOrDirectory))
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, $"Ranking_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            }

            if (Path.HasExtension(outputPathOrDirectory))
            {
                var filePath = outputPathOrDirectory;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                return filePath;
            }

            Directory.CreateDirectory(outputPathOrDirectory);
            return Path.Combine(outputPathOrDirectory, $"Ranking_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        }

        private static string HtmlEncode(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private IReadOnlyList<RankedStock> GetCurrentViewStocks()
        {
            if (RankedStocksView != null)
            {
                return RankedStocksView
                    .Cast<object>()
                    .OfType<RankedStock>()
                    .ToList();
            }

            return RankedStocks.ToList();
        }
    }
}
