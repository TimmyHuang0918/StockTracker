using System;
using System.Collections.Generic;

namespace StockTracker.Models
{
    public sealed class ThemeStockMetric
    {
        public string Symbol { get; set; }
        public decimal Change1D { get; set; }
        public decimal Change5D { get; set; }
        public decimal VolumeRatio20D { get; set; }
    }

    public sealed class ThemeStatus
    {
        public string Theme { get; set; }
        public DateTime AsOfDate { get; set; }
        public int StockCount { get; set; }
        public int AdvancingCount { get; set; }
        public int DecliningCount { get; set; }
        public decimal AdvanceRatioPercent { get; set; }
        public decimal AverageChange1D { get; set; }
        public decimal AverageChange5D { get; set; }
        public decimal AverageVolumeRatio20D { get; set; }
        public string MarketStatus { get; set; }
        public string Source { get; set; }
        public List<string> LeadingSymbols { get; set; } = new List<string>();
        public List<string> WeakSymbols { get; set; } = new List<string>();
    }
}
