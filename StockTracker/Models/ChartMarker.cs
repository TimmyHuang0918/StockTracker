using System;

namespace StockTracker.Models
{
    public class ChartMarker
    {
        public DateTime Time { get; set; }
        public string Text { get; set; }
        public double Price { get; set; }
        public string ColorHex { get; set; }
        public string MarkerType { get; set; }
    }
}
