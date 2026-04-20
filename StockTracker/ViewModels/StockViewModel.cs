using StockTracker.Models;
using StockManager.Library;
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
        private const int MinDisplayPoints = 20;

        private readonly List<CandleData> _candles = new List<CandleData>();
        private readonly List<StockViewModel> _detailViewModels = new List<StockViewModel>();
        private readonly List<double> _macdSeries = new List<double>();
        private readonly List<double> _signalSeries = new List<double>();
        private readonly List<SignalMarkerData> _signalHistory = new List<SignalMarkerData>();

        private decimal _latestPrice;
        private decimal _changePercent;
        private Brush _latestChangeBrush = Brushes.Gainsboro;
        private double _ma5;
        private double _ma20;
        private double _macd;
        private double _rsi;
        private double _latestPriceY;
        private double _macdZeroY;
        private string _selectedKLineInterval = "1分K";
	private string _selectedKLineCount= "120";
	private string _signal = "中立";
        private string _lastNotifiedSignal = string.Empty;
        private List<string> _latestRecommendationReasons = new List<string>();
        private int _maxDisplayPoints = 60;
        private readonly List<CandleData> _lastDisplayCandles = new List<CandleData>();
        private readonly List<double> _lastDisplayMacdSeries = new List<double>();
        private readonly List<double> _lastDisplaySignalSeries = new List<double>();
        private readonly List<double> _lastDisplayRsiSeries = new List<double>();
        private double _lastMinPrice;
        private double _lastPriceRange = 1;
        private double _crosshairX;
        private double _crosshairY;
        private Visibility _crosshairVisibility = Visibility.Collapsed;
        private string _hoverInfo;
        private double _chartPaddingWidth { get { return ChartWidth - 20; } }

        public StockViewModel(string symbol, string name)
        {
            Symbol = symbol;
            Name = name;

            Candles = new ObservableCollection<CandlestickVisual>();
            MacdHistogram = new ObservableCollection<HistogramBarVisual>();
            VolumeBars = new ObservableCollection<HistogramBarVisual>();
            SignalMarkers = new ObservableCollection<SignalMarkerVisual>();
            TimeLabels = new ObservableCollection<TimeLabelVisual>();
            PriceLevels = new ObservableCollection<PriceLevelVisual>();
            MacdLevels = new ObservableCollection<PriceLevelVisual>();
            RsiLevels = new ObservableCollection<PriceLevelVisual>();
            VolumeLevels = new ObservableCollection<PriceLevelVisual>();

            Ma5Points = new PointCollection();
            Ma20Points = new PointCollection();
            MacdLinePoints = new PointCollection();
            SignalLinePoints = new PointCollection();
            RsiLinePoints = new PointCollection();
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
                _selectedKLineInterval = _selectedKLineInterval
            };

            detailVm.OnPropertyChanged(nameof(SelectedKLineInterval));
            detailVm.OnPropertyChanged(nameof(ChartWidth));

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
        public double MacdSignal => _signalSeries.LastOrDefault();
        public double MacdHistogramValue => (_macdSeries.Any() && _signalSeries.Any()) ? _macdSeries.Last() - _signalSeries.Last() : 0;

        public ObservableCollection<CandlestickVisual> Candles { get; }
        public PointCollection Ma5Points { get; private set; }
        public PointCollection Ma20Points { get; private set; }

        public ObservableCollection<HistogramBarVisual> MacdHistogram { get; }
        public PointCollection MacdLinePoints { get; private set; }
        public PointCollection SignalLinePoints { get; private set; }

        public PointCollection RsiLinePoints { get; private set; }
        public ObservableCollection<HistogramBarVisual> VolumeBars { get; }
        public ObservableCollection<SignalMarkerVisual> SignalMarkers { get; }
        public ObservableCollection<TimeLabelVisual> TimeLabels { get; }
        public ObservableCollection<PriceLevelVisual> PriceLevels { get; }
        public ObservableCollection<PriceLevelVisual> MacdLevels { get; }
        public ObservableCollection<PriceLevelVisual> RsiLevels { get; }
        public ObservableCollection<PriceLevelVisual> VolumeLevels { get; }

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
                $"\n開: {candle.Open:F2} 高: {candle.High:F2} 低: {candle.Low:F2} 收: {candle.Close:F2}" +
                $"\n漲跌幅: {candle.PercentageChange:F2} %" +
		$"\n游標價: {priceAtCursor:F2}" +
                $"\n成交量: {candle.Volume:N0}" +
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
		incoming.Volume = Math.Max(existing.Volume, incoming.Volume);
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

        private void RecalculateIndicatorsOnCandles()
        {
            if (_candles.Count == 0)
            {
                MA5 = 0;
                MA20 = 0;
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
                var ma5Count = i - ma5Start + 1;
                var ma20Count = i - ma20Start + 1;

                _candles[i].MA5 = closes.Skip(ma5Start).Take(ma5Count).Average();
                _candles[i].MA20 = closes.Skip(ma20Start).Take(ma20Count).Average();
                _candles[i].MACD = macdSeries[i];
                _candles[i].MacdSignal = signalSeries[i];
                _candles[i].MacdHistogram = macdSeries[i] - signalSeries[i];
                _candles[i].RSI = CalculateRsiAt(i, 14, closes);
                if(i > 0)
                    _candles[i].PercentageChange = (_candles[i].Close - _candles[i - 1].Close) / _candles[i - 1].Close * 100;
	    }

            var latest = _candles[_candles.Count - 1];
            MA5 = latest.MA5;
            MA20 = latest.MA20;
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
            var analysisCandles = GetDisplayCandles();
            if (!analysisCandles.Any())
            {
                Signal = "資料不足";
                _latestRecommendationReasons.Clear();
                return;
            }

            var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(
                analysisCandles,
                (double)LatestPrice,
                (double?)ChangePercent);

            _latestRecommendationReasons = recommendation.Reasons ?? new List<string>();
            Signal = TradingRecommendationLibrary.GetAdvancedSuggestion(recommendation.Score);

            var actionSignal = ResolveActionSignal(Signal);
            if ((actionSignal == "買進訊號" || actionSignal == "賣出訊號") && actionSignal != _lastNotifiedSignal)
            {
                _lastNotifiedSignal = actionSignal;
                _signalHistory.Add(new SignalMarkerData
                {
                    Index = _candles.Count - 1,
                    Price = analysisCandles.Last().Close,
                    Signal = actionSignal
                });
                SignalTriggered?.Invoke(this, actionSignal);
            }
        }

	private static string ResolveActionSignal(string suggestion)
	{
	    if (string.IsNullOrWhiteSpace(suggestion))
	    {
		return "中立";
	    }

	    if (suggestion.Contains("買入") || suggestion.Contains("偏多"))
	    {
		return "買進訊號";
	    }

	    if (suggestion.Contains("賣出") || suggestion.Contains("偏空"))
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

	    var slice = _lastDisplayCandles.Take(nearestIndex + 1).ToList();
	    double? previousClose = null;
	    if (nearestIndex > 0)
	    {
		previousClose = (double)_lastDisplayCandles[nearestIndex - 1].Close;
	    }

	    double? changePercent = null;
	    if (previousClose.HasValue && previousClose.Value != 0)
	    {
		changePercent = ((double)candle.Close - previousClose.Value) / previousClose.Value * 100d;
	    }

	    var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(slice, (double)candle.Close, changePercent, previousClose);
	    var suggestion = TradingRecommendationLibrary.GetAdvancedSuggestion(recommendation.Score);
	    var topReasons = (recommendation.Reasons ?? new List<string>()).Take(4).Select(r => $"- {r}");
	    return $"\n建議: {suggestion} ({recommendation.Score})\n建議理由:\n" + string.Join("\n", topReasons);
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
                    WickTop = Math.Min(highY, lowY),
                    WickBottom = Math.Max(highY, lowY),
                    BodyTop = Math.Min(openY, closeY),
                    BodyHeight = Math.Max(2, Math.Abs(openY - closeY)),
                    BodyBrush = item.Close >= item.Open ? Brushes.IndianRed : Brushes.SeaGreen
                });

                if (i >= 4)
                {
                    ma5Points.Add(new Point(centerX, Scale(candles[i].MA5, minPrice, priceRange, CandleChartHeight)));
                }

                if (i >= 19)
                {
                    ma20Points.Add(new Point(centerX, Scale(candles[i].MA20, minPrice, priceRange, CandleChartHeight)));
                }
            }

            Ma5Points = ma5Points;
            Ma20Points = ma20Points;
            LatestPriceY = Scale((double)LatestPrice, minPrice, priceRange, CandleChartHeight);
            OnPropertyChanged(nameof(Ma5Points));
            OnPropertyChanged(nameof(Ma20Points));
            OnPropertyChanged(nameof(_chartPaddingWidth));

            SignalMarkers.Clear();
            foreach (var signal in _signalHistory.Where(x => x.Index >= 0))
            {
                var displayIndex = signal.Index - displayOffset;
                if (displayIndex < 0 || displayIndex >= candles.Count)
                {
                    continue;
                }

                var x = CalculateCenterX(displayIndex, candles.Count, _chartPaddingWidth);
                var y = Scale((double)signal.Price, minPrice, priceRange, CandleChartHeight);
                var isBuy = signal.Signal == "買進訊號";
                SignalMarkers.Add(new SignalMarkerVisual
                {
                    X = x,
                    Y = isBuy ? y - 18 : y + 6,
                    Text = isBuy ? "▲買" : "▼賣",
                    Brush = isBuy ? Brushes.OrangeRed : Brushes.LimeGreen
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
            public decimal Price { get; set; }
            public string Signal { get; set; }
        }
    }
}
