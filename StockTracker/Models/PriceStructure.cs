using System.Collections.Generic;

namespace StockTracker.Models
{
    /// <summary>A price area formed by nearby historical swing levels, not a single exact price.</summary>
    public sealed class PriceStructureZone
    {
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public int Strength { get; set; }
        public int Touches { get; set; }
        public string Basis { get; set; }
        public decimal Mid => (Low + High) / 2m;
    }

    /// <summary>Daily-chart support and resistance areas used by manual trade plans.</summary>
    public sealed class PriceStructureAnalysis
    {
        public bool HasSufficientData { get; set; }
        public decimal LatestPrice { get; set; }
        public decimal Atr14 { get; set; }
        public List<PriceStructureZone> Supports { get; set; } = new List<PriceStructureZone>();
        public List<PriceStructureZone> Resistances { get; set; } = new List<PriceStructureZone>();
        public string Message { get; set; }
    }
}
