using System;

namespace StockTracker.Models
{
    /// <summary>
    /// A user-authored trading note. It is intentionally disconnected from all
    /// broker and market-order APIs: it can only be saved, copied, and reviewed.
    /// </summary>
    public class TradePlan
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Strategy { get; set; }
        public decimal EntryLower { get; set; }
        public decimal EntryUpper { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TargetOne { get; set; }
        public decimal TargetTwo { get; set; }
        public decimal RiskBudget { get; set; }
        public int SuggestedShares { get; set; }
        public DateTime ValidUntil { get; set; }
        public string CancelCondition { get; set; }
        public string Notes { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
