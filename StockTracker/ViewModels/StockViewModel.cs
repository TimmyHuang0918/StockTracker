using StockManager.Library;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace StockTracker.ViewModels
{
    public class StockViewModel : ViewModelBase
    {
        private const double CandleChartHeight = 200;
        private const double MacdChartHeight = 120;
        private const double RsiChartHeight = 90;
        private const double VolumeChartHeight = 100;
        private const double ThreeMajorChartHeight = 100;
        private const int MinDisplayPoints = 20;

        private readonly List<CandleData> _candles = new List<CandleData>();
        private readonly List<StockViewModel> _detailViewModels = new List<StockViewModel>();
        private readonly List<double> _macdSeries = new List<double>();
        private readonly List<double> _signalSeries = new List<double>();
        private readonly List<SignalMarkerData> _signalHistory = new List<SignalMarkerData>();
        private readonly Dictionary<DateTime, TwseT86Record> _twseByDate = new Dictionary<DateTime, TwseT86Record>();
        private readonly Dictionary<DateTime, TwseMarginRecord> _marginByDate = new Dictionary<DateTime, TwseMarginRecord>();

        private decimal _latestPrice;
        private decimal _changePercent;
        private Brush _latestChangeBrush = Brushes.Gainsboro;
        private double _ma5;
        private double _ma20;
        private double _ma120;
        private double _ma240;
        private double _macd;
        private double _rsi;
        private double _latestPriceY;
        private double _macdZeroY;
        private double _threeMajorZeroY;
        private string _selectedKLineInterval = "1分K";
        private string _selectedKLineCount = "300";
        private string _signal = "中立";
        private string _lastNotifiedSignal = string.Empty;
        private List<string> _latestRecommendationReasons = new List<string>();
        private int _maxDisplayPoints = 60;
        private readonly List<CandleData> _lastDisplayCandles = new List<CandleData>();
        private readonly List<double> _lastDisplayMacdSeries = new List<double>();
        private readonly List<double> _lastDisplaySignalSeries = new List<double>();
        private readonly List<double> _lastDisplayRsiSeries = new List<double>();
        private readonly List<MarginBalancePointVisual> _lastDisplayMarginBalanceSeries = new List<MarginBalancePointVisual>();
        private readonly List<ThreeMajorPointData> _lastDisplayThreeMajorSeries = new List<ThreeMajorPointData>();
        private readonly Dictionary<DateTime, int> _recommendationScoreCache = new Dictionary<DateTime, int>();
        private readonly Dictionary<DateTime, List<string>> _recommendationReasonsCache = new Dictionary<DateTime, List<string>>();
        private double _lastMinPrice;
        private double _lastPriceRange = 1;
        private double _crosshairX;
        private double _crosshairY;
        private Visibility _crosshairVisibility = Visibility.Collapsed;
        private string _hoverInfo;
        private double _marginCrosshairX;
        private Visibility _marginCrosshairVisibility = Visibility.Collapsed;
        private string _marginHoverInfo;
        private double _threeMajorCrosshairX;
        private Visibility _threeMajorCrosshairVisibility = Visibility.Collapsed;
        private string _threeMajorHoverInfo;
        private double _chartPaddingWidth { get { return ChartWidth - 20; } }

        public StockViewModel(string symbol, string name)
        {
            Symbol = symbol;
            Name = name;

            Candles = new ObservableCollection<CandlestickVisual>();
            MacdHistogram = new ObservableCollection<HistogramBarVisual>();
            VolumeBars = new ObservableCollection<HistogramBarVisual>();
            MarginBalanceBars = new ObservableCollection<HistogramBarVisual>();
            SignalMarkers = new ObservableCollection<SignalMarkerVisual>();
            TimeLabels = new ObservableCollection<TimeLabelVisual>();
            PriceLevels = new ObservableCollection<PriceLevelVisual>();
            MacdLevels = new ObservableCollection<PriceLevelVisual>();
            RsiLevels = new ObservableCollection<PriceLevelVisual>();
            VolumeLevels = new ObservableCollection<PriceLevelVisual>();
            MarginBalanceLevels = new ObservableCollection<PriceLevelVisual>();

            Ma5Points = new PointCollection();
            Ma20Points = new PointCollection();
            Ma120Points = new PointCollection();
            Ma240Points = new PointCollection();
            BollingerUpperPoints = new PointCollection();
            BollingerMiddlePoints = new PointCollection();
            BollingerLowerPoints = new PointCollection();
            MacdLinePoints = new PointCollection();
            SignalLinePoints = new PointCollection();
            RsiLinePoints = new PointCollection();
            ThreeMajorNetPoints = new PointCollection();
            ForeignNetPoints = new PointCollection();
            InvestmentTrustNetPoints = new PointCollection();
            DealerNetPoints = new PointCollection();
            ThreeMajorLevels = new ObservableCollection<PriceLevelVisual>();
        }

        public string Symbol { get; }
        public string Name { get; }

        public decimal LatestPrice
        {
            get => _latestPrice;
            private set
            {
                if (_latestPrice == value)
                    return;
                _latestPrice = value;
                OnPropertyChanged();
            }
        }

        public double ThreeMajorZeroY
        {
            get => _threeMajorZeroY;
            private set
            {
                _threeMajorZeroY = value;
                OnPropertyChanged();
            }
        }

        public decimal ChangePercent
        {
            get => _changePercent;
            private set
            {
                if (_changePercent == value)
                    return;
                _changePercent = value;
                OnPropertyChanged(nameof(ChangePercent));
                OnPropertyChanged(nameof(ChangePercentText));
                UpdateLatestChangeBrush();
            }
        }

        public string ChangePercentText => ChangePercent.ToString("+0.00;-0.00;0.00") + "%";

        public Brush LatestChangeBrush
        {
            get => _latestChangeBrush;
            private set
            {
                _latestChangeBrush = value;
                OnPropertyChanged();
            }
        }

        public double MA5
        {
            get => _ma5;
            private set
            {
                _ma5 = value;
                OnPropertyChanged();
            }
        }

        public double MA20
        {
            get => _ma20;
            private set
            {
                _ma20 = value;
                OnPropertyChanged();
            }
        }

        public double MA120
        {
            get => _ma120;
            private set
            {
                _ma120 = value;
                OnPropertyChanged();
            }
        }

        public double MA240
        {
            get => _ma240;
            private set
            {
                _ma240 = value;
                OnPropertyChanged();
            }
        }

        public double MACD
        {
            get => _macd;
            private set
            {
                _macd = value;
                OnPropertyChanged();
            }
        }

        public double RSI
        {
            get => _rsi;
            private set
            {
                _rsi = value;
                OnPropertyChanged();
            }
        }

        public string Signal
        {
            get => _signal;
            private set
            {
                _signal = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> KLineIntervals { get; } = new[] { "日K", "5分K", "3分K", "1分K" };

        public string SelectedKLineInterval
        {
            get => _selectedKLineInterval;
            set
            {
                if (_selectedKLineInterval == value || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                _selectedKLineInterval = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginBalanceChartVisibility));
                OnPropertyChanged(nameof(ThreeMajorChartVisibility));
                OnPropertyChanged(nameof(_chartPaddingWidth));
                RebuildVisuals();
            }
        }

        public string SelectedKLineCount
        {
            get => _selectedKLineCount;
            set
            {
                if (_selectedKLineCount == value || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                _selectedKLineCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(_chartPaddingWidth));
                RebuildVisuals();
            }
        }

        public StockViewModel CreateDetailViewModel()
        {
            var detailVm = new StockViewModel(Symbol, Name)
            {
                _selectedKLineInterval = _selectedKLineInterval,
                _maxDisplayPoints = _maxDisplayPoints
            };

            detailVm.OnPropertyChanged(nameof(SelectedKLineInterval));
            detailVm.OnPropertyChanged(nameof(ChartWidth));

            detailVm.SetTwseT86Records(_twseByDate.Values);
            detailVm.SetTwseMarginRecords(_marginByDate.Values);
            foreach (var candle in _candles)
            {
                detailVm.UpdateFromKLine(candle);
            }


            _detailViewModels.Add(detailVm);
            return detailVm;
        }

        public void DetachDetailViewModel(StockViewModel detailVm)
        {
            if (detailVm == null)
            {
                return;
            }

            _detailViewModels.Remove(detailVm);
        }

        public double ChartWidth => CalculateChartWidth(GetDisplayCandles().Count);
        public double LatestPriceY
        {
            get => _latestPriceY;
            private set
            {
                _latestPriceY = value;
                OnPropertyChanged();
            }
        }

        public double MacdZeroY
        {
            get => _macdZeroY;
            private set
            {
                _macdZeroY = value;
                OnPropertyChanged();
            }
        }

        public decimal LatestVolume => _candles.Count == 0 ? 0 : _candles.Last().Volume;
        public long LatestMarginBalance => _marginByDate.Count == 0 ? 0 : _marginByDate.OrderBy(x => x.Key).Last().Value.MarginBalance;
        public double MacdSignal => _signalSeries.LastOrDefault();
        public double MacdHistogramValue => (_macdSeries.Any() && _signalSeries.Any()) ? _macdSeries.Last() - _signalSeries.Last() : 0;

        public ObservableCollection<CandlestickVisual> Candles { get; }
        public PointCollection Ma5Points { get; private set; }
        public PointCollection Ma20Points { get; private set; }
        public PointCollection Ma120Points { get; private set; }
        public PointCollection Ma240Points { get; private set; }
        public PointCollection BollingerUpperPoints { get; private set; }
        public PointCollection BollingerMiddlePoints { get; private set; }
        public PointCollection BollingerLowerPoints { get; private set; }

        public ObservableCollection<HistogramBarVisual> MacdHistogram { get; }
        public PointCollection MacdLinePoints { get; private set; }
        public PointCollection SignalLinePoints { get; private set; }

        public PointCollection RsiLinePoints { get; private set; }
        public PointCollection ThreeMajorNetPoints { get; private set; }
        public PointCollection ForeignNetPoints { get; private set; }
        public PointCollection InvestmentTrustNetPoints { get; private set; }
        public PointCollection DealerNetPoints { get; private set; }
        public ObservableCollection<HistogramBarVisual> VolumeBars { get; }
        public ObservableCollection<HistogramBarVisual> MarginBalanceBars { get; }
        public ObservableCollection<SignalMarkerVisual> SignalMarkers { get; }
        public ObservableCollection<TimeLabelVisual> TimeLabels { get; }
        public ObservableCollection<PriceLevelVisual> PriceLevels { get; }
        public ObservableCollection<PriceLevelVisual> MacdLevels { get; }
        public ObservableCollection<PriceLevelVisual> RsiLevels { get; }
        public ObservableCollection<PriceLevelVisual> VolumeLevels { get; }
        public ObservableCollection<PriceLevelVisual> MarginBalanceLevels { get; }
        public ObservableCollection<PriceLevelVisual> ThreeMajorLevels { get; }
        public Visibility MarginBalanceChartVisibility => SelectedKLineInterval == "日K" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ThreeMajorChartVisibility => SelectedKLineInterval == "日K" ? Visibility.Visible : Visibility.Collapsed;

        public double MarginCrosshairX
        {
            get => _marginCrosshairX;
            private set
            {
                _marginCrosshairX = value;
                OnPropertyChanged();
            }
        }

        public Visibility MarginCrosshairVisibility
        {
            get => _marginCrosshairVisibility;
            private set
            {
                _marginCrosshairVisibility = value;
                OnPropertyChanged();
            }
        }

        public string MarginHoverInfo
        {
            get => _marginHoverInfo;
            private set
            {
                _marginHoverInfo = value;
                OnPropertyChanged();
            }
        }

        public double ThreeMajorCrosshairX
        {
            get => _threeMajorCrosshairX;
            private set
            {
                _threeMajorCrosshairX = value;
                OnPropertyChanged();
            }
        }

        public Visibility ThreeMajorCrosshairVisibility
        {
            get => _threeMajorCrosshairVisibility;
            private set
            {
                _threeMajorCrosshairVisibility = value;
                OnPropertyChanged();
            }
        }

        public string ThreeMajorHoverInfo
        {
            get => _threeMajorHoverInfo;
            private set
            {
                _threeMajorHoverInfo = value;
                OnPropertyChanged();
            }
        }

        public double CrosshairX
        {
            get => _crosshairX;
            private set
            {
                _crosshairX = value;
                OnPropertyChanged();
            }
        }

        public double CrosshairY
        {
            get => _crosshairY;
            private set
            {
                _crosshairY = value;
                OnPropertyChanged();
            }
        }

        public Visibility CrosshairVisibility
        {
            get => _crosshairVisibility;
            private set
            {
                _crosshairVisibility = value;
                OnPropertyChanged();
            }
        }

        public string HoverInfo
        {
            get => _hoverInfo;
            private set
            {
                _hoverInfo = value;
                OnPropertyChanged();
            }
        }

        public event Action<StockViewModel, string> SignalTriggered;

        public void UpdateDisplayCapacity(double availableWidth)
        {
            if (availableWidth <= 0)
            {
                return;
            }

            var candidate = Math.Max(MinDisplayPoints, (int)Math.Floor((availableWidth - 100) / 12));
            if (candidate == _maxDisplayPoints)
            {
                return;
            }

            _maxDisplayPoints = candidate;

            OnPropertyChanged(nameof(ChartWidth));
            RebuildVisuals();
        }

        public void UpdateCrosshair(double x, double y)
        {
            if (_lastDisplayCandles.Count == 0)
            {
                CrosshairVisibility = Visibility.Collapsed;
                HoverInfo = null;
                return;
            }

            var clampedY = Math.Max(0, Math.Min(CandleChartHeight, y));
            var nearestIndex = GetNearestCandleIndex(x, _lastDisplayCandles.Count, _chartPaddingWidth);
            var clampedX = Math.Max(0, Math.Min(_chartPaddingWidth, x));
            var candle = _lastDisplayCandles[nearestIndex];
            var priceAtCursor = _lastMinPrice + ((_lastPriceRange * (CandleChartHeight - clampedY - 5)) / Math.Max(1, CandleChartHeight - 10));
            var macd = nearestIndex < _lastDisplayMacdSeries.Count ? _lastDisplayMacdSeries[nearestIndex] : 0;
            var signal = nearestIndex < _lastDisplaySignalSeries.Count ? _lastDisplaySignalSeries[nearestIndex] : 0;
            var hist = macd - signal;
            var rsi = nearestIndex < _lastDisplayRsiSeries.Count ? _lastDisplayRsiSeries[nearestIndex] : 50;

            CrosshairX = clampedX;
            CrosshairY = clampedY;
            CrosshairVisibility = Visibility.Visible;
            HoverInfo =
                $"時間: {candle.Time:yyyy/MM/dd HH:mm}" +
                $"\n開: {candle.Open:F2}  高: {candle.High:F2}  低: {candle.Low:F2}  收: {candle.Close:F2}" +
                $"\n漲跌幅: {candle.PercentageChange:F2}%  游標價: {priceAtCursor:F2}  成交量: {candle.Volume:N0}" +
                $"\nMA5: {candle.MA5:F2}  MA20: {candle.MA20:F2}  MA120: {candle.MA120:F2}  MA240: {candle.MA240:F2}" +
                $"\nBB上軌: {candle.BollingerUpper:F2}  中軌: {candle.BollingerMiddle:F2}  下軌: {candle.BollingerLower:F2}" +
                $"\nMACD: {macd:F4}  DEA: {signal:F4}  柱狀: {hist:F4}" +
                $"\nRSI: {rsi:F2}" +
                BuildRecommendationTooltip(nearestIndex, candle);
        }

        public void ClearCrosshair()
        {
            CrosshairVisibility = Visibility.Collapsed;
            HoverInfo = null;
        }

        public void ApplyInstantCandle(CandleData instantCandle, string interval)
        {
            if (instantCandle == null)
            {
                return;
            }

            var alignedTime = AlignTimeToInterval(instantCandle.Time, interval);
            var incoming = new CandleData
            {
                Time = alignedTime,
                Open = instantCandle.Open,
                High = instantCandle.High,
                Low = instantCandle.Low,
                Close = instantCandle.Close,
                Volume = instantCandle.Volume
            };

            var existing = _candles.FirstOrDefault(x => x.Time == alignedTime);
            if (existing != null)
            {
                incoming.Open = existing.Open;
                incoming.High = Math.Max(existing.High, incoming.High);
                incoming.Low = Math.Min(existing.Low, incoming.Low);
                incoming.Volume = existing.Volume + incoming.Volume;
            }

            UpdateFromKLine(incoming);
        }

        public void UpdateFromKLine(CandleData candle)
        {
            if (candle == null)
            {
                return;
            }

            var normalized = new CandleData
            {
                Time = candle.Time,
                Open = Math.Round(candle.Open, 2),
                High = Math.Round(candle.High, 2),
                Low = Math.Round(Math.Max(0.1m, candle.Low), 2),
                Close = Math.Round(candle.Close, 2),
                Volume = candle.Volume
            };

            var existingIndex = _candles.FindIndex(x => x.Time == normalized.Time);
            var appendedToTail = false;

            if (existingIndex >= 0)
            {
                _candles[existingIndex] = normalized;
            }
            else
            {
                _candles.Add(normalized);
                _candles.Sort((a, b) => a.Time.CompareTo(b.Time));
                appendedToTail = _candles.Count > 0 && _candles[_candles.Count - 1].Time == normalized.Time;
            }

            if (!appendedToTail)
            {
                _signalHistory.Clear();
                _lastNotifiedSignal = string.Empty;
            }

            var latest = _candles.LastOrDefault();
            LatestPrice = latest?.Close ?? normalized.Close;
            RecalculateIndicatorsOnCandles();
            ChangePercent = latest.PercentageChange;
            UpdateSignal();
            RebuildVisuals();
            OnPropertyChanged(nameof(LatestVolume));

            foreach (var detailVm in _detailViewModels.ToList())
            {
                detailVm.UpdateFromKLine(normalized);
            }
        }

        public void ClearData()
        {
            _candles.Clear();
            _signalHistory.Clear();
            _lastNotifiedSignal = string.Empty;
            LatestPrice = 0;
            ChangePercent = 0;
            UpdateLatestChangeBrush();
            MA5 = 0;
            MA20 = 0;
            MACD = 0;
            RSI = 0;
            LatestPriceY = 0;
            MacdZeroY = 0;
            Candles.Clear();
            Ma5Points.Clear();
            Ma20Points.Clear();
            MacdHistogram.Clear();
            MacdLinePoints.Clear();
            SignalLinePoints.Clear();
            RsiLinePoints.Clear();
            VolumeBars.Clear();
            SignalMarkers.Clear();
            TimeLabels.Clear();
            PriceLevels.Clear();
            MacdLevels.Clear();
            RsiLevels.Clear();
            VolumeLevels.Clear();
            MarginBalanceBars.Clear();
            MarginBalanceLevels.Clear();
            ThreeMajorLevels.Clear();
            ThreeMajorNetPoints.Clear();
            ForeignNetPoints.Clear();
            InvestmentTrustNetPoints.Clear();
            DealerNetPoints.Clear();
            _recommendationScoreCache.Clear();
            _recommendationReasonsCache.Clear();
            _lastDisplayMarginBalanceSeries.Clear();
            MarginCrosshairVisibility = Visibility.Collapsed;
            MarginHoverInfo = null;
            _lastDisplayThreeMajorSeries.Clear();
            ThreeMajorZeroY = 0;
            ThreeMajorCrosshairVisibility = Visibility.Collapsed;
            ThreeMajorHoverInfo = null;
            _lastDisplayMacdSeries.Clear();
            _lastDisplaySignalSeries.Clear();
            _lastDisplayRsiSeries.Clear();
            ClearCrosshair();
            OnPropertyChanged(nameof(LatestVolume));

            foreach (var detailVm in _detailViewModels.ToList())
            {
                detailVm.ClearData();
            }
        }

        public void SetTwseT86Records(IEnumerable<TwseT86Record> records)
        {
            _twseByDate.Clear();
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record == null)
                    {
                        continue;
                    }

                    _twseByDate[record.TradeDate.Date] = record;
                }
            }

            RebuildVisuals();

            foreach (var detailVm in _detailViewModels.ToList())
            {
                detailVm.SetTwseT86Records(_twseByDate.Values);
            }
        }

        public void SetTwseMarginRecords(IEnumerable<TwseMarginRecord> records)
        {
            _marginByDate.Clear();
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record == null)
                    {
                        continue;
                    }

                    _marginByDate[record.TradeDate.Date] = record;
                }
            }

            OnPropertyChanged(nameof(LatestMarginBalance));
            RebuildVisuals();

            foreach (var detailVm in _detailViewModels.ToList())
            {
                detailVm.SetTwseMarginRecords(_marginByDate.Values);
            }
        }

        private void RecalculateIndicatorsOnCandles()
        {
            if (_candles.Count == 0)
            {
                MA5 = 0;
                MA20 = 0;
                MA120 = 0;
                MACD = 0;
                RSI = 0;
                _macdSeries.Clear();
                _signalSeries.Clear();
                return;
            }

            var closes = _candles.Select(x => (double)x.Close).ToList();
            var ema12 = CalculateEmaSeries(closes, 12);
            var ema26 = CalculateEmaSeries(closes, 26);
            var macdSeries = new List<double>(_candles.Count);
            for (var i = 0; i < closes.Count; i++)
            {
                macdSeries.Add(ema12[i] - ema26[i]);
            }

            var signalSeries = CalculateEmaSeries(macdSeries, 9);

            for (var i = 0; i < _candles.Count; i++)
            {
                var ma5Start = Math.Max(0, i - 4);
                var ma20Start = Math.Max(0, i - 19);
                var ma120Start = Math.Max(0, i - 119);
                var ma240Start = Math.Max(0, i - 239);
                var ma5Count = i - ma5Start + 1;
                var ma20Count = i - ma20Start + 1;
                var ma120Count = i - ma120Start + 1;
                var ma240Count = i - ma240Start + 1;

                _candles[i].MA5 = closes.Skip(ma5Start).Take(ma5Count).Average();
                _candles[i].MA20 = closes.Skip(ma20Start).Take(ma20Count).Average();
                _candles[i].MA120 = closes.Skip(ma120Start).Take(ma120Count).Average();
                _candles[i].MA240 = closes.Skip(ma240Start).Take(ma240Count).Average();
                _candles[i].MACD = macdSeries[i];
                _candles[i].MacdSignal = signalSeries[i];
                _candles[i].MacdHistogram = macdSeries[i] - signalSeries[i];
                _candles[i].RSI = CalculateRsiAt(i, 14, closes);
                if (i > 0)
                    _candles[i].PercentageChange = (_candles[i].Close - _candles[i - 1].Close) / _candles[i - 1].Close * 100;

                // Bollinger Bands (20-period, 2 std dev)
                var bbSlice = closes.Skip(ma20Start).Take(ma20Count).ToList();
                var bbMid = _candles[i].MA20;
                var variance = bbSlice.Sum(v => (v - bbMid) * (v - bbMid)) / bbSlice.Count;
                var stdDev = Math.Sqrt(variance);
                _candles[i].BollingerMiddle = bbMid;
                _candles[i].BollingerUpper = bbMid + 2 * stdDev;
                _candles[i].BollingerLower = bbMid - 2 * stdDev;
            }

            var latest = _candles[_candles.Count - 1];
            MA5 = latest.MA5;
            MA20 = latest.MA20;
            MA120 = latest.MA120;
            MA240 = latest.MA240;
            MACD = latest.MACD;
            RSI = latest.RSI;

            _macdSeries.Clear();
            _macdSeries.AddRange(macdSeries);
            _signalSeries.Clear();
            _signalSeries.AddRange(signalSeries);

            OnPropertyChanged(nameof(MacdSignal));
            OnPropertyChanged(nameof(MacdHistogramValue));
        }

        private static List<double> CalculateEmaSeries(IReadOnlyList<double> source, int period)
        {
            var result = new List<double>();
            if (source.Count == 0)
            {
                return result;
            }

            var multiplier = 2.0 / (period + 1);
            var ema = source[0];
            result.Add(ema);

            for (var i = 1; i < source.Count; i++)
            {
                ema = (source[i] - ema) * multiplier + ema;
                result.Add(ema);
            }

            return result;
        }

        private void UpdateLatestChangeBrush()
        {
            if (ChangePercent > 0)
            {
                LatestChangeBrush = Brushes.IndianRed;
                return;
            }

            if (ChangePercent < 0)
            {
                LatestChangeBrush = Brushes.MediumSeaGreen;
                return;
            }

            LatestChangeBrush = Brushes.Gainsboro;
        }

        private void UpdateSignal()
        {
            if (_candles.Count < 20)
            {
                Signal = "資料不足";
                _latestRecommendationReasons.Clear();
                return;
            }

            var latestCandle = _candles.Last();

            if (_selectedKLineInterval == "日K")
            {
                var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                    _candles,
                    (double)LatestPrice,
                    (double?)ChangePercent,
                    (double)_candles[_candles.Count - 2].Close,
                    BuildTwseHistorySnapshot(),
                    latestCandle.Time);

                _latestRecommendationReasons = recommendation.Reasons ?? new List<string>();
                Signal = TradingRecommendationLibrary.GetAdvancedSuggestion(recommendation.Score);
                _recommendationScoreCache[latestCandle.Time] = recommendation.Score;
                _recommendationReasonsCache[latestCandle.Time] = _latestRecommendationReasons;

                var actionSignal = ResolveActionSignal(Signal);
                var shouldRecord = recommendation.Score >= 70 || recommendation.Score <= 30;
                var signalKey = $"{actionSignal}_{recommendation.Score / 15}";

                if (shouldRecord && signalKey != _lastNotifiedSignal)
                {
                    _lastNotifiedSignal = signalKey;
                    _signalHistory.Add(new SignalMarkerData
                    {
                        Index = _candles.Count - 1,
                        Time = latestCandle.Time,
                        Price = latestCandle.Close,
                        Signal = actionSignal,
                        Score = recommendation.Score
                    });
                    if (actionSignal == "買進訊號" || actionSignal == "賣出訊號")
                    {
                        SignalTriggered?.Invoke(this, actionSignal);
                    }
                }
            }
            else
            {
                var intradayRecommendation = TradingRecommendationLibrary.CalculateIntradayRecommendation(
                    _candles,
                    (double)LatestPrice);

                _latestRecommendationReasons = intradayRecommendation.Reasons ?? new List<string>();
                Signal = intradayRecommendation.Action;
                _recommendationReasonsCache[latestCandle.Time] = _latestRecommendationReasons;

                var actionSignal = ResolveActionSignal(Signal);
                int score = Signal.Contains("強烈") ? 100 : Signal.Contains("偏多") || Signal.Contains("偏空") ? 70 : 50;
                var shouldRecord = score >= 70;
                var signalKey = $"{actionSignal}_{score / 15}";

                if (shouldRecord && signalKey != _lastNotifiedSignal)
                {
                    _lastNotifiedSignal = signalKey;
                    _signalHistory.Add(new SignalMarkerData
                    {
                        Index = _candles.Count - 1,
                        Time = latestCandle.Time,
                        Price = latestCandle.Close,
                        Signal = actionSignal,
                        Score = score
                    });
                    if (actionSignal == "買進訊號" || actionSignal == "賣出訊號")
                    {
                        SignalTriggered?.Invoke(this, actionSignal);
                    }
                }
            }
        }

        private static string ResolveActionSignal(string suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                return "中立";
            }

            if (suggestion.Contains("買入") || suggestion.Contains("偏多") || suggestion.Contains("做多"))
            {
                return "買進訊號";
            }

            if (suggestion.Contains("賣出") || suggestion.Contains("偏空") || suggestion.Contains("做空"))
            {
                return "賣出訊號";
            }

            return "中立";
        }

        private string BuildRecommendationTooltip(int nearestIndex, CandleData candle)
        {
            if (_lastDisplayCandles == null || _lastDisplayCandles.Count == 0 || nearestIndex < 0 || nearestIndex >= _lastDisplayCandles.Count)
            {
                return string.Empty;
            }

            var marker = _signalHistory.LastOrDefault(x => x.Time == candle.Time);

            int score;
            List<string> reasons;
            if (!_recommendationScoreCache.TryGetValue(candle.Time, out score))
            {
                if (marker == null)
                {
                    return string.Empty;
                }
                score = marker.Score;
            }

            if (!_recommendationReasonsCache.TryGetValue(candle.Time, out reasons))
            {
                reasons = new List<string>();
            }

            var suggestion = TradingRecommendationLibrary.GetAdvancedSuggestion(score);
            var markerText = marker == null ? string.Empty : $"\n訊號標記: {marker.Signal}";
            var topReasons = reasons.Take(50).Select(r => $"- {r}");
            return $"\n建議: {suggestion} ({score}){markerText}\n建議理由:\n" + string.Join("\n", topReasons);
        }

        private void RebuildVisuals()
        {
            var displayOffset = Math.Max(0, _candles.Count - _maxDisplayPoints);
            var candles = _candles.Skip(displayOffset).ToList();
            if (!candles.Any())
            {
                return;
            }

            var minPrice = (double)candles.Min(x => x.Low);
            var maxPrice = (double)candles.Max(x => x.High);
            var priceRange = Math.Max(0.01, maxPrice - minPrice);
            _lastDisplayCandles.Clear();
            _lastDisplayCandles.AddRange(candles);
            _lastMinPrice = minPrice;
            _lastPriceRange = priceRange;
            _lastDisplayMacdSeries.Clear();
            _lastDisplaySignalSeries.Clear();
            _lastDisplayRsiSeries.Clear();
            _lastDisplayMacdSeries.AddRange(candles.Select(x => x.MACD));
            _lastDisplaySignalSeries.AddRange(candles.Select(x => x.MacdSignal));
            _lastDisplayRsiSeries.AddRange(candles.Select(x => x.RSI));

            Candles.Clear();
            var ma5Points = new PointCollection();
            var ma20Points = new PointCollection();
            var ma120Points = new PointCollection();
            var ma240Points = new PointCollection();
            var bollingerUpperPoints = new PointCollection();
            var bollingerMiddlePoints = new PointCollection();
            var bollingerLowerPoints = new PointCollection();

            for (var i = 0; i < candles.Count; i++)
            {
                var item = candles[i];
                var centerX = CalculateCenterX(i, candles.Count, _chartPaddingWidth);
                var x = centerX - 4;
                var openY = Scale((double)item.Open, minPrice, priceRange, CandleChartHeight);
                var closeY = Scale((double)item.Close, minPrice, priceRange, CandleChartHeight);
                var highY = Scale((double)item.High, minPrice, priceRange, CandleChartHeight);
                var lowY = Scale((double)item.Low, minPrice, priceRange, CandleChartHeight);

                Candles.Add(new CandlestickVisual
                {
                    X = x,
                    DateTime = item.Time,
                    WickTop = Math.Min(highY, lowY),
                    WickBottom = Math.Max(highY, lowY),
                    BodyTop = Math.Min(openY, closeY),
                    BodyHeight = Math.Max(2, Math.Abs(openY - closeY)),
                    BodyBrush = item.Close >= item.Open ? Brushes.IndianRed : Brushes.SeaGreen
                });
                ma5Points.Add(new Point(centerX, Scale(candles[i].MA5, minPrice, priceRange, CandleChartHeight)));
                ma20Points.Add(new Point(centerX, Scale(candles[i].MA20, minPrice, priceRange, CandleChartHeight)));
                if (candles[i].MA120 > 0)
                    ma120Points.Add(new Point(centerX, Scale(candles[i].MA120, minPrice, priceRange, CandleChartHeight)));
                if (candles[i].MA240 > 0)
                    ma240Points.Add(new Point(centerX, Scale(candles[i].MA240, minPrice, priceRange, CandleChartHeight)));
                if (candles[i].BollingerUpper > 0)
                {
                    bollingerUpperPoints.Add(new Point(centerX, Scale(candles[i].BollingerUpper, minPrice, priceRange, CandleChartHeight)));
                    bollingerMiddlePoints.Add(new Point(centerX, Scale(candles[i].BollingerMiddle, minPrice, priceRange, CandleChartHeight)));
                    bollingerLowerPoints.Add(new Point(centerX, Scale(candles[i].BollingerLower, minPrice, priceRange, CandleChartHeight)));
                }
            }

            Ma5Points = ma5Points;
            Ma20Points = ma20Points;
            Ma120Points = ma120Points;
            Ma240Points = ma240Points;
            BollingerUpperPoints = bollingerUpperPoints;
            BollingerMiddlePoints = bollingerMiddlePoints;
            BollingerLowerPoints = bollingerLowerPoints;
            LatestPriceY = Scale((double)LatestPrice, minPrice, priceRange, CandleChartHeight);
            OnPropertyChanged(nameof(Ma5Points));
            OnPropertyChanged(nameof(Ma20Points));
            OnPropertyChanged(nameof(Ma120Points));
            OnPropertyChanged(nameof(Ma240Points));
            OnPropertyChanged(nameof(BollingerUpperPoints));
            OnPropertyChanged(nameof(BollingerMiddlePoints));
            OnPropertyChanged(nameof(BollingerLowerPoints));
            OnPropertyChanged(nameof(_chartPaddingWidth));

            SignalMarkers.Clear();
            var listCandles = Candles.ToList();

            // Collect valid signals with their display indices
            var validSignals = new List<System.Tuple<int, SignalMarkerData>>();
            foreach (var signal in _signalHistory.Where(x => x.Index >= 0))
            {
                var displayIndex = listCandles.FindIndex(c => c.DateTime == signal.Time);
                if (displayIndex < 0)
                {
                    displayIndex = signal.Index - displayOffset;
                }
                if (displayIndex >= 0 && displayIndex < candles.Count)
                {
                    validSignals.Add(System.Tuple.Create(displayIndex, signal));
                }
            }

            // Filter: prefer strongest signal, keep min 5-candle gap
            var filteredSignals = new List<System.Tuple<int, SignalMarkerData>>();
            foreach (var item in validSignals.OrderByDescending(x => Math.Abs(x.Item2.Score - 50)))
            {
                if (!filteredSignals.Any(f => Math.Abs(f.Item1 - item.Item1) < 5))
                {
                    filteredSignals.Add(item);
                }
            }

            foreach (var entry in filteredSignals)
            {
                var displayIndex = entry.Item1;
                var signal = entry.Item2;

                var x = Candles[displayIndex].X;
                var candleHigh = (double)candles[displayIndex].High;
                var candleLow = (double)candles[displayIndex].Low;
                var highY = Scale(candleHigh, minPrice, priceRange, CandleChartHeight);
                var lowY = Scale(candleLow, minPrice, priceRange, CandleChartHeight);

                string text;
                Brush brush;
                double offsetY;

                if (signal.Score >= 85)
                {
                    text = "▲";
                    brush = Brushes.Red;
                    offsetY = highY - 22;
                }
                else if (signal.Score >= 70)
                {
                    text = "▲";
                    brush = Brushes.OrangeRed;
                    offsetY = highY - 20;
                }
                else if (signal.Score >= 55)
                {
                    text = "▲";
                    brush = Brushes.Orange;
                    offsetY = highY - 18;
                }
                else if (signal.Score <= 15)
                {
                    text = "▼";
                    brush = Brushes.DarkGreen;
                    offsetY = lowY + 8;
                }
                else if (signal.Score <= 30)
                {
                    text = "▼";
                    brush = Brushes.MediumSeaGreen;
                    offsetY = lowY + 8;
                }
                else
                {
                    text = "▼";
                    brush = Brushes.LightGreen;
                    offsetY = lowY + 8;
                }

                SignalMarkers.Add(new SignalMarkerVisual
                {
                    X = x - 2,
                    Y = offsetY,
                    Text = text,
                    Brush = brush
                });
            }

            TimeLabels.Clear();
            var labelStep = Math.Max(1, candles.Count / 8);
            for (var i = 0; i < candles.Count; i += labelStep)
            {
                var x = CalculateCenterX(i, candles.Count, _chartPaddingWidth);
                var timeText = candles[i].Time.ToString(SelectedKLineInterval == "日K" ? "MM/dd" : "MM/dd HH:mm");
                TimeLabels.Add(new TimeLabelVisual
                {
                    X = x,
                    Left = x - timeText.Length * 2.8,
                    Text = timeText
                });
            }

            if (candles.Count > 1 && (candles.Count - 1) % labelStep != 0)
            {
                var lastIndex = candles.Count - 1;
                TimeLabels.Add(new TimeLabelVisual
                {
                    X = CalculateCenterX(lastIndex, candles.Count, _chartPaddingWidth),
                    Text = candles[lastIndex].Time.ToString(SelectedKLineInterval == "日K" ? "MM/dd" : "MM/dd HH:mm")
                });
                TimeLabels[TimeLabels.Count - 1].Left = TimeLabels[TimeLabels.Count - 1].X - TimeLabels[TimeLabels.Count - 1].Text.Length * 2.8;
            }

            PriceLevels.Clear();
            const int priceLevelCount = 5;
            for (var i = 0; i < priceLevelCount; i++)
            {
                var ratio = i / (double)(priceLevelCount - 1);
                var price = maxPrice - priceRange * ratio;
                PriceLevels.Add(new PriceLevelVisual
                {
                    Y = Scale(price, minPrice, priceRange, CandleChartHeight),
                    LabelTop = Math.Max(4, Math.Min(CandleChartHeight - 28, Scale(price, minPrice, priceRange, CandleChartHeight) - 8)),
                    Text = price.ToString("F2")
                });
            }

            RebuildMacdVisuals(candles);
            RebuildRsiVisuals(candles);
            RebuildVolumeVisuals(candles);
            RebuildMarginBalanceVisuals(candles);
            RebuildThreeMajorVisuals(candles);
        }

        private void RebuildMarginBalanceVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (SelectedKLineInterval != "日K" || sourceCandles == null || sourceCandles.Count == 0)
            {
                MarginBalanceBars.Clear();
                MarginBalanceLevels.Clear();
                _lastDisplayMarginBalanceSeries.Clear();
                MarginCrosshairVisibility = Visibility.Collapsed;
                MarginHoverInfo = null;
                return;
            }

            var values = sourceCandles.Select(candle =>
            {
                TwseMarginRecord record;
                if (_marginByDate.TryGetValue(candle.Time.Date, out record))
                {
                    return new MarginBalancePointVisual
                    {
                        Time = candle.Time,
                        MarginPurchaseSales = record.MarginPurchaseSales,
                        MarginSales = record.MarginSales,
                        MarginRedemption = record.MarginRedemption,
                        MarginBalance = record.MarginBalance,
                        ShortCovering = record.ShortCovering,
                        ShortSales = record.ShortSales,
                        ShortRedemption = record.ShortRedemption,
                        ShortBalance = record.ShortBalance,
                        HasData = true
                    };
                }

                return new MarginBalancePointVisual
                {
                    Time = candle.Time,
                    HasData = false
                };
            }).ToList();

            var maxBalance = Math.Max(1d, values.Max(x => (double)x.MarginBalance));

            MarginBalanceBars.Clear();
            MarginBalanceLevels.Clear();

            const int marginLevelCount = 4;
            for (var i = 0; i < marginLevelCount; i++)
            {
                var ratio = i / (double)(marginLevelCount - 1);
                var value = maxBalance * (1 - ratio);
                var y = VolumeChartHeight - (value / maxBalance * VolumeChartHeight);
                MarginBalanceLevels.Add(new PriceLevelVisual
                {
                    Y = y,
                    LabelTop = Math.Max(2, Math.Min(VolumeChartHeight - 14, y - 7)),
                    Text = value.ToString("N0")
                });
            }

            _lastDisplayMarginBalanceSeries.Clear();
            for (var i = 0; i < values.Count; i++)
            {
                var balance = values[i].MarginBalance;
                var height = balance / maxBalance * VolumeChartHeight;
                MarginBalanceBars.Add(new HistogramBarVisual
                {
                    X = CalculateCenterX(i, values.Count, _chartPaddingWidth) - 4,
                    Top = VolumeChartHeight - height,
                    Height = Math.Max(1, height),
                    Brush = values[i].HasData
                        ? (values[i].MarginPurchaseSales >= 0 ? Brushes.DodgerBlue : Brushes.MediumPurple)
                        : Brushes.DimGray
                });
                _lastDisplayMarginBalanceSeries.Add(values[i]);
            }

            OnPropertyChanged(nameof(LatestMarginBalance));
        }

        private void RebuildThreeMajorVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (SelectedKLineInterval != "日K" || sourceCandles == null || sourceCandles.Count == 0)
            {
                ThreeMajorLevels.Clear();
                ThreeMajorNetPoints = new PointCollection();
                ForeignNetPoints = new PointCollection();
                InvestmentTrustNetPoints = new PointCollection();
                DealerNetPoints = new PointCollection();
                _lastDisplayThreeMajorSeries.Clear();
                ThreeMajorZeroY = 0;
                OnPropertyChanged(nameof(ThreeMajorNetPoints));
                OnPropertyChanged(nameof(ForeignNetPoints));
                OnPropertyChanged(nameof(InvestmentTrustNetPoints));
                OnPropertyChanged(nameof(DealerNetPoints));
                return;
            }

            var values = sourceCandles.Select(c =>
            {
                TwseT86Record record;
                if (_twseByDate.TryGetValue(c.Time.Date, out record))
                {
                    return new ThreeMajorPointData
                    {
                        Time = c.Time,
                        ForeignNet = record.ForeignNet,
                        InvestmentTrustNet = record.InvestmentTrustNet,
                        DealerNet = record.DealerNet,
                        ThreeMajorNet = record.ThreeMajorNet
                    };
                }

                return new ThreeMajorPointData
                {
                    Time = c.Time,
                    ForeignNet = 0,
                    InvestmentTrustNet = 0,
                    DealerNet = 0,
                    ThreeMajorNet = 0
                };
            }).ToList();

            var min = Math.Min(0d, new[]
            {
                values.Min(x => (double)x.ForeignNet),
                values.Min(x => (double)x.InvestmentTrustNet),
                values.Min(x => (double)x.DealerNet)
            }.Min());
            var max = Math.Max(0d, new[]
            {
                values.Max(x => (double)x.ForeignNet),
                values.Max(x => (double)x.InvestmentTrustNet),
                values.Max(x => (double)x.DealerNet)
            }.Max());
            var range = Math.Max(1d, max - min);

            ThreeMajorLevels.Clear();
            const int levelCount = 5;
            for (var i = 0; i < levelCount; i++)
            {
                var ratio = i / (double)(levelCount - 1);
                var value = max - range * ratio;
                var y = Scale(value, min, range, ThreeMajorChartHeight);
                ThreeMajorLevels.Add(new PriceLevelVisual
                {
                    Y = y,
                    LabelTop = Math.Max(2, Math.Min(ThreeMajorChartHeight - 14, y - 7)),
                    Text = value.ToString("N0")
                });
            }

            var totalPoints = new PointCollection();
            var foreignPoints = new PointCollection();
            var trustPoints = new PointCollection();
            var dealerPoints = new PointCollection();
            _lastDisplayThreeMajorSeries.Clear();
            for (var i = 0; i < values.Count; i++)
            {
                var x = CalculateCenterX(i, values.Count, _chartPaddingWidth);
                foreignPoints.Add(new Point(x, Scale(values[i].ForeignNet, min, range, ThreeMajorChartHeight)));
                trustPoints.Add(new Point(x, Scale(values[i].InvestmentTrustNet, min, range, ThreeMajorChartHeight)));
                dealerPoints.Add(new Point(x, Scale(values[i].DealerNet, min, range, ThreeMajorChartHeight)));
                totalPoints.Add(new Point(x, Scale(values[i].ThreeMajorNet, min, range, ThreeMajorChartHeight)));
                _lastDisplayThreeMajorSeries.Add(values[i]);
            }

            ThreeMajorNetPoints = totalPoints;
            ForeignNetPoints = foreignPoints;
            InvestmentTrustNetPoints = trustPoints;
            DealerNetPoints = dealerPoints;
            ThreeMajorZeroY = Scale(0d, min, range, ThreeMajorChartHeight);
            OnPropertyChanged(nameof(ThreeMajorNetPoints));
            OnPropertyChanged(nameof(ForeignNetPoints));
            OnPropertyChanged(nameof(InvestmentTrustNetPoints));
            OnPropertyChanged(nameof(DealerNetPoints));
        }

        public void UpdateThreeMajorCrosshair(double x)
        {
            if (_lastDisplayThreeMajorSeries.Count == 0)
            {
                ThreeMajorCrosshairVisibility = Visibility.Collapsed;
                ThreeMajorHoverInfo = null;
                return;
            }

            var clampedX = Math.Max(0, Math.Min(_chartPaddingWidth, x));
            var nearestIndex = GetNearestCandleIndex(clampedX, _lastDisplayThreeMajorSeries.Count, _chartPaddingWidth);
            var item = _lastDisplayThreeMajorSeries[nearestIndex];
            ThreeMajorCrosshairX = CalculateCenterX(nearestIndex, _lastDisplayThreeMajorSeries.Count, _chartPaddingWidth);
            ThreeMajorCrosshairVisibility = Visibility.Visible;
            ThreeMajorHoverInfo =
                $"日期: {item.Time:yyyy/MM/dd}" +
                $"\n外資: {item.ForeignNet:N0}" +
                $"\n投信: {item.InvestmentTrustNet:N0}" +
                $"\n自營商: {item.DealerNet:N0}" +
                $"\n合計: {item.ThreeMajorNet:N0}";
        }

        public void UpdateMarginCrosshair(double x)
        {
            if (_lastDisplayMarginBalanceSeries.Count == 0)
            {
                MarginCrosshairVisibility = Visibility.Collapsed;
                MarginHoverInfo = null;
                return;
            }

            var clampedX = Math.Max(0, Math.Min(_chartPaddingWidth, x));
            var nearestIndex = GetNearestCandleIndex(clampedX, _lastDisplayMarginBalanceSeries.Count, _chartPaddingWidth);
            var item = _lastDisplayMarginBalanceSeries[nearestIndex];
            MarginCrosshairX = CalculateCenterX(nearestIndex, _lastDisplayMarginBalanceSeries.Count, _chartPaddingWidth);
            MarginCrosshairVisibility = Visibility.Visible;

            if (!item.HasData)
            {
                MarginHoverInfo = $"日期: {item.Time:yyyy/MM/dd}\n融資資料: 無";
                return;
            }

            MarginHoverInfo =
                $"日期: {item.Time:yyyy/MM/dd}" +
                $"\n融資餘額: {item.MarginBalance:N0}" +
                $"\n融資增減: {item.MarginPurchaseSales:N0}" +
                $"\n融資賣出: {item.MarginSales:N0}" +
                $"\n融資現金償還: {item.MarginRedemption:N0}" +
                $"\n融券買進: {item.ShortCovering:N0}" +
                $"\n融券賣出: {item.ShortSales:N0}" +
                $"\n融券現券償還: {item.ShortRedemption:N0}" +
                $"\n融券餘額: {item.ShortBalance:N0}";
        }

        public void ClearMarginCrosshair()
        {
            MarginCrosshairVisibility = Visibility.Collapsed;
            MarginHoverInfo = null;
        }

        public void ClearThreeMajorCrosshair()
        {
            ThreeMajorCrosshairVisibility = Visibility.Collapsed;
            ThreeMajorHoverInfo = null;
        }

        private TwseT86History BuildTwseHistorySnapshot()
        {
            return new TwseT86History
            {
                Symbol = Symbol,
                Name = Name,
                RecordsByDate = new Dictionary<DateTime, TwseT86Record>(_twseByDate)
            };
        }

        private void RebuildMacdVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (sourceCandles == null || sourceCandles.Count == 0)
            {
                MacdHistogram.Clear();
                MacdLevels.Clear();
                MacdLinePoints = new PointCollection();
                SignalLinePoints = new PointCollection();
                OnPropertyChanged(nameof(MacdLinePoints));
                OnPropertyChanged(nameof(SignalLinePoints));
                return;
            }

            _macdSeries.Clear();
            _macdSeries.AddRange(sourceCandles.Select(x => x.MACD));
            _signalSeries.Clear();
            _signalSeries.AddRange(sourceCandles.Select(x => x.MacdSignal));
            MACD = sourceCandles[sourceCandles.Count - 1].MACD;
            OnPropertyChanged(nameof(MacdSignal));
            OnPropertyChanged(nameof(MacdHistogramValue));

            var histSeries = _macdSeries.Zip(_signalSeries, (m, s) => m - s).ToList();
            var min = new[] { _macdSeries.Min(), _signalSeries.Min(), histSeries.Min(), 0d }.Min();
            var max = new[] { _macdSeries.Max(), _signalSeries.Max(), histSeries.Max(), 0d }.Max();
            var range = Math.Max(0.01, max - min);

            MacdLevels.Clear();
            const int macdLevelCount = 5;
            for (var i = 0; i < macdLevelCount; i++)
            {
                var ratio = i / (double)(macdLevelCount - 1);
                var value = max - range * ratio;
                MacdLevels.Add(new PriceLevelVisual
                {
                    Y = Scale(value, min, range, MacdChartHeight),
                    LabelTop = Math.Max(2, Math.Min(MacdChartHeight - 14, Scale(value, min, range, MacdChartHeight) - 7)),
                    Text = value.ToString("F3")
                });
            }

            var macdPoints = new PointCollection();
            var signalPoints = new PointCollection();
            MacdHistogram.Clear();

            for (var i = 0; i < _macdSeries.Count; i++)
            {
                var centerX = CalculateCenterX(i, _macdSeries.Count, _chartPaddingWidth);
                var x = centerX - 4;
                var macdY = Scale(_macdSeries[i], min, range, MacdChartHeight);
                var signalY = Scale(_signalSeries[i], min, range, MacdChartHeight);
                var zeroY = Scale(0d, min, range, MacdChartHeight);

                macdPoints.Add(new Point(centerX, macdY));
                signalPoints.Add(new Point(centerX, signalY));

                var hist = _macdSeries[i] - _signalSeries[i];
                var histY = Scale(hist, min, range, MacdChartHeight);

                MacdHistogram.Add(new HistogramBarVisual
                {
                    X = x,
                    Top = Math.Min(zeroY, histY),
                    Height = Math.Max(1, Math.Abs(zeroY - histY)),
                    Brush = hist >= 0 ? Brushes.IndianRed : Brushes.SeaGreen
                });
            }

            MacdLinePoints = macdPoints;
            SignalLinePoints = signalPoints;
            MacdZeroY = Scale(0d, min, range, MacdChartHeight);
            OnPropertyChanged(nameof(MacdLinePoints));
            OnPropertyChanged(nameof(SignalLinePoints));
        }

        private void RebuildRsiVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (sourceCandles == null || sourceCandles.Count == 0)
            {
                RsiLevels.Clear();
                RsiLinePoints = new PointCollection();
                OnPropertyChanged(nameof(RsiLinePoints));
                return;
            }

            var rsiPoints = new PointCollection();

            RsiLevels.Clear();
            foreach (var level in new[] { 100d, 70d, 50d, 30d, 0d })
            {
                RsiLevels.Add(new PriceLevelVisual
                {
                    Y = RsiChartHeight - (level / 100.0 * RsiChartHeight),
                    LabelTop = Math.Max(2, Math.Min(RsiChartHeight - 14, (RsiChartHeight - (level / 100.0 * RsiChartHeight)) - 7)),
                    Text = level.ToString("F0")
                });
            }

            for (var i = 0; i < sourceCandles.Count; i++)
            {
                var rsi = sourceCandles[i].RSI;
                var y = RsiChartHeight - (rsi / 100.0 * RsiChartHeight);
                rsiPoints.Add(new Point(CalculateCenterX(i, sourceCandles.Count, _chartPaddingWidth), y));
            }

            RSI = sourceCandles[sourceCandles.Count - 1].RSI;
            RsiLinePoints = rsiPoints;
            OnPropertyChanged(nameof(RsiLinePoints));
        }

        private void RebuildVolumeVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (sourceCandles == null || sourceCandles.Count == 0)
            {
                VolumeBars.Clear();
                VolumeLevels.Clear();
                return;
            }

            var maxVolume = Math.Max(1, sourceCandles.Max(x => (double)x.Volume));
            VolumeBars.Clear();
            VolumeLevels.Clear();

            const int volumeLevelCount = 4;
            for (var i = 0; i < volumeLevelCount; i++)
            {
                var ratio = i / (double)(volumeLevelCount - 1);
                var value = maxVolume * (1 - ratio);
                var y = VolumeChartHeight - (value / maxVolume * VolumeChartHeight);
                VolumeLevels.Add(new PriceLevelVisual
                {
                    Y = y,
                    LabelTop = Math.Max(2, Math.Min(VolumeChartHeight - 14, y - 7)),
                    Text = value.ToString("N0")
                });
            }

            for (var i = 0; i < sourceCandles.Count; i++)
            {
                var volume = sourceCandles[i].Volume;
                var height = volume / maxVolume * VolumeChartHeight;
                VolumeBars.Add(new HistogramBarVisual
                {
                    X = CalculateCenterX(i, sourceCandles.Count, _chartPaddingWidth) - 4,
                    Top = VolumeChartHeight - height,
                    Height = Math.Max(1, height),
                    Brush = Brushes.SteelBlue
                });
            }
        }

        private List<CandleData> GetDisplayCandles()
        {
            if (_candles.Count == 0)
            {
                return new List<CandleData>();
            }

            return _candles.Skip(Math.Max(0, _candles.Count - _maxDisplayPoints)).ToList();
        }

        public List<CandleData> GetPublicCandles()
        {
            return _candles.ToList();
        }

        private static double CalculateRsiAt(int index, int period, IReadOnlyList<double> closes)
        {
            if (index < 1)
            {
                return 50;
            }

            var start = Math.Max(1, index - period + 1);
            double gains = 0;
            double losses = 0;
            for (var i = start; i <= index; i++)
            {
                var diff = closes[i] - closes[i - 1];
                if (diff >= 0)
                {
                    gains += diff;
                }
                else
                {
                    losses -= diff;
                }
            }

            if (losses == 0)
            {
                return 100;
            }

            var rs = gains / losses;
            return 100 - (100 / (1 + rs));
        }

        private static double Scale(double value, double min, double range, double height)
        {
            return height - ((value - min) / range * (height - 10)) - 5;
        }

        private static double CalculateChartWidth(int count)
        {
            return Math.Max(500, count * 12 + 30);
        }

        private static double CalculateCenterX(int index, int count, double chartWidth)
        {
            if (count <= 1)
            {
                return chartWidth / 2;
            }

            var usableWidth = Math.Max(1, chartWidth - 60);
            return 50 + index * (usableWidth / (count - 1));
        }

        private static int GetNearestCandleIndex(double x, int count, double chartWidth)
        {
            if (count <= 1)
            {
                return 0;
            }

            var usableWidth = Math.Max(1, chartWidth - 60);
            var ratio = Math.Max(0, Math.Min(1, (x - 50) / usableWidth));
            return (int)Math.Round(ratio * (count - 1));
        }

        private static DateTime AlignTimeToInterval(DateTime time, string interval)
        {
            switch (interval)
            {
                case "5分K":
                    {
                        var minute = (time.Minute / 5) * 5;
                        return new DateTime(time.Year, time.Month, time.Day, time.Hour, minute, 0);
                    }
                case "3分K":
                    {
                        var minute = (time.Minute / 3) * 3;
                        return new DateTime(time.Year, time.Month, time.Day, time.Hour, minute, 0);
                    }
                case "日K":
                    return time.Date;
                default:
                    return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0);
            }
        }

        private class SignalMarkerData
        {
            public int Index { get; set; }
            public DateTime Time { get; set; }
            public decimal Price { get; set; }
            public string Signal { get; set; }
            public int Score { get; set; }
        }

        private class ThreeMajorPointData
        {
            public DateTime Time { get; set; }
            public long ForeignNet { get; set; }
            public long InvestmentTrustNet { get; set; }
            public long DealerNet { get; set; }
            public long ThreeMajorNet { get; set; }
        }
    }
}
