using System;
using System.Windows.Media;

namespace StockTracker.Models
{
    public class StockModel
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public DateTime Time { get; set; }
        public long Volume { get; set; }
    }

    public class CandleData
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
	public decimal PercentageChange { get; set; }
	public long Volume { get; set; }
        public double MA5 { get; set; }
        public double MA20 { get; set; }
        public double MACD { get; set; }
        public double MacdSignal { get; set; }
        public double MacdHistogram { get; set; }
        public double RSI { get; set; }
    }

    public class CandlestickVisual
    {
        public double X { get; set; }
        public double WickTop { get; set; }
        public double WickBottom { get; set; }
        public double BodyTop { get; set; }
        public double BodyHeight { get; set; }
        public Brush BodyBrush { get; set; }
    }

    public class HistogramBarVisual
    {
        public double X { get; set; }
        public double Top { get; set; }
        public double Height { get; set; }
        public Brush Brush { get; set; }
    }

    public class SignalMarkerVisual
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Text { get; set; }
        public Brush Brush { get; set; }
    }

    public class TimeLabelVisual
    {
        public double X { get; set; }
        public double Left { get; set; }
        public string Text { get; set; }
    }

    public class PriceLevelVisual
    {
        public double Y { get; set; }
        public double LabelTop { get; set; }
        public string Text { get; set; }
    }
}
