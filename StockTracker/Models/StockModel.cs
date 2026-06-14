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
        public double MA120 { get; set; }
        public double MA240 { get; set; }
        public double MACD { get; set; }
        public double MacdSignal { get; set; }
        public double MacdHistogram { get; set; }
        public double RSI { get; set; }
        public double BollingerUpper { get; set; }
        public double BollingerMiddle { get; set; }
        public double BollingerLower { get; set; }
    }

    public class CandlestickVisual
    {
        public double X { get; set; }
        public double WickTop { get; set; }
        public double WickBottom { get; set; }
        public double BodyTop { get; set; }
        public double BodyHeight { get; set; }
        public Brush BodyBrush { get; set; }
        public DateTime DateTime { get; set; }
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
        public string TooltipText { get; set; }
        public string PercentageText { get; set; }
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

    public class LineSegmentVisual
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public Brush Brush { get; set; }
    }

    public class MarginBalancePointVisual
    {
        public DateTime Time { get; set; }
        public long MarginPurchaseSales { get; set; }
        public long MarginSales { get; set; }
        public long MarginRedemption { get; set; }
        public long MarginBalance { get; set; }
        public long ShortCovering { get; set; }
        public long ShortSales { get; set; }
        public long ShortRedemption { get; set; }
        public long ShortBalance { get; set; }
        public bool HasData { get; set; }
    }

    public class TwseT86Record
    {
        public string Market { get; set; } // 新增市場欄位
        /// <summary>
        /// 交易日期
        /// </summary>
        public DateTime TradeDate { get; set; }

        /// <summary>
        /// 證券代號
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// 證券名稱
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 外陸資買進股數 (不含外資自營商)
        /// </summary>
        public long ForeignBuy { get; set; }

        /// <summary>
        /// 外陸資賣出股數 (不含外資自營商)
        /// </summary>
        public long ForeignSell { get; set; }

        /// <summary>
        /// 外陸資買賣超股數 (不含外資自營商)
        /// </summary>
        public long ForeignNet { get; set; }

        /// <summary>
        /// 投信買進股數
        /// </summary>
        public long InvestmentTrustBuy { get; set; }

        /// <summary>
        /// 投信賣出股數
        /// </summary>
        public long InvestmentTrustSell { get; set; }

        /// <summary>
        /// 投信買賣超股數
        /// </summary>
        public long InvestmentTrustNet { get; set; }

        /// <summary>
        /// 自營商買賣超股數 (合計：自行買賣 + 避險)
        /// </summary>
        public long DealerNet { get; set; }

        /// <summary>
        /// 自營商買進股數 (自行買賣)
        /// </summary>
        public long DealerSelfBuy { get; set; }

        /// <summary>
        /// 自營商賣出股數 (自行買賣)
        /// </summary>
        public long DealerSelfSell { get; set; }

        /// <summary>
        /// 自營商買賣超股數 (自行買賣)
        /// </summary>
        public long DealerSelfNet { get; set; }

        /// <summary>
        /// 自營商買進股數 (避險)
        /// </summary>
        public long DealerHedgeBuy { get; set; }

        /// <summary>
        /// 自營商賣出股數 (避險)
        /// </summary>
        public long DealerHedgeSell { get; set; }

        /// <summary>
        /// 自營商買賣超股數 (避險)
        /// </summary>
        public long DealerHedgeNet { get; set; }

        /// <summary>
        /// 三大法人買賣超股數 (外資 + 投信 + 自營商合計)
        /// </summary>
        public long ThreeMajorNet { get; set; }
    }

    public class TwseT86History
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<DateTime, TwseT86Record> RecordsByDate { get; set; } = new System.Collections.Generic.Dictionary<DateTime, TwseT86Record>();
    }

    public class TwseMarginRecord
    {
        public string Market { get; set; }

        /// <summary>
        /// 交易日期
        /// </summary>
        public DateTime TradeDate { get; set; }

        /// <summary>
        /// 證券代號
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// 證券名稱
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 融資增減
        /// </summary>
        public long MarginPurchaseSales { get; set; }

        /// <summary>
        /// 融資賣出
        /// </summary>
        public long MarginSales { get; set; }

        /// <summary>
        /// 融資現金償還
        /// </summary>
        public long MarginRedemption { get; set; }

        /// <summary>
        /// 融資餘額
        /// </summary>
        public long MarginBalance { get; set; }

        /// <summary>
        /// 融券買進
        /// </summary>
        public long ShortCovering { get; set; }

        /// <summary>
        /// 融券賣出
        /// </summary>
        public long ShortSales { get; set; }

        /// <summary>
        /// 融券現券償還
        /// </summary>
        public long ShortRedemption { get; set; }

        /// <summary>
        /// 融券餘額
        /// </summary>
        public long ShortBalance { get; set; }
    }

    public class TwseMarginHistory
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<DateTime, TwseMarginRecord> RecordsByDate { get; set; } = new System.Collections.Generic.Dictionary<DateTime, TwseMarginRecord>();
    }

    public class DailyCloseRecord
    {
        public DateTime TradeDate { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public double Close { get; set; }
    }

    public class DailyCloseHistory
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<DateTime, DailyCloseRecord> RecordsByDate { get; set; } = new System.Collections.Generic.Dictionary<DateTime, DailyCloseRecord>();
    }

    public class TwseMarginMetricResult
    {
        public TwseMarginRecord Record { get; set; }
        public double Close { get; set; }
        public double TotalLoan { get; set; }
        public double MarginMaintenanceRatio { get; set; }
        public double MarginAverageCost { get; set; }
    }

    public class TwseMarginMetricHistory
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<DateTime, TwseMarginMetricResult> RecordsByDate { get; set; } = new System.Collections.Generic.Dictionary<DateTime, TwseMarginMetricResult>();
    }
}
