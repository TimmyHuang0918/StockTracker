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
        public long ForeignNet { get; set; }
        public long DealerNet { get; set; }
        public long InvestmentTrustNet { get; set; }
        public string ScoreReason { get; set; }
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
        public string ForeignNetDisplay
        {
            get
            {
                var lots = ForeignNet / 1000m;
                return lots > 0 ? $"+{lots:N0}" : lots.ToString("N0", CultureInfo.InvariantCulture);
            }
        }
        public System.Windows.Media.Brush ForeignNetBrush => ForeignNet > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                              ForeignNet < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                              System.Windows.Media.Brushes.Gray;
        public string DealerNetDisplay
        {
            get
            {
                var lots = DealerNet / 1000m;
                return lots > 0 ? $"+{lots:N0}" : lots.ToString("N0", CultureInfo.InvariantCulture);
            }
        }
        public System.Windows.Media.Brush DealerNetBrush => DealerNet > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                             DealerNet < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                             System.Windows.Media.Brushes.Gray;
        public string TrustNetDisplay
        {
            get
            {
                var lots = InvestmentTrustNet / 1000m;
                return lots > 0 ? $"+{lots:N0}" : lots.ToString("N0", CultureInfo.InvariantCulture);
            }
        }
        public System.Windows.Media.Brush TrustNetBrush => InvestmentTrustNet > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                            InvestmentTrustNet < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
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
        private int _topCount = 10000;
        private bool _isControlPanelExpanded = true;
        private bool _isPublishingWebsite;
        private string _scoreDay0Header = "D0";
        private string _scoreDay1Header = "D1";
        private string _scoreDay2Header = "D2";
        private string _scoreDay3Header = "D3";
        private string _scoreDay4Header = "D4";
        private RankedStock _stock0050;

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

        public RankedStock Stock0050
        {
            get => _stock0050;
            private set
            {
                _stock0050 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Has0050Data));
            }
        }

        public bool Has0050Data => Stock0050 != null;

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
                if (MinCrashRiskScoreFilter.HasValue && stock.CrashRiskScore > MinCrashRiskScoreFilter.Value) return false;
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
                bool hasScoreReasonColumn = false;
                bool hasForeignNetColumn = false;
                bool hasDealerNetColumn = false;
                bool hasInvestmentTrustNetColumn = false;
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

                            if (colName == "ScoreReason")
                            {
                                hasScoreReasonColumn = true;
                            }

                            if (colName == "ForeignNet")
                            {
                                hasForeignNetColumn = true;
                            }

                            if (colName == "DealerNet")
                            {
                                hasDealerNetColumn = true;
                            }

                            if (colName == "InvestmentTrustNet")
                            {
                                hasInvestmentTrustNetColumn = true;
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
                            RecentScores TEXT NOT NULL DEFAULT '',
                            ScoreReason TEXT NOT NULL DEFAULT ''
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

                if (!hasScoreReasonColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN ScoreReason TEXT NOT NULL DEFAULT '';";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasForeignNetColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN ForeignNet INTEGER NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasDealerNetColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN DealerNet INTEGER NOT NULL DEFAULT 0;";
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!hasInvestmentTrustNetColumn)
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "ALTER TABLE LatestRanking ADD COLUMN InvestmentTrustNet INTEGER NOT NULL DEFAULT 0;";
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
                        cmd.CommandText = "SELECT Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, CrashRiskScore, PatternTagCount, PatternTags, Suggestion, StrategyDecision, StrategyActionText, StrategyStageLabel, ThreeMajorNet, ThreeMajorNetAmount, RecentScores, ScoreReason, ForeignNet, DealerNet, InvestmentTrustNet FROM LatestRanking ORDER BY Rank ASC";
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
                                    RecentScores = DeserializeRecentScores(recentScoresRaw, reader.GetInt32(5)),
                                    ScoreReason = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                                    ForeignNet = reader.IsDBNull(18) ? 0 : reader.GetInt64(18),
                                    DealerNet = reader.IsDBNull(19) ? 0 : reader.GetInt64(19),
                                    InvestmentTrustNet = reader.IsDBNull(20) ? 0 : reader.GetInt64(20)
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
                    Stock0050 = loaded.FirstOrDefault(s => s.Symbol == "0050");
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
                                INSERT INTO LatestRanking (Rank, Symbol, Name, LatestPrice, ChangePercent, Score, ScoreDate, CrashRiskScore, PatternTagCount, PatternTags, Suggestion, StrategyDecision, StrategyActionText, StrategyStageLabel, ThreeMajorNet, ThreeMajorNetAmount, RecentScores, ScoreReason, ForeignNet, DealerNet, InvestmentTrustNet)
                                VALUES (@rank, @sym, @name, @price, @change, @score, @scoreDate, @crashRiskScore, @patternTagCount, @patternTags, @sugg, @strategyDecision, @strategyActionText, @strategyStageLabel, @net, @netAmount, @recentScores, @scoreReason, @foreignNet, @dealerNet, @trustNet)";
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
                                cmd.Parameters.AddWithValue("@scoreReason", s.ScoreReason ?? string.Empty);
                                cmd.Parameters.AddWithValue("@foreignNet", s.ForeignNet);
                                cmd.Parameters.AddWithValue("@dealerNet", s.DealerNet);
                                cmd.Parameters.AddWithValue("@trustNet", s.InvestmentTrustNet);
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
                        new XElement("ScoreReason", s.ScoreReason ?? string.Empty),
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

        // 輔助方法：格式化法人買賣超張數（加入正負號與千分位）
        private static string FormatNetShares(double value)
        {
            if (value == 0) return "0";
            string sign = value > 0 ? "+" : "";
            return $"{sign}{value:N0}"; // N0 會格式化為 +1,234 或 -567
        }

        // 輔助方法：格式化買賣金額（轉為億、萬單位，並加上正負號）
        private static string FormatMoney(double value)
        {
            if (value == 0) return "0";
            string sign = value > 0 ? "+" : "";
            double absVal = Math.Abs(value);

            // 假設原本欄位數值單位是「元」
            if (absVal >= 100000000) // 大於等於 1 億
            {
                return $"{sign}{(value / 100000000).ToString("F2", CultureInfo.InvariantCulture)}億";
            }
            if (absVal >= 10000) // 大於等於 1 萬
            {
                return $"{sign}{(value / 10000).ToString("F0", CultureInfo.InvariantCulture)}萬";
            }
            return $"{sign}{value:N0}";
        }

        public string BuildRankingWebsiteHtml()
        {
            var exportStocks = GetCurrentViewStocks();
            var latestScoreDate = exportStocks
                .Where(s => s.ScoreDate != DateTime.MinValue)
                .Select(s => (DateTime?)s.ScoreDate.Date)
                .Max();
            var latestKLineDateText = latestScoreDate.HasValue
                ? latestScoreDate.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                : "無資料";
            var updateSummary = latestScoreDate.HasValue
                ? $"資料日期：{latestScoreDate.Value:yyyy-MM-dd} · 筆數：{exportStocks.Count}"
                : $"筆數：{exportStocks.Count}";

            // Fetch 0050 K-Line data for 120 days
            var kLineData0050Json = "[]";
            try
            {
                var candles0050 = new List<CandleData>();
                int kLineCount = 120;
                MainWindow.BuildDateRangeForBars("日K", kLineCount, out var startDate, out var endDate);

                Action<string, CandleData> onKLineReceived = null;
                onKLineReceived = (symbol, candle) =>
                {
                    if (symbol == "0050")
                    {
                        candles0050.Add(candle);
                    }
                };

                _apiService.KLineDataReceived += onKLineReceived;
                _apiService.RequestKLineByDate("0050", 4, 1, 0, startDate, endDate, 0);

                // Wait for data with timeout
                var startWait = DateTime.UtcNow;
                while (candles0050.Count < kLineCount && (DateTime.UtcNow - startWait).TotalSeconds < 3)
                {
                    System.Threading.Thread.Sleep(50);
                }

                _apiService.KLineDataReceived -= onKLineReceived;

                if (candles0050.Count > 0)
                {
                    candles0050.Sort((a, b) => a.Time.CompareTo(b.Time));

                    // Create a temporary StockViewModel to calculate technical indicators
                    var temp0050Vm = new StockViewModel("0050", "元大台灣50")
                    {
                        SelectedKLineInterval = "日K"
                    };
                    temp0050Vm.LoadCandlesForAnalysis(candles0050);

                    // Get the enriched candles with all indicators calculated
                    var enrichedCandles = temp0050Vm.GetPublicCandles().ToList();

                    // Take last 120 days
                    var last120 = enrichedCandles.Skip(Math.Max(0, enrichedCandles.Count - 120)).ToList();

                    kLineData0050Json = System.Text.Json.JsonSerializer.Serialize(last120.Select(c => new
                    {
                        date = c.Time.ToString("yyyy-MM-dd"),
                        open = Math.Round((double)c.Open, 2),
                        high = Math.Round((double)c.High, 2),
                        low = Math.Round((double)c.Low, 2),
                        close = Math.Round((double)c.Close, 2),
                        volume = (double)c.Volume,
                        ma5 = Math.Round(c.MA5, 2),
                        ma20 = Math.Round(c.MA20, 2),
                        ma120 = Math.Round(c.MA120, 2),
                        ma240 = Math.Round(c.MA240, 2),
                        bbUpper = Math.Round(c.BollingerUpper, 2),
                        bbMiddle = Math.Round(c.BollingerMiddle, 2),
                        bbLower = Math.Round(c.BollingerLower, 2),
                        macd = Math.Round(c.MACD, 4),
                        macdSignal = Math.Round(c.MacdSignal, 4),
                        macdHist = Math.Round(c.MACD - c.MacdSignal, 4),
                        rsi = Math.Round(c.RSI, 2)
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch 0050 K-Line data: {ex.Message}");
            }

            // Extract 0050 data for summary display
            var stock0050 = exportStocks.FirstOrDefault(s => s.Symbol == "0050");
            string stock0050Json = "null";
            if (stock0050 != null)
            {
                stock0050Json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    rank = stock0050.Rank,
                    symbol = HtmlEncode(stock0050.Symbol),
                    name = HtmlEncode(stock0050.Name),
                    score = stock0050.Score,
                    crash = stock0050.CrashRiskScore,
                    pcount = stock0050.PatternTagCount,
                    pattern = HtmlEncode(stock0050.PatternTagsText),
                    d0 = stock0050.ScoreDay0,
                    d1 = stock0050.ScoreDay1,
                    d2 = stock0050.ScoreDay2,
                    d3 = stock0050.ScoreDay3,
                    d4 = stock0050.ScoreDay4,
                    avg = Math.Round((double)stock0050.AverageRecentScore, 1),
                    trend = stock0050.ScoreTrend,
                    net = (double)stock0050.ThreeMajorNet,
                    netStr = FormatNetShares((double)stock0050.ThreeMajorNet),
                    netAmount = (double)stock0050.ThreeMajorNetAmount,
                    netAmountStr = FormatMoney((double)stock0050.ThreeMajorNetAmount),
                    action = HtmlEncode(stock0050.StrategyActionText),
                    stage = HtmlEncode(stock0050.StrategyStageLabel),
                    suggestion = HtmlEncode(stock0050.Suggestion),
                    price = Math.Round((double)stock0050.LatestPrice, 2),
                    chg = Math.Round((double)stock0050.ChangePercent, 2),
                    scoreReason = HtmlEncode(stock0050.ScoreReason ?? string.Empty)
                });
            }

            // 將所有股票資料序列化為 JSON，讓前端 JS 操作原生 Data Array，避免建立數萬個初始 DOM 節點
            var stockDataJson = System.Text.Json.JsonSerializer.Serialize(exportStocks.Select(s => new
            {
                rank = s.Rank,
                symbol = HtmlEncode(s.Symbol),
                name = HtmlEncode(s.Name),
                score = s.Score,
                crash = s.CrashRiskScore,
                pcount = s.PatternTagCount,
                pattern = HtmlEncode(s.PatternTagsText),
                d0 = s.ScoreDay0,
                d1 = s.ScoreDay1,
                d2 = s.ScoreDay2,
                d3 = s.ScoreDay3,
                d4 = s.ScoreDay4,
                avg = Math.Round((double)s.AverageRecentScore, 1),
                trend = s.ScoreTrend,
                net = (double)s.ThreeMajorNet,
                netStr = FormatNetShares((double)s.ThreeMajorNet),
                netClass = ResolveValueColorClass((double)s.ThreeMajorNet),
                netAmount = (double)s.ThreeMajorNetAmount,
                netAmountStr = FormatMoney((double)s.ThreeMajorNetAmount),
                netAmountClass = ResolveValueColorClass((double)s.ThreeMajorNetAmount),
                foreignNet = (double)s.ForeignNet,
                foreignNetStr = FormatNetShares((double)s.ForeignNet),
                foreignNetClass = ResolveValueColorClass((double)s.ForeignNet),
                dealerNet = (double)s.DealerNet,
                dealerNetStr = FormatNetShares((double)s.DealerNet),
                dealerNetClass = ResolveValueColorClass((double)s.DealerNet),
                trustNet = (double)s.InvestmentTrustNet,
                trustNetStr = FormatNetShares((double)s.InvestmentTrustNet),
                trustNetClass = ResolveValueColorClass((double)s.InvestmentTrustNet),
                action = HtmlEncode(s.StrategyActionText),
                stage = HtmlEncode(s.StrategyStageLabel),
                suggestion = HtmlEncode(s.Suggestion),
                price = Math.Round((double)s.LatestPrice, 2),
                chg = Math.Round((double)s.ChangePercent, 2),
                chgClass = ResolveValueColorClass((double)s.ChangePercent),
                searchKey = $"{s.Symbol} {s.Name} {s.PatternTagsText} {s.StrategyActionText} {s.Suggestion}".ToLower(),
                scoreReason = HtmlEncode(s.ScoreReason ?? string.Empty)
            }));

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"zh-Hant\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\" />");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            html.AppendLine("<meta name=\"format-detection\" content=\"telephone=no, date=no, address=no, email=no\" />");
            html.AppendLine($"<title>StockTracker 全市場排名 ({latestKLineDateText})</title>");
            html.AppendLine("<style>");
            html.AppendLine(":root{--bg:#0d1117;--panel-bg:#161b22;--border:#30363d;--text:#c9d1d9;--text-muted:#8b949e;--primary:#1f6feb;--primary-hover:#388bfd;--rise:#ff453a;--fall:#32d74b;--flat:#8e8e93;}");
            html.AppendLine("*{box-sizing:border-box;}");
            html.AppendLine("body{background:var(--bg);color:var(--text);font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;margin:0;padding:16px;line-height:1.5;}");

            html.AppendLine(".header-bar{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:16px;gap:16px;}");
            html.AppendLine(".header-title{flex:1;}");
            html.AppendLine("h2{margin:0 0 4px 0;font-size:22px;font-weight:600;color:#fff;}");
            html.AppendLine(".muted{color:var(--text-muted);font-size:13px;margin:0;}");

            html.AppendLine(".btn-csv{background:var(--primary);color:#fff;border:none;border-radius:6px;padding:8px 16px;font-size:14px;font-weight:600;cursor:pointer;transition:background 0.2s;display:inline-flex;align-items:center;gap:6px;height:38px;white-space:nowrap;box-shadow:0 2px 6px rgba(0,0,0,0.15);}");
            html.AppendLine(".btn-csv:hover{background:var(--primary-hover);}");

            html.AppendLine(".panel{background:var(--panel-bg);border:1px solid var(--border);border-radius:12px;padding:16px;margin-bottom:16px;box-shadow:0 4px 12px rgba(0,0,0,0.15);}");
            html.AppendLine(".hero-card{background:linear-gradient(135deg, #1f2937 0%, #111827 100%);border:2px solid #2563eb;border-radius:12px;padding:24px;margin-bottom:20px;box-shadow:0 8px 24px rgba(37,99,235,0.2);}");
            html.AppendLine(".hero-header{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:20px;flex-wrap:wrap;gap:16px;}");
            html.AppendLine(".hero-title{flex:1;min-width:200px;}");
            html.AppendLine(".hero-title h2{margin:0 0 4px 0;font-size:28px;font-weight:700;color:#fff;display:flex;align-items:center;gap:12px;}");
            html.AppendLine(".hero-title .subtitle{color:var(--text-muted);font-size:14px;}");
            html.AppendLine(".hero-price{text-align:right;}");
            html.AppendLine(".hero-price .price{font-size:32px;font-weight:700;color:#fff;margin:0;}");
            html.AppendLine(".hero-price .change{font-size:18px;font-weight:600;margin:4px 0 0 0;}");
            html.AppendLine(".hero-stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:16px;margin-bottom:20px;}");
            html.AppendLine(".stat-box{background:rgba(255,255,255,0.05);border:1px solid rgba(255,255,255,0.1);border-radius:8px;padding:12px 16px;}");
            html.AppendLine(".stat-label{color:var(--text-muted);font-size:12px;font-weight:600;text-transform:uppercase;margin-bottom:6px;}");
            html.AppendLine(".stat-value{color:#fff;font-size:20px;font-weight:700;}");
            html.AppendLine(".stat-value.large{font-size:24px;}");
            html.AppendLine(".hero-action{background:rgba(59,130,246,0.15);border:1px solid #3b82f6;border-radius:8px;padding:16px;margin-bottom:12px;}");
            html.AppendLine(".hero-action .action-title{color:#3b82f6;font-size:14px;font-weight:700;text-transform:uppercase;margin-bottom:8px;}");
            html.AppendLine(".hero-action .action-content{color:#fff;font-size:16px;font-weight:600;}");
            html.AppendLine(".hero-suggestion{background:rgba(16,185,129,0.15);border:1px solid #10b981;border-radius:8px;padding:16px;}");
            html.AppendLine(".hero-suggestion .suggestion-title{color:#10b981;font-size:14px;font-weight:700;text-transform:uppercase;margin-bottom:8px;}");
            html.AppendLine(".hero-suggestion .suggestion-content{color:#fff;font-size:15px;line-height:1.6;}");
            html.AppendLine(".rank-badge{background:linear-gradient(135deg,#f59e0b,#d97706);color:#fff;padding:6px 12px;border-radius:6px;font-size:16px;font-weight:700;display:inline-block;}");
            html.AppendLine(".filter-grid{display:grid;grid-template-columns:repeat(auto-fill, minmax(220px, 1fr));gap:12px;}");
            html.AppendLine(".filter-group{display:flex;flex-direction:column;gap:4px;}");
            html.AppendLine(".filter-group.row-inputs{flex-direction:row;align-items:center;gap:8px;}");
            html.AppendLine(".filter-group.row-inputs input{width:100%;}");
            html.AppendLine(".checkbox-group{flex-direction:row;align-items:center;gap:8px;padding-top:20px;}");
            html.AppendLine("label{font-size:12px;font-weight:500;color:var(--text-muted);text-transform:uppercase;}");
            html.AppendLine("input,select{background:#21262d;color:#fff;border:1px solid var(--border);border-radius:6px;padding:8px 10px;font-size:14px;width:100%;transition:border-color 0.2s;}");
            html.AppendLine("input:focus,select:focus{outline:none;border-color:var(--primary);box-shadow:0 0 0 3px rgba(31,111,235,0.2);}");
            html.AppendLine("input[type=checkbox]{width:18px;height:18px;cursor:pointer;accent-color:var(--primary);}");

            // 開啟 GPU 圖層獨立優化與垂直高度固定
            html.AppendLine(".table-container{background:var(--panel-bg);border:1px solid var(--border);border-radius:12px;overflow:auto;max-height:75vh;position:relative;box-shadow:0 4px 12px rgba(0,0,0,0.15);will-change:transform;}");
            html.AppendLine("table{width:100%;border-collapse:collapse;font-size:13px;white-space:nowrap;}");
            html.AppendLine("th,td{border-bottom:1px solid var(--border);padding:8px 10px;text-align:center;height:38px;box-sizing:border-box;}");

            // 渲染層次優化：隱藏螢幕外渲染
            html.AppendLine("tr{content-visibility:auto;contain-intrinsic-size:38px;}");
            html.AppendLine(".text-left{text-align:left;}");
            html.AppendLine("th{background:#1f242c;color:#fff;font-weight:600;cursor:pointer;position:sticky;top:0;z-index:2;user-select:none;}");

            html.AppendLine("@media(max-width:768px){");
            html.AppendLine("  .header-bar{flex-direction:column;align-items:stretch;gap:12px;}");
            html.AppendLine("  .btn-csv{justify-content:center;width:100%;}");
            html.AppendLine("  .filter-grid{grid-template-columns:1fr 1fr;}");
            html.AppendLine("  .checkbox-group{padding-top:0;}");
            html.AppendLine("  .sticky-col{position:sticky;left:0;background:var(--panel-bg);z-index:1;}");
            html.AppendLine("  th.sticky-col{z-index:3;background:#1f242c;}");
            html.AppendLine("}");

            html.AppendLine(".font-mono{font-family:ui-monospace,SFMono-Regular,SF Mono,Menlo,monospace;}");
            html.AppendLine(".badge{display:inline-block;padding:2px 6px;border-radius:4px;font-weight:600;font-size:12px;}");
            html.AppendLine(".score-badge{background:rgba(31,111,235,0.15);color:#58a6ff;}");
            html.AppendLine(".rise{color:var(--rise);font-weight:600;}");
            html.AppendLine(".fall{color:var(--fall);font-weight:600;}");
            html.AppendLine(".flat{color:var(--flat);}");
            html.AppendLine(".modal-overlay{position:fixed;inset:0;background:rgba(0,0,0,0.78);z-index:1000;display:flex;align-items:center;justify-content:center;padding:16px;}");
            html.AppendLine(".modal-box{background:#161b22;border:1px solid #30363d;border-radius:16px;width:100%;max-width:720px;max-height:90vh;overflow-y:auto;box-shadow:0 24px 64px rgba(0,0,0,0.6);}");
            html.AppendLine(".modal-header{display:flex;justify-content:space-between;align-items:flex-start;padding:20px 24px 16px;border-bottom:1px solid #30363d;gap:12px;}");
            html.AppendLine(".modal-body{padding:20px 24px 24px;}");
            html.AppendLine(".modal-close{background:none;border:none;color:#8b949e;font-size:26px;cursor:pointer;padding:0 4px;line-height:1;flex-shrink:0;}");
            html.AppendLine(".modal-close:hover{color:#fff;}");
            html.AppendLine(".detail-section{margin-bottom:18px;}");
            html.AppendLine(".detail-section-title{color:#8b949e;font-size:12px;font-weight:700;text-transform:uppercase;margin-bottom:10px;padding-bottom:6px;border-bottom:1px solid #21262d;letter-spacing:0.05em;}");
            html.AppendLine(".detail-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:10px;}");
            html.AppendLine(".detail-item{background:#21262d;border-radius:8px;padding:10px 14px;}");
            html.AppendLine(".detail-item-label{color:#8b949e;font-size:11px;font-weight:600;text-transform:uppercase;margin-bottom:5px;}");
            html.AppendLine(".detail-item-value{color:#fff;font-size:17px;font-weight:700;}");
            html.AppendLine(".reason-box{background:#21262d;border-radius:8px;padding:14px 16px;color:#c9d1d9;font-size:13px;line-height:1.8;white-space:pre-wrap;word-break:break-all;}");
            html.AppendLine(".score-pills{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px;}");
            html.AppendLine(".score-pill{background:#21262d;border-radius:8px;padding:8px 14px;text-align:center;min-width:68px;}");
            html.AppendLine(".score-pill-label{color:#8b949e;font-size:10px;font-weight:600;margin-bottom:4px;}");
            html.AppendLine(".score-pill-value{color:#58a6ff;font-size:20px;font-weight:700;}");
            html.AppendLine(".tag-list{display:flex;flex-wrap:wrap;gap:6px;}");
            html.AppendLine(".tag-chip{background:rgba(31,111,235,0.15);color:#58a6ff;border:1px solid rgba(31,111,235,0.3);border-radius:12px;padding:4px 12px;font-size:12px;font-weight:600;}");
            html.AppendLine(".inst-row{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:10px;}");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            html.AppendLine("<div class='header-bar'>");
            html.AppendLine("  <div class='header-title'>");
            html.AppendLine("    <h2>全市場掃描排名</h2>");
            html.AppendLine($"    <div class=\"muted\" id=\"summaryText\">{HtmlEncode(updateSummary)}</div>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div>");
            html.AppendLine("    <button id='btnDownloadCsv' class='btn-csv'>💾 下載 CSV 檔</button>");
            html.AppendLine("  </div>");
            html.AppendLine("</div>");

            html.AppendLine("<div class=\"panel\">");
            html.AppendLine("<div class='filter-grid'>");
            html.AppendLine("<div class='filter-group'><label>關鍵字搜尋</label><input id='searchInput' placeholder='代號/名稱/型態/建議/策略' /></div>");
            html.AppendLine("<div class='filter-group'><label>Top 數量</label><input id='topCount' type='number' min='1' placeholder='100' /></div>");
            html.AppendLine("<div class='filter-group'><label>價格區間</label><div class='row-inputs'><input id='minPrice' type='number' step='0.01' placeholder='Min' /><input id='maxPrice' type='number' step='0.01' placeholder='Max' /></div></div>");
            html.AppendLine("<div class='filter-group'><label>漲跌幅%</label><div class='row-inputs'><input id='minChange' type='number' step='0.01' placeholder='Min' /><input id='maxChange' type='number' step='0.01' placeholder='Max' /></div></div>");
            html.AppendLine("<div class='filter-group'><label>法人買賣超(張)</label><div class='row-inputs'><input id='minNet' type='number' step='1' placeholder='Min' /><input id='maxNet' type='number' step='1' placeholder='Max' /></div></div>");
            html.AppendLine("<div class='filter-group'><label>買賣超金額</label><div class='row-inputs'><input id='minNetAmount' type='number' step='1' placeholder='Min' /><input id='maxNetAmount' type='number' step='1' placeholder='Max' /></div></div>");
            html.AppendLine("<div class='filter-group'><label>最新分數 ≥</label><input id='minScore' type='number' step='1' placeholder='0' /></div>");
            html.AppendLine("<div class='filter-group'><label>風險分數 ≦</label><input id='minCrash' type='number' step='1' placeholder='0' /></div>");
            html.AppendLine("<div class='filter-group'><label>型態數量 ≥</label><input id='minPatternCount' type='number' step='1' placeholder='0' /></div>");
            html.AppendLine("<div class='filter-group'><label>指定型態</label><select id='patternFilter'><option value=''>全部</option></select></div>");
            html.AppendLine("<div class='filter-group'><label>策略動作</label><select id='actionFilter'><option value=''>全部</option></select></div>");
            html.AppendLine("<div class='filter-group'><label>建議倉位</label><select id='holdingFilter'><option value=''>全部</option></select></div>");
            html.AppendLine("<div class='filter-group'><label>綜合建議</label><select id='suggestionFilter'><option value=''>全部</option></select></div>");
            html.AppendLine("<div class='filter-group'><label>5日均分 ≥</label><input id='minAvg' type='number' step='0.1' placeholder='0' /></div>");
            html.AppendLine("<div class='filter-group'><label>連續天數條件</label><div class='row-inputs'><input id='minConDays' type='number' step='1' placeholder='天數' /><input id='minConScore' type='number' step='1' placeholder='分數' value='60' /></div></div>");
            html.AppendLine("<div class='filter-group checkbox-group'><label><input id='trendUp' type='checkbox' /> 5日分數趨勢上升</label></div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");

            // 0050 Market Leader Summary Card
            html.AppendLine("<div id='hero0050Card' class='hero-card' style='display:none;'>");
            html.AppendLine("<div class='hero-header'>");
            html.AppendLine("<div class='hero-title'>");
            html.AppendLine("<h2>🏆 <span id='hero-symbol'></span> <span id='hero-name'></span></h2>");
            html.AppendLine("<div class='subtitle'>市場主要趨勢指標 | 排名: <span class='rank-badge' id='hero-rank'></span></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='hero-price'>");
            html.AppendLine("<p class='price' id='hero-price'></p>");
            html.AppendLine("<p class='change' id='hero-change'></p>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='hero-stats'>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>綜合評分</div>");
            html.AppendLine("<div class='stat-value large' id='hero-score'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>風險評分</div>");
            html.AppendLine("<div class='stat-value' id='hero-crash'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>5日均分</div>");
            html.AppendLine("<div class='stat-value' id='hero-avg'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>分數趨勢 (5日)</div>");
            html.AppendLine("<div class='stat-value' id='hero-trend'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>法人買賣(張)</div>");
            html.AppendLine("<div class='stat-value' id='hero-net'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='stat-box'>");
            html.AppendLine("<div class='stat-label'>買賣金額</div>");
            html.AppendLine("<div class='stat-value' id='hero-netAmount'></div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='hero-action'>");
            html.AppendLine("<div class='action-title'>📈 策略建議</div>");
            html.AppendLine("<div class='action-content'><span id='hero-action'></span> | <span id='hero-stage'></span></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='hero-suggestion'>");
            html.AppendLine("<div class='suggestion-title'>💡 下一步操作建議</div>");
            html.AppendLine("<div class='suggestion-content' id='hero-suggestion'></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div style='margin-top:16px;padding:12px;background:rgba(139,148,158,0.1);border-radius:6px;'>");
            html.AppendLine("<div style='color:var(--text-muted);font-size:13px;font-weight:600;margin-bottom:8px;'>📊 評分理由</div>");
            html.AppendLine("<div style='color:var(--text);font-size:13px;line-height:1.6;' id='hero-reason'></div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");

            html.AppendLine("<div class=\"panel\">");
            html.AppendLine("<h3 style='margin:0 0 12px 0;font-size:18px;font-weight:600;'>📊 0050 元大台灣50 技術分析圖表 (120日)</h3>");
            html.AppendLine("<div id='chart0050Container' style='background:#0d1117;border:1px solid var(--border);border-radius:8px;padding:16px;'>");
            html.AppendLine("<canvas id='chartCandlestick' style='width:100%;height:400px;display:block;margin-bottom:8px;'></canvas>");
            html.AppendLine("<canvas id='chartVolume' style='width:100%;height:120px;display:block;margin-bottom:8px;'></canvas>");
            html.AppendLine("<canvas id='chartMACD' style='width:100%;height:120px;display:block;margin-bottom:8px;'></canvas>");
            html.AppendLine("<canvas id='chartRSI' style='width:100%;height:100px;display:block;'></canvas>");
            html.AppendLine("<div style='margin-top:12px;color:var(--text-muted);font-size:12px;'>");
            html.AppendLine("<span style='margin-right:16px;'>📈 MA5 <span style='color:#58a6ff;'>━━</span></span>");
            html.AppendLine("<span style='margin-right:16px;'>📈 MA20 <span style='color:#ff7b72;'>━━</span></span>");
            html.AppendLine("<span style='margin-right:16px;'>📈 MA120 <span style='color:#a5d6ff;'>━━</span></span>");
            html.AppendLine("<span style='margin-right:16px;'>📈 MA240 <span style='color:#d2a8ff;'>━━</span></span>");
            html.AppendLine("<span style='margin-right:16px;'>📊 BB <span style='color:#8b949e;'>- - -</span></span>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");

            html.AppendLine("<div id='stockModal' class='modal-overlay' style='display:none;' onclick=\"if(event.target===this)closeModal()\">");
            html.AppendLine("<div class='modal-box'>");
            html.AppendLine("<div class='modal-header'>");
            html.AppendLine("<div style='flex:1;min-width:0;'>");
            html.AppendLine("  <div style='display:flex;align-items:center;gap:10px;margin-bottom:6px;flex-wrap:wrap;'>");
            html.AppendLine("    <span style='font-size:22px;font-weight:700;color:#fff;' id='md-symbol'></span>");
            html.AppendLine("    <span style='font-size:17px;color:#c9d1d9;' id='md-name'></span>");
            html.AppendLine("    <span class='rank-badge' id='md-rank'></span>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div style='display:flex;gap:14px;align-items:baseline;'>");
            html.AppendLine("    <span style='font-size:30px;font-weight:700;color:#fff;font-family:ui-monospace,monospace;' id='md-price'></span>");
            html.AppendLine("    <span style='font-size:18px;font-weight:600;font-family:ui-monospace,monospace;' id='md-chg'></span>");
            html.AppendLine("  </div>");
            html.AppendLine("</div>");
            html.AppendLine("<button class='modal-close' onclick='closeModal()'>✕</button>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='modal-body'>");
            html.AppendLine("  <div class='detail-section'>");
            html.AppendLine("    <div class='detail-section-title'>評分概覽</div>");
            html.AppendLine("    <div class='detail-grid'>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>最新分數</div><div class='detail-item-value' id='md-score'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>風險評分</div><div class='detail-item-value' id='md-crash'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>5日均分</div><div class='detail-item-value' id='md-avg'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>分數趨勢(5日)</div><div class='detail-item-value' id='md-trend'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>型態數量</div><div class='detail-item-value' id='md-pcount'></div></div>");
            html.AppendLine("    </div>");
            html.AppendLine("    <div class='score-pills' id='md-score-pills'></div>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div class='detail-section'>");
            html.AppendLine("    <div class='detail-section-title'>📋 評分理由明細</div>");
            html.AppendLine("    <div class='reason-box' id='md-reason'>（無評分理由）</div>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div class='detail-section' id='md-pattern-section'>");
            html.AppendLine("    <div class='detail-section-title'>📊 型態標籤</div>");
            html.AppendLine("    <div class='tag-list' id='md-patterns'></div>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div class='detail-section'>");
            html.AppendLine("    <div class='detail-section-title'>📈 策略建議</div>");
            html.AppendLine("    <div class='detail-grid' style='margin-bottom:10px;'>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>策略動作</div><div class='detail-item-value' id='md-action'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>倉位狀態</div><div class='detail-item-value' id='md-stage'></div></div>");
            html.AppendLine("    </div>");
            html.AppendLine("    <div style='background:#21262d;border-radius:8px;padding:12px 16px;'>");
            html.AppendLine("      <div style='color:#8b949e;font-size:11px;font-weight:600;text-transform:uppercase;margin-bottom:6px;'>綜合建議</div>");
            html.AppendLine("      <div style='color:#c9d1d9;font-size:14px;line-height:1.6;' id='md-suggestion'></div>");
            html.AppendLine("    </div>");
            html.AppendLine("  </div>");
            html.AppendLine("  <div class='detail-section'>");
            html.AppendLine("    <div class='detail-section-title'>🏦 法人買賣超 (張)</div>");
            html.AppendLine("    <div class='inst-row'>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>三大法人合計</div><div class='detail-item-value' id='md-net'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>外資</div><div class='detail-item-value' id='md-foreign'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>自營商</div><div class='detail-item-value' id='md-dealer'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>投信</div><div class='detail-item-value' id='md-trust'></div></div>");
            html.AppendLine("      <div class='detail-item'><div class='detail-item-label'>買賣金額</div><div class='detail-item-value' id='md-netAmount'></div></div>");
            html.AppendLine("    </div>");
            html.AppendLine("  </div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class=\"table-container\" id=\"tableContainer\"><table id=\"rankingTable\"><thead><tr>");
            html.AppendLine("<th data-type='num' class='sticky-col'>排名</th><th data-type='text' class='sticky-col'>代號</th><th data-type='text' class='sticky-col'>名稱</th><th data-type='num'>分數</th><th data-type='num'>風險</th><th data-type='num'>型態數</th><th data-type='text' class='text-left'>型態標籤</th><th data-type='num'>D0</th><th data-type='num'>D1</th><th data-type='num'>D2</th><th data-type='num'>D3</th><th data-type='num'>D4</th><th data-type='num'>5日均分</th><th data-type='num'>趨勢</th><th data-type='num'>法人買賣(張)</th><th data-type='num'>買賣金額</th><th data-type='text'>策略</th><th data-type='text'>倉位</th><th data-type='text' class='text-left'>建議說明</th><th data-type='num'>最新價</th><th data-type='num'>漲跌幅</th>");
            html.AppendLine("</tr></thead><tbody id=\"tbody\"></tbody></table></div>");

            // 將原生 Stock JSON 埋在 JS 變數中
            html.AppendLine("<script>");
            html.AppendLine($"const rawData = {stockDataJson};");
            html.AppendLine($"const kLineData0050 = {kLineData0050Json};");
            html.AppendLine($"const stock0050Data = {stock0050Json};");
            html.AppendLine("const table=document.getElementById('rankingTable');const tbody=document.getElementById('tbody');const container=document.getElementById('tableContainer');const $=id=>document.getElementById(id);");
            html.AppendLine("function populate0050Hero() {");
            html.AppendLine("  if (!stock0050Data) { $('hero0050Card').style.display='none'; return; }");
            html.AppendLine("  $('hero0050Card').style.display='block';");
            html.AppendLine("  $('hero-symbol').textContent = stock0050Data.symbol;");
            html.AppendLine("  $('hero-name').textContent = stock0050Data.name;");
            html.AppendLine("  $('hero-rank').textContent = '#' + stock0050Data.rank;");
            html.AppendLine("  $('hero-price').textContent = stock0050Data.price.toFixed(2);");
            html.AppendLine("  const chgSign = stock0050Data.chg >= 0 ? '+' : '';");
            html.AppendLine("  const chgColor = stock0050Data.chg > 0 ? 'var(--rise)' : stock0050Data.chg < 0 ? 'var(--fall)' : 'var(--flat)';");
            html.AppendLine("  $('hero-change').textContent = chgSign + stock0050Data.chg.toFixed(2) + '%';");
            html.AppendLine("  $('hero-change').style.color = chgColor;");
            html.AppendLine("  $('hero-score').textContent = stock0050Data.score;");
            html.AppendLine("  $('hero-crash').textContent = stock0050Data.crash;");
            html.AppendLine("  $('hero-avg').textContent = stock0050Data.avg.toFixed(1);");
            html.AppendLine("  const trendSign = stock0050Data.trend > 0 ? '+' : '';");
            html.AppendLine("  const trendColor = stock0050Data.trend > 0 ? 'var(--rise)' : stock0050Data.trend < 0 ? 'var(--fall)' : 'var(--flat)';");
            html.AppendLine("  $('hero-trend').textContent = trendSign + stock0050Data.trend;");
            html.AppendLine("  $('hero-trend').style.color = trendColor;");
            html.AppendLine("  const netColor = stock0050Data.net > 0 ? 'var(--rise)' : stock0050Data.net < 0 ? 'var(--fall)' : 'var(--flat)';");
            html.AppendLine("  $('hero-net').textContent = stock0050Data.netStr;");
            html.AppendLine("  $('hero-net').style.color = netColor;");
            html.AppendLine("  const netAmountColor = stock0050Data.netAmount > 0 ? 'var(--rise)' : stock0050Data.netAmount < 0 ? 'var(--fall)' : 'var(--flat)';");
            html.AppendLine("  $('hero-netAmount').textContent = stock0050Data.netAmountStr;");
            html.AppendLine("  $('hero-netAmount').style.color = netAmountColor;");
            html.AppendLine("  $('hero-action').textContent = stock0050Data.action;");
            html.AppendLine("  $('hero-stage').textContent = stock0050Data.stage;");
            html.AppendLine("  $('hero-suggestion').textContent = stock0050Data.suggestion || '無特別建議';");
            html.AppendLine("  $('hero-reason').textContent = stock0050Data.scoreReason || '評分理由尚未載入';");
            html.AppendLine("}");
            html.AppendLine("const f={search:$('searchInput'),top:$('topCount'),minPrice:$('minPrice'),maxPrice:$('maxPrice'),minChange:$('minChange'),maxChange:$('maxChange'),minNet:$('minNet'),maxNet:$('maxNet'),minNetAmount:$('minNetAmount'),maxNetAmount:$('maxNetAmount'),minScore:$('minScore'),minCrash:$('minCrash'),minPatternCount:$('minPatternCount'),pattern:$('patternFilter'),action:$('actionFilter'),holding:$('holdingFilter'),suggestion:$('suggestionFilter'),minAvg:$('minAvg'),trendUp:$('trendUp'),minConDays:$('minConDays'),minConScore:$('minConScore')};");

            html.AppendLine("let filteredData = [...rawData];");
            html.AppendLine("let renderedCount = 0;");
            html.AppendLine("const PAGE_SIZE = 60;");

            // Add comprehensive 0050 chart rendering function
            html.AppendLine("function draw0050Charts() {");
            html.AppendLine("  if (!kLineData0050 || kLineData0050.length === 0) {");
            html.AppendLine("    $('chart0050Container').innerHTML = '<p style=\"color:var(--text-muted);text-align:center;padding:40px;\">0050 K線資料載入中或無可用資料</p>';");
            html.AppendLine("    return;");
            html.AppendLine("  }");
            html.AppendLine("  drawCandlestickChart();");
            html.AppendLine("  drawVolumeChart();");
            html.AppendLine("  drawMACDChart();");
            html.AppendLine("  drawRSIChart();");
            html.AppendLine("}");

            // Candlestick chart with MA lines and Bollinger Bands
            html.AppendLine("function drawCandlestickChart() {");
            html.AppendLine("  const canvas = $('chartCandlestick');");
            html.AppendLine("  if (!canvas) return;");
            html.AppendLine("  const ctx = canvas.getContext('2d');");
            html.AppendLine("  canvas.width = canvas.offsetWidth * window.devicePixelRatio;");
            html.AppendLine("  canvas.height = 400 * window.devicePixelRatio;");
            html.AppendLine("  ctx.scale(window.devicePixelRatio, window.devicePixelRatio);");
            html.AppendLine("  const w = canvas.width / window.devicePixelRatio, h = canvas.height / window.devicePixelRatio;");
            html.AppendLine("  ctx.clearRect(0, 0, w, h);");
            html.AppendLine("  const padding = {left: 60, right: 20, top: 30, bottom: 30};");
            html.AppendLine("  const chartW = w - padding.left - padding.right;");
            html.AppendLine("  const chartH = h - padding.top - padding.bottom;");
            html.AppendLine("  const dataLen = kLineData0050.length;");
            html.AppendLine("  const candleWidth = Math.max(2, chartW / dataLen * 0.7);");
            html.AppendLine("  const candleSpacing = chartW / dataLen;");

            // Find price range
            html.AppendLine("  let minPrice = Math.min(...kLineData0050.map(d => d.low));");
            html.AppendLine("  let maxPrice = Math.max(...kLineData0050.map(d => d.high));");
            html.AppendLine("  const priceRange = maxPrice - minPrice;");
            html.AppendLine("  minPrice -= priceRange * 0.05;");
            html.AppendLine("  maxPrice += priceRange * 0.05;");
            html.AppendLine("  const priceScale = chartH / (maxPrice - minPrice);");

            html.AppendLine("  function priceToY(price) { return padding.top + chartH - (price - minPrice) * priceScale; }");
            html.AppendLine("  function indexToX(i) { return padding.left + i * candleSpacing + candleSpacing / 2; }");

            // Draw grid
            html.AppendLine("  ctx.strokeStyle = '#30363d';");
            html.AppendLine("  ctx.lineWidth = 1;");
            html.AppendLine("  for (let i = 0; i <= 5; i++) {");
            html.AppendLine("    const y = padding.top + (chartH / 5) * i;");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    ctx.moveTo(padding.left, y);");
            html.AppendLine("    ctx.lineTo(w - padding.right, y);");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("    const price = maxPrice - (maxPrice - minPrice) * (i / 5);");
            html.AppendLine("    ctx.fillStyle = '#8b949e';");
            html.AppendLine("    ctx.font = '11px sans-serif';");
            html.AppendLine("    ctx.textAlign = 'right';");
            html.AppendLine("    ctx.fillText(price.toFixed(2), padding.left - 5, y + 4);");
            html.AppendLine("  }");

            // Draw MA lines
            html.AppendLine("  const maConfigs = [");
            html.AppendLine("    {key: 'ma5', color: '#58a6ff', width: 1.5},");
            html.AppendLine("    {key: 'ma20', color: '#ff7b72', width: 1.5},");
            html.AppendLine("    {key: 'ma120', color: '#a5d6ff', width: 1.2},");
            html.AppendLine("    {key: 'ma240', color: '#d2a8ff', width: 1.2}");
            html.AppendLine("  ];");
            html.AppendLine("  maConfigs.forEach(cfg => {");
            html.AppendLine("    ctx.strokeStyle = cfg.color;");
            html.AppendLine("    ctx.lineWidth = cfg.width;");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    kLineData0050.forEach((d, i) => {");
            html.AppendLine("      if (d[cfg.key] && d[cfg.key] > 0) {");
            html.AppendLine("        const x = indexToX(i);");
            html.AppendLine("        const y = priceToY(d[cfg.key]);");
            html.AppendLine("        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);");
            html.AppendLine("      }");
            html.AppendLine("    });");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("  });");

            // Draw Bollinger Bands
            html.AppendLine("  ctx.strokeStyle = '#8b949e';");
            html.AppendLine("  ctx.lineWidth = 1;");
            html.AppendLine("  ctx.setLineDash([3, 3]);");
            html.AppendLine("  ['bbUpper', 'bbMiddle', 'bbLower'].forEach(key => {");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    kLineData0050.forEach((d, i) => {");
            html.AppendLine("      if (d[key] && d[key] > 0) {");
            html.AppendLine("        const x = indexToX(i);");
            html.AppendLine("        const y = priceToY(d[key]);");
            html.AppendLine("        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);");
            html.AppendLine("      }");
            html.AppendLine("    });");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("  });");
            html.AppendLine("  ctx.setLineDash([]);");

            // Draw candlesticks
            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const isRise = d.close >= d.open;");
            html.AppendLine("    ctx.strokeStyle = isRise ? '#ff453a' : '#32d74b';");
            html.AppendLine("    ctx.fillStyle = isRise ? '#ff453a' : '#32d74b';");
            html.AppendLine("    const yHigh = priceToY(d.high);");
            html.AppendLine("    const yLow = priceToY(d.low);");
            html.AppendLine("    const yOpen = priceToY(d.open);");
            html.AppendLine("    const yClose = priceToY(d.close);");
            html.AppendLine("    ctx.lineWidth = 1;");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    ctx.moveTo(x, yHigh);");
            html.AppendLine("    ctx.lineTo(x, yLow);");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("    const bodyHeight = Math.abs(yClose - yOpen);");
            html.AppendLine("    if (bodyHeight > 0.5) {");
            html.AppendLine("      ctx.fillRect(x - candleWidth / 2, Math.min(yOpen, yClose), candleWidth, bodyHeight);");
            html.AppendLine("    } else {");
            html.AppendLine("      ctx.fillRect(x - candleWidth / 2, yClose - 0.5, candleWidth, 1);");
            html.AppendLine("    }");
            html.AppendLine("  });");

            // Draw title and legend
            html.AppendLine("  ctx.fillStyle = '#c9d1d9';");
            html.AppendLine("  ctx.font = 'bold 14px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'left';");
            html.AppendLine("  const latest = kLineData0050[kLineData0050.length - 1];");
            html.AppendLine("  ctx.fillText(`0050 元大台灣50 - 收盤: ${latest.close} (${latest.date})`, padding.left, 20);");
            html.AppendLine("}");

            // Volume chart
            html.AppendLine("function drawVolumeChart() {");
            html.AppendLine("  const canvas = $('chartVolume');");
            html.AppendLine("  if (!canvas) return;");
            html.AppendLine("  const ctx = canvas.getContext('2d');");
            html.AppendLine("  canvas.width = canvas.offsetWidth * window.devicePixelRatio;");
            html.AppendLine("  canvas.height = 120 * window.devicePixelRatio;");
            html.AppendLine("  ctx.scale(window.devicePixelRatio, window.devicePixelRatio);");
            html.AppendLine("  const w = canvas.width / window.devicePixelRatio, h = canvas.height / window.devicePixelRatio;");
            html.AppendLine("  ctx.clearRect(0, 0, w, h);");
            html.AppendLine("  const padding = {left: 60, right: 20, top: 10, bottom: 20};");
            html.AppendLine("  const chartW = w - padding.left - padding.right;");
            html.AppendLine("  const chartH = h - padding.top - padding.bottom;");
            html.AppendLine("  const dataLen = kLineData0050.length;");
            html.AppendLine("  const barWidth = Math.max(2, chartW / dataLen * 0.7);");
            html.AppendLine("  const barSpacing = chartW / dataLen;");
            html.AppendLine("  const maxVol = Math.max(...kLineData0050.map(d => d.volume));");
            html.AppendLine("  const volScale = chartH / maxVol;");
            html.AppendLine("  function indexToX(i) { return padding.left + i * barSpacing + barSpacing / 2; }");

            // Add Y-axis grid lines and labels
            html.AppendLine("  ctx.strokeStyle = '#30363d';");
            html.AppendLine("  ctx.lineWidth = 1;");
            html.AppendLine("  ctx.fillStyle = '#8b949e';");
            html.AppendLine("  ctx.font = '10px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'right';");
            html.AppendLine("  for (let i = 0; i <= 3; i++) {");
            html.AppendLine("    const vol = maxVol * (1 - i / 3);");
            html.AppendLine("    const y = padding.top + (chartH / 3) * i;");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    ctx.moveTo(padding.left, y);");
            html.AppendLine("    ctx.lineTo(w - padding.right, y);");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("    const volLabel = vol >= 1000 ? (vol / 1000).toFixed(0) + 'K' : vol.toFixed(0);");
            html.AppendLine("    ctx.fillText(volLabel, padding.left - 5, y + 4);");
            html.AppendLine("  }");

            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const barHeight = d.volume * volScale;");
            html.AppendLine("    const y = padding.top + chartH - barHeight;");
            html.AppendLine("    const isRise = d.close >= d.open;");
            html.AppendLine("    ctx.fillStyle = isRise ? 'rgba(255,69,58,0.6)' : 'rgba(50,215,75,0.6)';");
            html.AppendLine("    ctx.fillRect(x - barWidth / 2, y, barWidth, barHeight);");
            html.AppendLine("  });");
            html.AppendLine("  ctx.fillStyle = '#8b949e';");
            html.AppendLine("  ctx.font = '11px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'left';");
            html.AppendLine("  ctx.fillText('成交量', padding.left, padding.top + 12);");
            html.AppendLine("}");

            // MACD chart
            html.AppendLine("function drawMACDChart() {");
            html.AppendLine("  const canvas = $('chartMACD');");
            html.AppendLine("  if (!canvas) return;");
            html.AppendLine("  const ctx = canvas.getContext('2d');");
            html.AppendLine("  canvas.width = canvas.offsetWidth * window.devicePixelRatio;");
            html.AppendLine("  canvas.height = 120 * window.devicePixelRatio;");
            html.AppendLine("  ctx.scale(window.devicePixelRatio, window.devicePixelRatio);");
            html.AppendLine("  const w = canvas.width / window.devicePixelRatio, h = canvas.height / window.devicePixelRatio;");
            html.AppendLine("  ctx.clearRect(0, 0, w, h);");
            html.AppendLine("  const padding = {left: 60, right: 20, top: 10, bottom: 20};");
            html.AppendLine("  const chartW = w - padding.left - padding.right;");
            html.AppendLine("  const chartH = h - padding.top - padding.bottom;");
            html.AppendLine("  const dataLen = kLineData0050.length;");
            html.AppendLine("  const barWidth = Math.max(2, chartW / dataLen * 0.7);");
            html.AppendLine("  const barSpacing = chartW / dataLen;");
            html.AppendLine("  const maxAbs = Math.max(...kLineData0050.map(d => Math.max(Math.abs(d.macd), Math.abs(d.macdSignal), Math.abs(d.macdHist))));");
            html.AppendLine("  const scale = (chartH / 2) / (maxAbs * 1.1);");
            html.AppendLine("  const zeroY = padding.top + chartH / 2;");
            html.AppendLine("  function indexToX(i) { return padding.left + i * barSpacing + barSpacing / 2; }");

            // Add Y-axis grid lines and labels
            html.AppendLine("  ctx.strokeStyle = '#30363d';");
            html.AppendLine("  ctx.lineWidth = 1;");
            html.AppendLine("  ctx.fillStyle = '#8b949e';");
            html.AppendLine("  ctx.font = '10px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'right';");
            html.AppendLine("  const maxMacd = maxAbs * 1.1;");
            html.AppendLine("  for (let i = 0; i <= 4; i++) {");
            html.AppendLine("    const value = maxMacd - (maxMacd * 2 * i / 4);");
            html.AppendLine("    const y = padding.top + (chartH / 4) * i;");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    ctx.moveTo(padding.left, y);");
            html.AppendLine("    ctx.lineTo(w - padding.right, y);");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("    ctx.fillText(value.toFixed(3), padding.left - 5, y + 4);");
            html.AppendLine("  }");

            // Emphasize zero line
            html.AppendLine("  ctx.strokeStyle = '#58a6ff';");
            html.AppendLine("  ctx.lineWidth = 1.5;");
            html.AppendLine("  ctx.beginPath();");
            html.AppendLine("  ctx.moveTo(padding.left, zeroY);");
            html.AppendLine("  ctx.lineTo(w - padding.right, zeroY);");
            html.AppendLine("  ctx.stroke();");

            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const histHeight = d.macdHist * scale;");
            html.AppendLine("    ctx.fillStyle = d.macdHist >= 0 ? 'rgba(255,69,58,0.8)' : 'rgba(50,215,75,0.8)';");
            html.AppendLine("    if (histHeight >= 0) {");
            html.AppendLine("      ctx.fillRect(x - barWidth / 2, zeroY - histHeight, barWidth, histHeight);");
            html.AppendLine("    } else {");
            html.AppendLine("      ctx.fillRect(x - barWidth / 2, zeroY, barWidth, -histHeight);");
            html.AppendLine("    }");
            html.AppendLine("  });");
            html.AppendLine("  ctx.strokeStyle = '#58a6ff';");
            html.AppendLine("  ctx.lineWidth = 1.5;");
            html.AppendLine("  ctx.beginPath();");
            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const y = zeroY - d.macd * scale;");
            html.AppendLine("    if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);");
            html.AppendLine("  });");
            html.AppendLine("  ctx.stroke();");
            html.AppendLine("  ctx.strokeStyle = '#ffa657';");
            html.AppendLine("  ctx.lineWidth = 1.5;");
            html.AppendLine("  ctx.beginPath();");
            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const y = zeroY - d.macdSignal * scale;");
            html.AppendLine("    if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);");
            html.AppendLine("  });");
            html.AppendLine("  ctx.stroke();");
            html.AppendLine("  ctx.fillStyle = '#8b949e';");
            html.AppendLine("  ctx.font = '11px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'left';");
            html.AppendLine("  ctx.fillText('MACD', padding.left, padding.top + 12);");
            html.AppendLine("}");

            // RSI chart
            html.AppendLine("function drawRSIChart() {");
            html.AppendLine("  const canvas = $('chartRSI');");
            html.AppendLine("  if (!canvas) return;");
            html.AppendLine("  const ctx = canvas.getContext('2d');");
            html.AppendLine("  canvas.width = canvas.offsetWidth * window.devicePixelRatio;");
            html.AppendLine("  canvas.height = 100 * window.devicePixelRatio;");
            html.AppendLine("  ctx.scale(window.devicePixelRatio, window.devicePixelRatio);");
            html.AppendLine("  const w = canvas.width / window.devicePixelRatio, h = canvas.height / window.devicePixelRatio;");
            html.AppendLine("  ctx.clearRect(0, 0, w, h);");
            html.AppendLine("  const padding = {left: 60, right: 20, top: 10, bottom: 20};");
            html.AppendLine("  const chartW = w - padding.left - padding.right;");
            html.AppendLine("  const chartH = h - padding.top - padding.bottom;");
            html.AppendLine("  const dataLen = kLineData0050.length;");
            html.AppendLine("  const barSpacing = chartW / dataLen;");
            html.AppendLine("  const rsiScale = chartH / 100;");
            html.AppendLine("  function indexToX(i) { return padding.left + i * barSpacing + barSpacing / 2; }");
            html.AppendLine("  function rsiToY(rsi) { return padding.top + chartH - rsi * rsiScale; }");
            html.AppendLine("  [70, 50, 30].forEach(level => {");
            html.AppendLine("    const y = rsiToY(level);");
            html.AppendLine("    ctx.strokeStyle = level === 50 ? '#30363d' : '#8b949e';");
            html.AppendLine("    ctx.lineWidth = 1;");
            html.AppendLine("    ctx.setLineDash(level === 50 ? [] : [2, 2]);");
            html.AppendLine("    ctx.beginPath();");
            html.AppendLine("    ctx.moveTo(padding.left, y);");
            html.AppendLine("    ctx.lineTo(w - padding.right, y);");
            html.AppendLine("    ctx.stroke();");
            html.AppendLine("    ctx.fillStyle = '#8b949e';");
            html.AppendLine("    ctx.font = '10px sans-serif';");
            html.AppendLine("    ctx.textAlign = 'right';");
            html.AppendLine("    ctx.fillText(level.toString(), padding.left - 5, y + 3);");
            html.AppendLine("  });");
            html.AppendLine("  ctx.setLineDash([]);");
            html.AppendLine("  ctx.strokeStyle = '#d2a8ff';");
            html.AppendLine("  ctx.lineWidth = 2;");
            html.AppendLine("  ctx.beginPath();");
            html.AppendLine("  kLineData0050.forEach((d, i) => {");
            html.AppendLine("    const x = indexToX(i);");
            html.AppendLine("    const y = rsiToY(d.rsi);");
            html.AppendLine("    if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);");
            html.AppendLine("  });");
            html.AppendLine("  ctx.stroke();");
            html.AppendLine("  ctx.fillStyle = '#8b949e';");
            html.AppendLine("  ctx.font = '11px sans-serif';");
            html.AppendLine("  ctx.textAlign = 'left';");
            html.AppendLine("  ctx.fillText('RSI', padding.left, padding.top + 12);");
            html.AppendLine("}");

            html.AppendLine("function closeModal(){$('stockModal').style.display='none';document.body.style.overflow='';}" );
            html.AppendLine("document.addEventListener('keydown',e=>{if(e.key==='Escape')closeModal();});");
            html.AppendLine("function applyValueColor(el,value){");
            html.AppendLine("  el.style.color = value > 0 ? 'var(--rise)' : value < 0 ? 'var(--fall)' : 'var(--flat)';");
            html.AppendLine("}");
            html.AppendLine("function showStockDetail(s){");
            html.AppendLine("  $('md-symbol').textContent = s.symbol;");
            html.AppendLine("  $('md-name').textContent = s.name;");
            html.AppendLine("  $('md-rank').textContent = '#' + s.rank;");
            html.AppendLine("  $('md-price').textContent = s.price.toFixed(2);");
            html.AppendLine("  const chgSign = s.chg >= 0 ? '+' : '';");
            html.AppendLine("  const chgEl = $('md-chg');");
            html.AppendLine("  chgEl.textContent = chgSign + s.chg.toFixed(2) + '%';");
            html.AppendLine("  applyValueColor(chgEl, s.chg);");
            html.AppendLine("  $('md-score').textContent = s.score;");
            html.AppendLine("  $('md-crash').textContent = s.crash;");
            html.AppendLine("  $('md-avg').textContent = s.avg.toFixed(1);");
            html.AppendLine("  const trendEl = $('md-trend');");
            html.AppendLine("  trendEl.textContent = (s.trend > 0 ? '+' : '') + s.trend;");
            html.AppendLine("  applyValueColor(trendEl, s.trend);");
            html.AppendLine("  $('md-pcount').textContent = s.pcount;");
            html.AppendLine("  const headers = ['D0','D1','D2','D3','D4'];");
            html.AppendLine("  const dayScores = [s.d0, s.d1, s.d2, s.d3, s.d4];");
            html.AppendLine("  const pillsEl = $('md-score-pills');");
            html.AppendLine("  pillsEl.innerHTML = '';");
            html.AppendLine("  dayScores.forEach((sc, i) => {");
            html.AppendLine("    if (sc === 0 && i > 0) return;");
            html.AppendLine("    const pill = document.createElement('div');");
            html.AppendLine("    pill.className = 'score-pill';");
            html.AppendLine("    const color = sc >= 75 ? 'var(--fall)' : sc >= 60 ? '#ffa657' : sc > 0 ? 'var(--text-muted)' : '#444';");
            html.AppendLine("    pill.innerHTML = `<div class='score-pill-label'>${headers[i]}</div><div class='score-pill-value' style='color:${color}'>${sc}</div>`;");
            html.AppendLine("    pillsEl.appendChild(pill);");
            html.AppendLine("  });");
            html.AppendLine("  const reasonEl = $('md-reason');");
            html.AppendLine("  if (s.scoreReason) {");
            html.AppendLine("    const lines = s.scoreReason.split(' | ').filter(r => r.trim());");
            html.AppendLine("    reasonEl.textContent = lines.join('\n');");
            html.AppendLine("  } else {");
            html.AppendLine("    reasonEl.textContent = '（無評分理由）';");
            html.AppendLine("  }");
            html.AppendLine("  const patternsEl = $('md-patterns');");
            html.AppendLine("  patternsEl.innerHTML = '';");
            html.AppendLine("  const patternSection = $('md-pattern-section');");
            html.AppendLine("  if (s.pattern) {");
            html.AppendLine("    patternSection.style.display = '';");
            html.AppendLine("    s.pattern.split('、').filter(p => p.trim()).forEach(tag => {");
            html.AppendLine("      const chip = document.createElement('span');");
            html.AppendLine("      chip.className = 'tag-chip';");
            html.AppendLine("      chip.textContent = tag.trim();");
            html.AppendLine("      patternsEl.appendChild(chip);");
            html.AppendLine("    });");
            html.AppendLine("  } else {");
            html.AppendLine("    patternSection.style.display = 'none';");
            html.AppendLine("  }");
            html.AppendLine("  $('md-action').textContent = s.action || '—';");
            html.AppendLine("  $('md-stage').textContent = s.stage || '—';");
            html.AppendLine("  $('md-suggestion').textContent = s.suggestion || '無特別建議';");
            html.AppendLine("  const netEl = $('md-net'); netEl.textContent = s.netStr; applyValueColor(netEl, s.net);");
            html.AppendLine("  const fEl = $('md-foreign'); fEl.textContent = s.foreignNetStr; applyValueColor(fEl, s.foreignNet);");
            html.AppendLine("  const dEl = $('md-dealer'); dEl.textContent = s.dealerNetStr; applyValueColor(dEl, s.dealerNet);");
            html.AppendLine("  const tEl = $('md-trust'); tEl.textContent = s.trustNetStr; applyValueColor(tEl, s.trustNet);");
            html.AppendLine("  const naEl = $('md-netAmount'); naEl.textContent = s.netAmountStr; applyValueColor(naEl, s.netAmount);");
            html.AppendLine("  $('stockModal').style.display = 'flex';");
            html.AppendLine("  document.body.style.overflow = 'hidden';");
            html.AppendLine("}");
            html.AppendLine("populate0050Hero();");
            html.AppendLine("draw0050Charts();");

            // 下拉選單填充
            html.AppendLine("function fillSelect(prop, sel){");
            html.AppendLine("  const vals=[...new Set(rawData.map(x=>x[prop]).filter(Boolean))].sort((a,b)=>a.localeCompare(b,'zh-Hant'));");
            html.AppendLine("  vals.forEach(v=>{const o=document.createElement('option');o.value=v;o.textContent=v;sel.appendChild(o);});");
            html.AppendLine("}");
            html.AppendLine("fillSelect('pattern',f.pattern);fillSelect('action',f.action);fillSelect('stage',f.holding);fillSelect('suggestion',f.suggestion);");

            html.AppendLine("function parseNum(v){if(v===null||v==='')return null;const n=parseFloat(v);return Number.isFinite(n)?n:null;}");
            html.AppendLine("function passRange(v,min,max){if(min!==null&&v<min)return false;if(max!==null&&v>max)return false;return true;}");
            html.AppendLine("function getConsecutive(scores,minScore){let c=0;for(const s of scores){if(s<minScore)break;c++;}return c;}");

            // 高效 DOM 節點生成
            html.AppendLine("function renderBatch(){");
            html.AppendLine("  const nextBatch = filteredData.slice(renderedCount, renderedCount + PAGE_SIZE);");
            html.AppendLine("  if(nextBatch.length === 0) return;");
            html.AppendLine("  const frag = document.createDocumentFragment();");
            html.AppendLine("  nextBatch.forEach(s => {");
            html.AppendLine("    const tr = document.createElement('tr');");
            html.AppendLine("    tr.style.cursor='pointer';");
            html.AppendLine("    tr.addEventListener('click', () => showStockDetail(s));");
            html.AppendLine("    const scoreTitle = s.scoreReason ? `title=\"${s.scoreReason.replace(/\"/g, '&quot;')}\"` : '';");
            html.AppendLine("    const scoreCell = s.scoreReason ? `<td ${scoreTitle} style='cursor:help;'><span class='badge score-badge'>${s.score}</span></td>` : `<td><span class='badge score-badge'>${s.score}</span></td>`;");
            html.AppendLine("    tr.innerHTML = `<td class='sticky-col'>${s.rank}</td>`+");
            html.AppendLine("      `<td class='sticky-col'>${s.symbol}</td>`+");
            html.AppendLine("      `<td class='sticky-col'>${s.name}</td>`+");
            html.AppendLine("      scoreCell+");
            html.AppendLine("      `<td>${s.crash}</td>`+");
            html.AppendLine("      `<td>${s.pcount}</td>`+");
            html.AppendLine("      `<td class='text-left'>${s.pattern}</td>`+");
            html.AppendLine("      `<td>${s.d0}</td><td>${s.d1}</td><td>${s.d2}</td><td>${s.d3}</td><td>${s.d4}</td>`+");
            html.AppendLine("      `<td>${s.avg.toFixed(1)}</td>`+");
            html.AppendLine("      `<td>${s.trend}</td>`+");
            html.AppendLine("      `<td class='${s.netClass}'>${s.netStr}</td>`+");
            html.AppendLine("      `<td class='${s.netAmountClass}'>${s.netAmountStr}</td>`+");
            html.AppendLine("      `<td>${s.action}</td>`+");
            html.AppendLine("      `<td>${s.stage}</td>`+");
            html.AppendLine("      `<td class='text-left'>${s.suggestion}</td>`+");
            html.AppendLine("      `<td class='font-mono'>${s.price.toFixed(2)}</td>`+");
            html.AppendLine("      `<td class='font-mono ${s.chgClass}'>${s.chg.toFixed(2)}%</td>`;");
            html.AppendLine("    frag.appendChild(tr);");
            html.AppendLine("  });");
            html.AppendLine("  tbody.appendChild(frag);");
            html.AppendLine("  renderedCount += nextBatch.length;");
            html.AppendLine("}");

            // 純記憶體快速 Array 篩選與 Chunk 渲染
            html.AppendLine("function applyFilter(){");
            html.AppendLine("  const kw=(f.search.value||'').trim().toLowerCase();const top=parseNum(f.top.value);");
            html.AppendLine("  const minPrice=parseNum(f.minPrice.value),maxPrice=parseNum(f.maxPrice.value),minChange=parseNum(f.minChange.value),maxChange=parseNum(f.maxChange.value);");
            html.AppendLine("  const minNet=parseNum(f.minNet.value),maxNet=parseNum(f.maxNet.value),minNetAmount=parseNum(f.minNetAmount.value),maxNetAmount=parseNum(f.maxNetAmount.value);");
            html.AppendLine("  const minScore=parseNum(f.minScore.value),minCrash=parseNum(f.minCrash.value),minPatternCount=parseNum(f.minPatternCount.value);");
            html.AppendLine("  const minAvg=parseNum(f.minAvg.value),minConDays=Math.max(0,parseNum(f.minConDays.value)||0),minConScore=parseNum(f.minConScore.value)??60;");
            html.AppendLine("  const pattern=f.pattern.value.toLowerCase(),action=f.action.value,holding=f.holding.value,suggestion=f.suggestion.value,trendUp=f.trendUp.checked;");

            html.AppendLine("  filteredData = rawData.filter(item => {");
            html.AppendLine("    if(top!==null&&item.rank>top) return false;");
            html.AppendLine("    if(kw&&!item.searchKey.includes(kw)) return false;");
            html.AppendLine("    if(!passRange(item.price,minPrice,maxPrice)) return false;");
            html.AppendLine("    if(!passRange(item.chg,minChange,maxChange)) return false;");
            html.AppendLine("    if(!passRange(item.net,minNet,maxNet)) return false;");
            html.AppendLine("    if(!passRange(item.netAmount,minNetAmount,maxNetAmount)) return false;");
            html.AppendLine("    if(minScore!==null&&item.score<minScore) return false;");
            html.AppendLine("    if(minCrash!==null&&item.crash>minCrash) return false;");
            html.AppendLine("    if(minPatternCount!==null&&item.pcount<minPatternCount) return false;");
            html.AppendLine("    if(pattern&&!item.pattern.toLowerCase().includes(pattern)) return false;");
            html.AppendLine("    if(action&&item.action!==action) return false;");
            html.AppendLine("    if(holding&&item.stage!==holding) return false;");
            html.AppendLine("    if(suggestion&&item.suggestion!==suggestion) return false;");
            html.AppendLine("    if(minAvg!==null&&item.avg<minAvg) return false;");
            html.AppendLine("    if(trendUp&&item.trend<=0) return false;");
            html.AppendLine("    if(minConDays>0&&getConsecutive([item.d0,item.d1,item.d2,item.d3,item.d4],minConScore)<minConDays) return false;");
            html.AppendLine("    return true;");
            html.AppendLine("  });");

            html.AppendLine("  tbody.innerHTML = '';");
            html.AppendLine("  renderedCount = 0;");
            html.AppendLine("  container.scrollTop = 0;");
            html.AppendLine("  renderBatch();");
            html.AppendLine("}");

            // 無感滾動加載 (Infinite Scroll)
            html.AppendLine("container.addEventListener('scroll', ()=>{");
            html.AppendLine("  if (container.scrollTop + container.clientHeight >= container.scrollHeight - 200) {");
            html.AppendLine("    renderBatch();");
            html.AppendLine("  }");
            html.AppendLine("});");

            // 防抖處理 (Debounce 300ms)
            html.AppendLine("function debounce(fn,d=300){let t;return(...a)=>{clearTimeout(t);t=setTimeout(()=>fn(...a),d);};}");
            html.AppendLine("const debouncedFilter=debounce(applyFilter,300);");
            html.AppendLine("Object.values(f).forEach(el=>{if(!el)return;const isSel=el.type==='checkbox'||el.tagName==='SELECT';el.addEventListener(isSel?'change':'input',isSel?applyFilter:debouncedFilter);});");

            // 排序機制
            html.AppendLine("let sortState={idx:0,asc:true};");
            html.AppendLine("const propMap=['rank','symbol','name','score','crash','pcount','pattern','d0','d1','d2','d3','d4','avg','trend','net','netAmount','action','stage','suggestion','price','chg'];");
            html.AppendLine("[...table.tHead.rows[0].cells].forEach((th,idx)=>{");
            html.AppendLine("  th.addEventListener('click',()=>{");
            html.AppendLine("    sortState.asc=(sortState.idx===idx)?!sortState.asc:true;");
            html.AppendLine("    sortState.idx=idx;");
            html.AppendLine("    const key=propMap[idx];");
            html.AppendLine("    filteredData.sort((a,b)=>{");
            html.AppendLine("      let va=a[key], vb=b[key];");
            html.AppendLine("      if(typeof va === 'string') return sortState.asc? va.localeCompare(vb,'zh-Hant') : vb.localeCompare(va,'zh-Hant');");
            html.AppendLine("      return sortState.asc ? va - vb : vb - va;");
            html.AppendLine("    });");
            html.AppendLine("    tbody.innerHTML = '';");
            html.AppendLine("    renderedCount = 0;");
            html.AppendLine("    renderBatch();");
            html.AppendLine("  });");
            html.AppendLine("});");

            // CSV 下載 (直接導出當前符合條件的 filteredData，速度極快)
            html.AppendLine("document.getElementById('btnDownloadCsv').addEventListener('click', () => {");
            html.AppendLine("  const csvRows = [];");
            html.AppendLine("  const headers = [...table.tHead.rows[0].cells].map(th => `\"${(th.textContent||'').trim().replace(/\"/g, '\"\"')}\"`);");
            html.AppendLine("  csvRows.push(headers.join(','));");
            html.AppendLine("  filteredData.forEach(s => {");
            html.AppendLine("    const row = [s.rank, s.symbol, s.name, s.score, s.crash, s.pcount, s.pattern, s.d0, s.d1, s.d2, s.d3, s.d4, s.avg, s.trend, s.netStr, s.netAmountStr, s.action, s.stage, s.suggestion, s.price, s.chg];");
            html.AppendLine("    csvRows.push(row.map(v => `\"${String(v).replace(/\"/g, '\"\"')}\"`).join(','));");
            html.AppendLine("  });");
            html.AppendLine("  const csvString = csvRows.join('\\r\\n');");
            html.AppendLine("  const blob = new Blob([new Uint8Array([0xEF, 0xBB, 0xBF]), csvString], { type: 'text/csv;charset=utf-8;' });");
            html.AppendLine("  const link = document.createElement('a');");
            html.AppendLine("  const dateText = '" + latestKLineDateText.Replace("/", "-") + "';");
            html.AppendLine("  link.href = URL.createObjectURL(blob);");
            html.AppendLine("  link.download = `StockTracker_Ranking_${dateText}.csv`;");
            html.AppendLine("  link.click();");
            html.AppendLine("  URL.revokeObjectURL(link.href);");
            html.AppendLine("});");

            // 初始次載入
            html.AppendLine("renderBatch();");

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
                    if (totalChecked % 25 == 0 || totalChecked == distinctSymbols.Count)
                    {
                        ProgressValue = ((double)totalChecked / distinctSymbols.Count) * 50; // 下載佔 50%
                        ProgressText = $"下載K線資料至第 {totalChecked} 檔股票，共 {distinctSymbols.Count} 檔 4 碼股票";
                        await System.Windows.Threading.Dispatcher.Yield();
                    }
                }

                // 第二階段：多執行緒計算推薦指標
                ProgressText = "分析K線資料計算分數中...";
                await System.Windows.Threading.Dispatcher.Yield();

                int analyzeChecked = 0;
                var lockObj = new object();
                var t86HistoryMap = await _mainViewModel.LoadAllTwseT86HistoriesForScanAsync(scanHistoryStartDate);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1)
                };

                await Task.Run(() =>
                {
                    Parallel.ForEach(symbolDataMap, parallelOptions, kvp =>
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
                            dummyVm.LoadCandlesForAnalysis(candles);

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
                            var yesterdayPrice = enrichedCandles.Count > 1 ? (double?)enrichedCandles[enrichedCandles.Count - 2].Close : null;
                            var price20DaysAgo = enrichedCandles.Count > 20 ? (double?)enrichedCandles[enrichedCandles.Count - 21].Close : null;
                            var latestVolume = enrichedCandles.Count > 0 ? (double?)enrichedCandles[enrichedCandles.Count - 1].Volume : null;
                            var avgVolume20 = enrichedCandles.Count == 0
                                ? (double?)null
                                : enrichedCandles.Skip(Math.Max(0, enrichedCandles.Count - 20)).Average(x => (double)x.Volume);
                            var latestCandle = enrichedCandles.Count > 0 ? enrichedCandles.Last() : null;
                            var latestOpenPrice = latestCandle != null ? (double?)latestCandle.Open : null;
                            var strategyOutput = AdvancedTradingStrategyEngine.EvaluateStrategy(
                                latestRecommendation,
                                recentRecommendations,
                                0d,
                                (double)dummyVm.LatestPrice,
                                yesterdayPrice,
                                price20DaysAgo,
                                dummyVm.MA5,
                                dummyVm.MA20,
                                previousMa20,
                                0d,
                                latestVolume,
                                avgVolume20,
                                latestCandle?.MA60,
                                latestCandle?.MA120,
                                latestCandle?.MA240,
                                latestOpenPrice,
                                null,
                                null);

                            // 使用儀表板版本的 FinalScore（EMA 平滑後）作為顯示分數
                            latestScore = strategyOutput?.FinalScore ?? latestRecommendation.Score;

                            long latestNet = ResolveThreeMajorNetByDate(t86History, scoreDate);
                            long foreignNet = ResolveForeignNetByDate(t86History, scoreDate);
                            long dealerNet = ResolveDealerNetByDate(t86History, scoreDate);
                            long trustNet = ResolveInvestmentTrustNetByDate(t86History, scoreDate);

                            lock (lockObj)
                            {
                                var latestPatternTags = latestRecommendation.PatternTags ?? new List<PatternTag>();
                                var scoreReasonText = latestRecommendation.Reasons != null && latestRecommendation.Reasons.Count > 0
                                    ? string.Join(" | ", latestRecommendation.Reasons)
                                    : string.Empty;

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
                                    ForeignNet = foreignNet,
                                    DealerNet = dealerNet,
                                    InvestmentTrustNet = trustNet,
                                    RecentScores = recentScores,
                                    ScoreReason = scoreReasonText
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
                Stock0050 = results.FirstOrDefault(r => r.Symbol == "0050");
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

                // 計算策略評分（與儀表板一致），取 FinalScore 作為近期分數
                var prevMa20 = subset.Count > 1 ? (double?)subset[subset.Count - 2].MA20 : null;
                var ydPrice = subset.Count > 1 ? (double?)subset[subset.Count - 2].Close : null;
                var p20DaysAgo = subset.Count > 20 ? (double?)subset[subset.Count - 21].Close : null;
                var vol = subset.Count > 0 ? (double?)subset[subset.Count - 1].Volume : null;
                var avgVol = subset.Count == 0 ? (double?)null : subset.Skip(Math.Max(0, subset.Count - 20)).Average(x => (double)x.Volume);
                var openPx = (double?)latestCandle.Open;
                var recentForPoint = result.RecentRecommendations.Count > 1
                    ? result.RecentRecommendations.Take(result.RecentRecommendations.Count - 1).ToList()
                    : new List<TrendRecommendationResult>();
                var strategyPoint = AdvancedTradingStrategyEngine.EvaluateStrategy(
                    recommendation, recentForPoint, 0d, (double)latestCandle.Close,
                    ydPrice, p20DaysAgo, latestCandle.MA5, latestCandle.MA20, prevMa20,
                    0d, vol, avgVol, latestCandle.MA60, latestCandle.MA120, latestCandle.MA240,
                    openPx, null, null);
                var displayScore = strategyPoint?.FinalScore ?? recommendation.Score;

                result.RecentRecommendations.Add(recommendation);
                result.RecentScores.Add(new RankedStockScorePoint
                {
                    Date = latestCandle.Time.Date,
                    Score = displayScore
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

        private static long ResolveForeignNetByDate(TwseT86History t86History, DateTime targetDate)
        {
            if (t86History == null || t86History.RecordsByDate == null || t86History.RecordsByDate.Count == 0)
                return 0;
            TwseT86Record exactRecord;
            if (t86History.RecordsByDate.TryGetValue(targetDate.Date, out exactRecord) && exactRecord != null)
                return exactRecord.ForeignNet;
            return 0;
        }

        private static long ResolveDealerNetByDate(TwseT86History t86History, DateTime targetDate)
        {
            if (t86History == null || t86History.RecordsByDate == null || t86History.RecordsByDate.Count == 0)
                return 0;
            TwseT86Record exactRecord;
            if (t86History.RecordsByDate.TryGetValue(targetDate.Date, out exactRecord) && exactRecord != null)
                return exactRecord.DealerNet;
            return 0;
        }

        private static long ResolveInvestmentTrustNetByDate(TwseT86History t86History, DateTime targetDate)
        {
            if (t86History == null || t86History.RecordsByDate == null || t86History.RecordsByDate.Count == 0)
                return 0;
            TwseT86Record exactRecord;
            if (t86History.RecordsByDate.TryGetValue(targetDate.Date, out exactRecord) && exactRecord != null)
                return exactRecord.InvestmentTrustNet;
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

        private static string ResolveValueColorClass(double value)
        {
            if (value > 0)
            {
                return "rise";
            }

            if (value < 0)
            {
                return "fall";
            }

            return "flat";
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
