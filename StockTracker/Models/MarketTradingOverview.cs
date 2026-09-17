using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace StockTracker.Models
{
    public sealed class MarketTradingOverview
    {
        public DateTime RetrievedAt { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<MarketTradingSeries> Markets { get; set; } = new List<MarketTradingSeries>
        {
            new MarketTradingSeries { Code = "TWSE", Name = "上市 · 加權指數", Status = "完成新版掃描後載入。", SourceUrl = "https://www.twse.com.tw/zh/trading/historical/fmtqik.html" },
            new MarketTradingSeries { Code = "TPEX", Name = "上櫃 · 櫃買指數", Status = "完成新版掃描後載入。", SourceUrl = "https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_index/st41_result.php?l=zh-tw&o=json" }
        };
        public string UpdatedText => RetrievedAt == DateTime.MinValue ? "成交資料待更新" : $"成交資料擷取：{RetrievedAt:yyyy/MM/dd HH:mm}（台北時間）";
    }

    public sealed class MarketTradingSeries
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public string SourceUrl { get; set; }
        public string Status { get; set; }
        public List<MarketTradingDay> Days { get; set; } = new List<MarketTradingDay>();
        [JsonIgnore] public MarketTradingDay Latest => Days.OrderByDescending(x => x.TradeDate).FirstOrDefault();
        [JsonIgnore] public string DateText => Latest == null ? "資料待更新" : Latest.DateText + " · " + Latest.SessionText;
        [JsonIgnore] public string IndexText => Latest?.IndexText ?? "—";
        [JsonIgnore] public string ChangeText => Latest?.ChangeText ?? "—";
        [JsonIgnore] public string AmountText => Latest?.AmountText ?? "—";
        [JsonIgnore] public string VolumeText => Latest?.VolumeText ?? "—";
        [JsonIgnore] public string AmountChangeText => Latest?.AmountChangeText ?? "—";
        [JsonIgnore] public string AmountRatioText => Latest?.AmountRatioText ?? "—";
        [JsonIgnore] public string Reading => Latest?.Reading ?? "成交資料待更新";
    }

    public sealed class MarketTradingDay
    {
        public DateTime TradeDate { get; set; }
        public decimal IndexClose { get; set; }
        public decimal IndexChange { get; set; }
        public decimal VolumeShares { get; set; }
        public decimal TurnoverNtd { get; set; }
        public decimal? TurnoverChangePercent { get; set; }
        public decimal? TurnoverRatio20 { get; set; }
        public bool IsComplete { get; set; }
        public string SessionText { get; set; }
        public string DateText => TradeDate.ToString("yyyy/MM/dd");
        public decimal? IndexChangePercent => IndexClose - IndexChange > 0 ? (decimal?)(IndexChange / (IndexClose - IndexChange) * 100m) : null;
        public string IndexText => IndexClose.ToString("N2", CultureInfo.InvariantCulture);
        public string ChangeText => Percent(IndexChangePercent);
        public string AmountText => (TurnoverNtd / 100000000m).ToString("N1", CultureInfo.InvariantCulture) + " 億";
        public string VolumeText => (VolumeShares / 10000000m).ToString("N2", CultureInfo.InvariantCulture) + " 萬張";
        public string AmountChangeText => Percent(TurnoverChangePercent);
        public string AmountRatioText => TurnoverRatio20.HasValue ? TurnoverRatio20.Value.ToString("F2", CultureInfo.InvariantCulture) + " 倍" : "—";
        public string Reading
        {
            get
            {
                if (!IsComplete) return "當日累計資料更新中，暫不與完整交易日比較。";
                var direction = IndexChange > 0 ? "指數上漲" : IndexChange < 0 ? "指數下跌" : "指數持平";
                var change = !TurnoverChangePercent.HasValue ? "前一交易日成交資料不足" : TurnoverChangePercent > 0 ? "成交較前一交易日增加" : TurnoverChangePercent < 0 ? "成交較前一交易日減少" : "成交與前一交易日相同";
                var baseline = !TurnoverRatio20.HasValue ? "前 20 日基準待補齊" : TurnoverRatio20 >= 1m ? "達到或高於前 20 日平均" : "低於前 20 日平均";
                return direction + "，" + change + "；" + baseline + "。";
            }
        }
        public static string Percent(decimal? value) => value.HasValue ? value.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "—";
    }
}
