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
        private const double CandleChartHeight = 220;
        private const double MacdChartHeight = 120;
        private const double RsiChartHeight = 90;
        private const double VolumeChartHeight = 100;
        private const int MaxDisplayPoints = 60;

        private readonly List<CandleData> _candles = new List<CandleData>();
        private readonly List<double> _macdSeries = new List<double>();
        private readonly List<double> _signalSeries = new List<double>();
        private readonly List<SignalMarkerData> _signalHistory = new List<SignalMarkerData>();

        private decimal _latestPrice;
        private decimal _changePercent;
        private double _ma5;
        private double _ma20;
        private double _macd;
        private double _rsi;
        private double _latestPriceY;
        private double _macdZeroY;
        private string _selectedKLineInterval = "1分K";
        private string _signal = "中立";
        private string _lastNotifiedSignal = string.Empty;

        public StockViewModel(string symbol, string name)
        {
            Symbol = symbol;
            Name = name;

            Candles = new ObservableCollection<CandlestickVisual>();
            MacdHistogram = new ObservableCollection<HistogramBarVisual>();
            VolumeBars = new ObservableCollection<HistogramBarVisual>();
            SignalMarkers = new ObservableCollection<SignalMarkerVisual>();
            TimeLabels = new ObservableCollection<TimeLabelVisual>();

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
                _latestPrice = value;
                OnPropertyChanged();
            }
        }

        public decimal ChangePercent
        {
            get => _changePercent;
            private set
            {
                _changePercent = value;
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
                OnPropertyChanged(nameof(ChartWidth));
                RebuildVisuals();
            }
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

        public event Action<StockViewModel, string> SignalTriggered;

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
            var first = _candles.FirstOrDefault()?.Open ?? LatestPrice;
            ChangePercent = first == 0 ? 0 : Math.Round((LatestPrice - first) / first * 100, 2);

            MA5 = CalculateMa(5);
            MA20 = CalculateMa(20);
            CalculateMacd();
            RSI = CalculateRsi(14);
            UpdateSignal();
            RebuildVisuals();
            OnPropertyChanged(nameof(LatestVolume));
        }

	public void ClearData()
	{
	    _candles.Clear();
	    _signalHistory.Clear();
	    _lastNotifiedSignal = string.Empty;
	    LatestPrice = 0;
	    ChangePercent = 0;
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
	    OnPropertyChanged(nameof(LatestVolume));
	}

        private double CalculateMa(int period)
        {
            var analysisCandles = GetDisplayCandles();
            if (analysisCandles.Count == 0)
            {
                return 0;
            }

            var count = Math.Min(period, analysisCandles.Count);
            return analysisCandles.Skip(analysisCandles.Count - count).Average(x => (double)x.Close);
        }

        private void CalculateMacd()
        {
            var closes = GetDisplayCandles().Select(x => (double)x.Close).ToList();
            if (!closes.Any())
            {
                MACD = 0;
                return;
            }

            var ema12 = CalculateEmaSeries(closes, 12);
            var ema26 = CalculateEmaSeries(closes, 26);

            _macdSeries.Clear();
            for (var i = 0; i < closes.Count; i++)
            {
                _macdSeries.Add(ema12[i] - ema26[i]);
            }

            var signal = CalculateEmaSeries(_macdSeries, 9);
            _signalSeries.Clear();
            _signalSeries.AddRange(signal);

            MACD = _macdSeries.LastOrDefault();
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

        private double CalculateRsi(int period)
        {
            var analysisCandles = GetDisplayCandles();
            if (analysisCandles.Count < 2)
            {
                return 50;
            }

            var closes = analysisCandles.Select(x => (double)x.Close).ToList();
            var gains = 0.0;
            var losses = 0.0;
            var start = Math.Max(1, closes.Count - period + 1);

            for (var i = start; i < closes.Count; i++)
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

        private void UpdateSignal()
        {
            var analysisCandles = GetDisplayCandles();
            if (analysisCandles.Count < 21)
            {
                Signal = "資料不足";
                return;
            }

            var previousMa5 = analysisCandles.Skip(analysisCandles.Count - 6).Take(5).Average(x => (double)x.Close);
            var previousMa20 = analysisCandles.Skip(analysisCandles.Count - 21).Take(20).Average(x => (double)x.Close);

            if (previousMa5 <= previousMa20 && MA5 > MA20)
            {
                Signal = "買進訊號";
            }
            else if (previousMa5 >= previousMa20 && MA5 < MA20)
            {
                Signal = "賣出訊號";
            }
            else
            {
                Signal = "中立";
            }

            if ((Signal == "買進訊號" || Signal == "賣出訊號") && Signal != _lastNotifiedSignal)
            {
                _lastNotifiedSignal = Signal;
                _signalHistory.Add(new SignalMarkerData
                {
                    Index = _candles.Count - 1,
                    Price = analysisCandles.Last().Close,
                    Signal = Signal
                });
                SignalTriggered?.Invoke(this, Signal);
            }
        }

        private void RebuildVisuals()
        {
            var displayOffset = Math.Max(0, _candles.Count - MaxDisplayPoints);
            var candles = _candles.Skip(displayOffset).ToList();
            if (!candles.Any())
            {
                return;
            }

            var minPrice = (double)candles.Min(x => x.Low);
            var maxPrice = (double)candles.Max(x => x.High);
            var priceRange = Math.Max(0.01, maxPrice - minPrice);

            Candles.Clear();
            var ma5Points = new PointCollection();
            var ma20Points = new PointCollection();

            for (var i = 0; i < candles.Count; i++)
            {
                var item = candles[i];
                var centerX = CalculateCenterX(i, candles.Count, ChartWidth);
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
                    var ma = candles.Skip(i - 4).Take(5).Average(c => (double)c.Close);
                    ma5Points.Add(new Point(centerX, Scale(ma, minPrice, priceRange, CandleChartHeight)));
                }

                if (i >= 19)
                {
                    var ma = candles.Skip(i - 19).Take(20).Average(c => (double)c.Close);
                    ma20Points.Add(new Point(centerX, Scale(ma, minPrice, priceRange, CandleChartHeight)));
                }
            }

            Ma5Points = ma5Points;
            Ma20Points = ma20Points;
            LatestPriceY = Scale((double)LatestPrice, minPrice, priceRange, CandleChartHeight);
            OnPropertyChanged(nameof(Ma5Points));
            OnPropertyChanged(nameof(Ma20Points));
            OnPropertyChanged(nameof(ChartWidth));

            SignalMarkers.Clear();
            foreach (var signal in _signalHistory.Where(x => x.Index >= 0))
            {
                var displayIndex = signal.Index - displayOffset;
                if (displayIndex < 0 || displayIndex >= candles.Count)
                {
                    continue;
                }

                var x = CalculateCenterX(displayIndex, candles.Count, ChartWidth);
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
                var x = CalculateCenterX(i, candles.Count, ChartWidth);
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
                    X = CalculateCenterX(lastIndex, candles.Count, ChartWidth),
                    Text = candles[lastIndex].Time.ToString(SelectedKLineInterval == "日K" ? "MM/dd" : "MM/dd HH:mm")
                });
                TimeLabels[TimeLabels.Count - 1].Left = TimeLabels[TimeLabels.Count - 1].X - TimeLabels[TimeLabels.Count - 1].Text.Length * 2.8;
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
                MacdLinePoints = new PointCollection();
                SignalLinePoints = new PointCollection();
                OnPropertyChanged(nameof(MacdLinePoints));
                OnPropertyChanged(nameof(SignalLinePoints));
                return;
            }

            var closes = sourceCandles.Select(x => (double)x.Close).ToList();
            var ema12 = CalculateEmaSeries(closes, 12);
            var ema26 = CalculateEmaSeries(closes, 26);

            _macdSeries.Clear();
            for (var i = 0; i < closes.Count; i++)
            {
                _macdSeries.Add(ema12[i] - ema26[i]);
            }

            _signalSeries.Clear();
            _signalSeries.AddRange(CalculateEmaSeries(_macdSeries, 9));
            MACD = _macdSeries.LastOrDefault();
            OnPropertyChanged(nameof(MacdSignal));
            OnPropertyChanged(nameof(MacdHistogramValue));

            var histSeries = _macdSeries.Zip(_signalSeries, (m, s) => m - s).ToList();
            var min = new[] { _macdSeries.Min(), _signalSeries.Min(), histSeries.Min(), 0d }.Min();
            var max = new[] { _macdSeries.Max(), _signalSeries.Max(), histSeries.Max(), 0d }.Max();
            var range = Math.Max(0.01, max - min);

            var macdPoints = new PointCollection();
            var signalPoints = new PointCollection();
            MacdHistogram.Clear();

            for (var i = 0; i < _macdSeries.Count; i++)
            {
                var centerX = CalculateCenterX(i, _macdSeries.Count, ChartWidth);
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
                RsiLinePoints = new PointCollection();
                OnPropertyChanged(nameof(RsiLinePoints));
                return;
            }

            var rsiPoints = new PointCollection();
            var closes = sourceCandles.Select(x => (double)x.Close).ToList();

            for (var i = 0; i < closes.Count; i++)
            {
                var rsi = CalculateRsiAt(i, 14, closes);
                var y = RsiChartHeight - (rsi / 100.0 * RsiChartHeight);
                rsiPoints.Add(new Point(CalculateCenterX(i, closes.Count, ChartWidth), y));
            }

            RSI = CalculateRsiAt(closes.Count - 1, 14, closes);
            RsiLinePoints = rsiPoints;
            OnPropertyChanged(nameof(RsiLinePoints));
        }

        private void RebuildVolumeVisuals(IReadOnlyList<CandleData> sourceCandles)
        {
            if (sourceCandles == null || sourceCandles.Count == 0)
            {
                VolumeBars.Clear();
                return;
            }

            var maxVolume = Math.Max(1, sourceCandles.Max(x => (double)x.Volume));
            VolumeBars.Clear();

            for (var i = 0; i < sourceCandles.Count; i++)
            {
                var volume = sourceCandles[i].Volume;
                var height = volume / maxVolume * VolumeChartHeight;
                VolumeBars.Add(new HistogramBarVisual
                {
                    X = CalculateCenterX(i, sourceCandles.Count, ChartWidth) - 4,
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

            return _candles.Skip(Math.Max(0, _candles.Count - MaxDisplayPoints)).ToList();
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

            var usableWidth = Math.Max(1, chartWidth - 20);
            return 10 + index * (usableWidth / (count - 1));
        }

        private class SignalMarkerData
        {
            public int Index { get; set; }
            public decimal Price { get; set; }
            public string Signal { get; set; }
        }
    }
}
