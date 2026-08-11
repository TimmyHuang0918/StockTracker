namespace StockTracker.Models
{
    public class PortfolioHolding
    {
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal AverageCost { get; set; }
    }

    public class PortfolioSettings
    {
        public decimal Cash { get; set; }
        public double CashReservePercentage { get; set; } = 15;
        public double SinglePositionLimitPercentage { get; set; } = 10;
        public System.Collections.Generic.List<PortfolioHolding> Holdings { get; set; } = new System.Collections.Generic.List<PortfolioHolding>();
    }
}
