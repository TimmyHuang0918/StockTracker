using System.Collections.Generic;

namespace StockTracker.Models
{
    /// <summary>
    /// A locally maintained classification record. It contains no market data;
    /// price and volume remain the responsibility of the Capital API.
    /// </summary>
    public sealed class StockGroupEntry
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Industry { get; set; }
        public List<string> Themes { get; set; } = new List<string>();
        public bool Enabled { get; set; } = true;
        public string Source { get; set; }
    }
}
