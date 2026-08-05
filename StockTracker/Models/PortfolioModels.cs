using System;

namespace StockTracker.Models
{
    public enum TradeSide
    {
        Buy,
        Sell
    }

    public class TradeTransaction
    {
        public long Id { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public DateTime TradeTime { get; set; }
        public TradeSide Side { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Fee { get; set; }
        public string Note { get; set; }
    }

    public class OpenPosition
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public decimal Quantity { get; set; }
        public decimal AverageCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal RealizedProfitLoss { get; set; }
    }

    public class RiskRecommendation
    {
        public decimal StopLossPrice { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public decimal TrailingStopPrice { get; set; }
        public string Summary { get; set; }
        public string Action { get; set; }
    }
}
