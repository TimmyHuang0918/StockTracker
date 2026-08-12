namespace StockTracker.Models
{
    public class PortfolioHolding
    {
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal AverageCost { get; set; }
    }

    public class PortfolioCashFlow
    {
        // 入金為正數；出金為負數。
        public System.DateTime Date { get; set; } = System.DateTime.Today;
        public decimal Amount { get; set; }
    }

    public class PortfolioSettings
    {
        public decimal Cash { get; set; }
        public double CashReservePercentage { get; set; } = 15;
        public double SinglePositionLimitPercentage { get; set; } = 10;
        public System.Collections.Generic.List<PortfolioHolding> Holdings { get; set; } = new System.Collections.Generic.List<PortfolioHolding>();
        public System.Collections.Generic.List<PortfolioCashFlow> CashFlows { get; set; } = new System.Collections.Generic.List<PortfolioCashFlow>();
    }
}
