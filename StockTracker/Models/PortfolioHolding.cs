namespace StockTracker.Models
{
    public class PortfolioHolding
    {
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal AverageCost { get; set; }

        // Quantity and cost of the cash (non-margin) portion.  Quantity and
        // AverageCost remain the aggregate values so existing portfolio files
        // and views stay compatible.
        public int CashQuantity { get; set; }
        public decimal CashAverageCost { get; set; }
    }

    public class PortfolioCashFlow
    {
        // 入金為正數；出金為負數。
        public System.DateTime Date { get; set; } = System.DateTime.Today;
        public decimal Amount { get; set; }
    }

    public class PortfolioTrade
    {
        public System.DateTime Date { get; set; } = System.DateTime.Today;
        public string Type { get; set; } = "Buy";
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Fee { get; set; }
        public decimal Tax { get; set; }
        public decimal CostBasisPerShare { get; set; }
        public decimal RealizedProfitLoss { get; set; }
        public string Note { get; set; }
        public bool IsMargin { get; set; }
        public decimal MarginRatio { get; set; }
        public decimal MarginPrincipal { get; set; }
        public decimal MarginInterestPaid { get; set; }
        // New margin trades store their actual cash movement.  Older trades
        // leave this as zero and are calculated using their original fields.
        public decimal CashImpact { get; set; }
    }

    public class PortfolioMarginLot
    {
        public System.Guid Id { get; set; } = System.Guid.NewGuid();
        public System.DateTime OpenDate { get; set; } = System.DateTime.Today;
        public string Symbol { get; set; }
        public int OriginalQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal CostBasisPerShare { get; set; }
        public decimal OutstandingPrincipal { get; set; }
        public decimal MarginRatio { get; set; } = 0.60m;
        public decimal AnnualInterestRate { get; set; }
    }

    public class PortfolioRealizedAdjustment
    {
        public System.DateTime Date { get; set; } = System.DateTime.Today;
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class PortfolioSettings
    {
        public decimal Cash { get; set; }
        public double CashReservePercentage { get; set; } = 15;
        public double SinglePositionLimitPercentage { get; set; } = 10;
        public System.Collections.Generic.List<PortfolioHolding> Holdings { get; set; } = new System.Collections.Generic.List<PortfolioHolding>();
        public System.Collections.Generic.List<PortfolioCashFlow> CashFlows { get; set; } = new System.Collections.Generic.List<PortfolioCashFlow>();
        public System.Collections.Generic.List<PortfolioTrade> Trades { get; set; } = new System.Collections.Generic.List<PortfolioTrade>();
        public System.Collections.Generic.List<PortfolioRealizedAdjustment> RealizedAdjustments { get; set; } = new System.Collections.Generic.List<PortfolioRealizedAdjustment>();
        public System.Collections.Generic.List<PortfolioMarginLot> MarginLots { get; set; } = new System.Collections.Generic.List<PortfolioMarginLot>();
    }
}
