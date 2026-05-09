using StockManager.Library;
using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    public class RankedStock
    {
        public int Rank { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public decimal LatestPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public int Score { get; set; }
        public string Suggestion { get; set; }
        public long ThreeMajorNet { get; set; }
        public string NetDisplay => ThreeMajorNet > 0 ? $"+{ThreeMajorNet:N0}" : ThreeMajorNet.ToString("N0");
        public System.Windows.Media.Brush ChangePercentBrush => ChangePercent > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                                  ChangePercent < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                                  System.Windows.Media.Brushes.Gray;
        public System.Windows.Media.Brush NetDisplayBrush => ThreeMajorNet > 0 ? System.Windows.Media.Brushes.IndianRed :
                                                               ThreeMajorNet < 0 ? System.Windows.Media.Brushes.MediumSeaGreen :
                                                               System.Windows.Media.Brushes.Gray;
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
        private int _topCount = 100;

        public RankingViewModel(CapitalApiService apiService, MainWindowViewModel mainViewModel)
        {
            _apiService = apiService;
            _mainViewModel = mainViewModel;
            _dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T86_History", "Ranking.db");
            EnsureDatabase();
            StartScanningCommand = new RelayCommand(async _ => await ScanAllStocksAsync(), _ => !_isScanning);

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

        public int TopCount
        {
            get => _topCount;
            set { _topCount = value; OnPropertyChanged(); _rankedStocksView.Refresh(); }
        }

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
                            Suggestion TEXT NOT NULL,
                            ThreeMajorNet INTEGER NOT NULL DEFAULT 0
                        );";
                    cmd.ExecuteNonQuery();
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
                        cmd.CommandText = "SELECT Rank, Symbol, Name, LatestPrice, ChangePercent, Score, Suggestion, ThreeMajorNet FROM LatestRanking ORDER BY Rank ASC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                loaded.Add(new RankedStock
                                {
                                    Rank = reader.GetInt32(0),
                                    Symbol = reader.GetString(1),
                                    Name = reader.GetString(2),
                                    LatestPrice = reader.GetDecimal(3),
                                    ChangePercent = reader.GetDecimal(4),
                                    Score = reader.GetInt32(5),
                                    Suggestion = reader.GetString(6),
                                    ThreeMajorNet = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
                                });
                            }
                        }
                    }
                }
                
                if (loaded.Count > 0)
                {
                    foreach (var s in loaded)
                        RankedStocks.Add(s);
                    ProgressText = $"已載入上次儲存的排行 ({loaded.Count} 筆)";
                }
            }
            catch (Exception ex)
            {
                ProgressText = $"讀取存檔時發生錯誤: {ex.Message}";
            }
        }

        private void SaveRankingToDb(List<RankedStock> topResults)
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
                                INSERT INTO LatestRanking (Rank, Symbol, Name, LatestPrice, ChangePercent, Score, Suggestion, ThreeMajorNet)
                                VALUES (@rank, @sym, @name, @price, @change, @score, @sugg, @net)";
                            foreach (var s in topResults)
                            {
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@rank", s.Rank);
                                cmd.Parameters.AddWithValue("@sym", s.Symbol);
                                cmd.Parameters.AddWithValue("@name", s.Name);
                                cmd.Parameters.AddWithValue("@price", s.LatestPrice);
                                cmd.Parameters.AddWithValue("@change", s.ChangePercent);
                                cmd.Parameters.AddWithValue("@score", s.Score);
                                cmd.Parameters.AddWithValue("@sugg", s.Suggestion);
                                cmd.Parameters.AddWithValue("@net", s.ThreeMajorNet);
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

		MainWindow.BuildDateRangeForBars( "日K", 300, out var startDate, out var endDate);

                int kLineCount = -1;

                foreach (var symbol in distinctSymbols)
                {
                    var stockInfo = _apiService.GetRelativeStockMessage(symbol);

                    if (!string.IsNullOrEmpty(stockInfo.bstrStockName))
                    {
                        var tcs = new TaskCompletionSource<List<CandleData>>();
                        var candles = new List<CandleData>();
                        
                        Action<string, CandleData> onKLineReceived = null;
                        
                        onKLineReceived = (incomingSymbol, candle) => 
                        {
                            if (incomingSymbol == symbol)
                            {
                                candles.Add(candle);
                                // The API doesn't tell us when a stream ends, so we extend the timeout per candle received
                            }
                        };
                        
                        _apiService.KLineDataReceived += onKLineReceived;
                        
                        // Request daily K-lines for analysis
                        _apiService.RequestKLineByDate(symbol, 4, 1, 0, startDate, endDate, 0);

                        if(kLineCount == -1)
                        {
			    await Task.Delay(2000); // Wait for the stream to complete receiving for this specific stock
                            kLineCount = candles.Count;
			}
                        else
			{
			    var start = DateTime.UtcNow;
			    while (kLineCount >= candles.Count)
			    {
				await Task.Delay(100);
				if ((DateTime.UtcNow - start).TotalSeconds > 2) // max 2 seconds wait for new candles, then assume stream is done
				{
                                    kLineCount = Math.Min(kLineCount, candles.Count);
				    break;
				}
			    }
			}

			_apiService.KLineDataReceived -= onKLineReceived;
                        
                        if (candles.Any())
                        {
                            candles.Sort((a,b) => a.Time.CompareTo(b.Time));
                            
                            // Initialize indicators
                            var dummyVm = new StockViewModel(symbol, stockInfo.bstrStockName);
                            foreach(var c in candles)
                            {
                                dummyVm.UpdateFromKLine(c);
                            }
                            
                            var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                                dummyVm.GetPublicCandles(), 
                                (double)dummyVm.LatestPrice,
                                (double?)dummyVm.ChangePercent,
                                (double)candles[Math.Max(0, candles.Count - 2)].Close,
                                new TwseT86History 
                                { 
                                    Symbol = symbol, 
                                    Name = stockInfo.bstrStockName, 
                                    RecordsByDate = _mainViewModel.TwseT86Histories
                                        .FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                                        ?.RecordsByDate ?? new Dictionary<DateTime, TwseT86Record>()
                                },
                                candles.Last().Time);

                            var t86History = _mainViewModel.TwseT86Histories.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                            long latestNet = 0;
                            if (t86History != null && t86History.RecordsByDate.Any())
                            {
                                latestNet = t86History.RecordsByDate.OrderByDescending(r => r.Key).First().Value.ThreeMajorNet;
                            }

                            results.Add(new RankedStock
                            {
                                Symbol = symbol,
                                Name = stockInfo.bstrStockName,
                                LatestPrice = dummyVm.LatestPrice,
                                ChangePercent = dummyVm.ChangePercent,
                                Score = recommendation.Score,
                                ThreeMajorNet = latestNet
                            });

                            candles.Clear();
			}
                        
                        // Force UI refresh after processing each stock since they can take a while
                        System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                            OnPropertyChanged(nameof(RankedStocks));
                        });
                    }

                    totalChecked++;
                    ProgressValue = ((double)totalChecked / distinctSymbols.Count) * 100;
		    ProgressText = $"分析至第 {totalChecked} 檔股票，共 {distinctSymbols.Count} 檔 4 碼股票";
                    
                    // Dispatcher to give UI thread a slice to update property binding bindings immediately
                    await System.Windows.Threading.Dispatcher.Yield();
		}

                // 依 Score 由高至低排序
                results = results.OrderByDescending(r => r.Score).ToList();

                RankedStocks.Clear();
                for (int i = 0; i < results.Count; i++)
                {
                    results[i].Rank = i + 1;
                    results[i].Suggestion = TradingRecommendationLibrary.GetAdvancedSuggestion(results[i].Score);
                    RankedStocks.Add(results[i]);
                }

                SaveRankingToDb(results.Take(TopCount).ToList());

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
    }
}