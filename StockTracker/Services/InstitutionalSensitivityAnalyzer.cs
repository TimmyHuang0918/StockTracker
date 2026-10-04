using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Services
{
    /// <summary>
    /// Measures whether a stock has historically continued in the direction of
    /// a foreign-investor or investment-trust flow.  All measurements use data
    /// available at that day's close and the following two trading days.
    /// </summary>
    public static class InstitutionalSensitivityAnalyzer
    {
        private const int LookbackDays = 60;
        private const int ForwardDays = 2;
        private const decimal MinimumFlowRatio = 0.01m;
        private const decimal MinimumSignalAmount = 3000000m;
        private const decimal MinimumAverageVolumeLots = 100m;
        private const decimal MinimumAverageTurnoverAmount = 30000000m;
        private const int MinimumSignalDays = 20;
        private const int MinimumDirectionalSignalDays = 6;
        private const int MinimumSensitiveScore = 60;
        private const int MinimumSensitiveConfidence = 60;

        private sealed class Observation
        {
            public decimal FlowRatio { get; set; }
            public decimal ForwardReturn { get; set; }
        }

        public static InstitutionalSensitivityResult Analyze(IEnumerable<CandleData> candles, TwseT86History history)
        {
            var dailyCandles = (candles ?? Enumerable.Empty<CandleData>())
                .Where(candle => candle != null && candle.Close > 0 && candle.Volume > 0)
                .GroupBy(candle => candle.Time.Date)
                .Select(group => group.OrderBy(candle => candle.Time).Last())
                .OrderBy(candle => candle.Time.Date)
                .ToList();
            var records = history?.RecordsByDate ?? new Dictionary<DateTime, TwseT86Record>();
            var liquidityWindow = dailyCandles
                .Skip(Math.Max(0, dailyCandles.Count - 20))
                .ToList();

            var result = new InstitutionalSensitivityResult
            {
                AverageVolumeLots = liquidityWindow.Count == 0 ? 0m : liquidityWindow.Average(candle => (decimal)candle.Volume),
                AverageTurnoverAmount = liquidityWindow.Count == 0
                    ? 0m
                    : liquidityWindow.Average(candle => candle.Close * (decimal)candle.Volume * 1000m)
            };

            // CandleData.Volume is normalized to lots by the daily-price import.
            // T86 institutional flows are raw shares, so normalize them to lots here.
            if (result.AverageVolumeLots < MinimumAverageVolumeLots ||
                result.AverageTurnoverAmount < MinimumAverageTurnoverAmount)
            {
                result.LeadershipLabel = "流動性不足";
                result.Summary = $"近 20 日平均成交量 {result.AverageVolumeLots:N0} 張、平均成交額 {FormatAmount(result.AverageTurnoverAmount)}；未達法人敏感度分析的流動性門檻。";
                return result;
            }

            result.Foreign = Calculate(dailyCandles, records, record => record.ForeignNet);
            result.InvestmentTrust = Calculate(dailyCandles, records, record => record.InvestmentTrustNet);

            var foreign = result.Foreign;
            var trust = result.InvestmentTrust;
            if (!foreign.HasSufficientData && !trust.HasSufficientData)
            {
                result.Summary = "需至少 20 個有效法人訊號日，且買超、賣超各至少 6 日，才能判讀價格敏感度。";
                return result;
            }

            var leading = foreign.Score >= trust.Score ? foreign : trust;
            var label = foreign.Score >= trust.Score ? "外資" : "投信";
            var foreignIsSensitive = IsSensitive(foreign);
            var trustIsSensitive = IsSensitive(trust);
            if (foreignIsSensitive && trustIsSensitive && Math.Abs(foreign.Score - trust.Score) < 10)
                label = "法人共振";
            else if (!IsSensitive(leading))
                label = "法人敏感度不明顯";
            else
                label += "敏感型";

            result.LeadershipLabel = label;
            result.Summary = BuildSummary(label, foreign, trust);
            return result;
        }

        private static InstitutionalSensitivityMetric Calculate(
            IReadOnlyList<CandleData> candles,
            IReadOnlyDictionary<DateTime, TwseT86Record> records,
            Func<TwseT86Record, long> getNet)
        {
            var metric = new InstitutionalSensitivityMetric();
            if (candles == null || candles.Count < ForwardDays + 20 || records == null)
                return metric;

            var start = Math.Max(0, candles.Count - LookbackDays - ForwardDays);
            var observations = new List<Observation>();
            for (var index = start; index < candles.Count - ForwardDays; index++)
            {
                TwseT86Record record;
                if (!records.TryGetValue(candles[index].Time.Date, out record) || record == null)
                    continue;

                var averageVolumeLots = candles.Skip(Math.Max(0, index - 19)).Take(Math.Min(20, index + 1))
                    .Average(candle => (decimal)candle.Volume);
                var denominator = Math.Max(averageVolumeLots, (decimal)candles[index].Volume * 0.5m);
                if (denominator <= 0) continue;
                var netLots = getNet(record) / 1000m;
                var flowRatio = netLots / denominator;
                var flowAmount = Math.Abs(netLots * candles[index].Close * 1000m);
                if (Math.Abs(flowRatio) < MinimumFlowRatio || flowAmount < MinimumSignalAmount) continue;

                var forwardClose = candles[index + ForwardDays].Close;
                var forwardReturn = (forwardClose / candles[index].Close - 1m) * 100m;
                observations.Add(new Observation { FlowRatio = flowRatio, ForwardReturn = forwardReturn });
            }

            metric.SignalDays = observations.Count;
            if (observations.Count < MinimumSignalDays)
                return metric;

            var buy = observations.Where(item => item.FlowRatio > 0).ToList();
            var sell = observations.Where(item => item.FlowRatio < 0).ToList();
            if (buy.Count < MinimumDirectionalSignalDays || sell.Count < MinimumDirectionalSignalDays)
                return metric;

            var correct = observations.Count(item => Math.Sign(item.FlowRatio) == Math.Sign(item.ForwardReturn));
            metric.HitRatePercent = Math.Round(correct * 100m / observations.Count, 1);
            metric.BuyFollowThroughPercent = Math.Round(buy.Average(item => item.ForwardReturn), 2);
            metric.SellFollowThroughPercent = Math.Round(sell.Average(item => item.ForwardReturn), 2);
            metric.SpreadPercent = Math.Round(metric.BuyFollowThroughPercent - metric.SellFollowThroughPercent, 2);
            metric.IsDirectionallyConsistent = metric.BuyFollowThroughPercent > 0m && metric.SellFollowThroughPercent < 0m;

            var hitScore = Clamp((metric.HitRatePercent - 50m) / 25m * 35m, 0m, 35m);
            var effectScore = Clamp(metric.SpreadPercent / 8m * 30m, 0m, 30m);
            var sampleScore = Clamp(observations.Count / 20m * 15m, 0m, 15m);
            var balanceScore = Clamp(Math.Min(buy.Count, sell.Count) / 8m * 10m, 0m, 10m);
            var directionalScore = metric.IsDirectionallyConsistent ? 10m : 0m;
            metric.Score = (int)Math.Round(hitScore + effectScore + sampleScore + balanceScore + directionalScore, MidpointRounding.AwayFromZero);
            metric.Confidence = (int)Math.Round(
                Clamp(observations.Count / 20m * 60m, 0m, 60m) +
                Clamp(Math.Min(buy.Count, sell.Count) / 8m * 20m, 0m, 20m) +
                Clamp((metric.HitRatePercent - 50m) / 25m * 20m, 0m, 20m),
                MidpointRounding.AwayFromZero);
            metric.HasSufficientData = true;
            return metric;
        }

        private static bool IsSensitive(InstitutionalSensitivityMetric metric)
        {
            return metric != null &&
                metric.HasSufficientData &&
                metric.IsDirectionallyConsistent &&
                metric.Score >= MinimumSensitiveScore &&
                metric.Confidence >= MinimumSensitiveConfidence;
        }

        private static string BuildSummary(string label, InstitutionalSensitivityMetric foreign, InstitutionalSensitivityMetric trust)
        {
            if (label == "法人共振")
                return $"近 60 日外資與投信皆達敏感度與信心門檻（外資 {foreign.Score}／投信 {trust.Score}）。";
            if (label == "法人敏感度不明顯")
                return $"有效法人訊號日已足夠，但雙向後續走勢或敏感度／信心未達門檻（外資 {foreign.Score}／投信 {trust.Score}）。";
            var leading = label.StartsWith("外資", StringComparison.Ordinal) ? foreign : trust;
            return $"近 60 日有效訊號 {leading.SignalDays} 日；顯著買超後兩日平均 {leading.BuyFollowThroughPercent:+0.00;-0.00;0.00}%、賣超後 {leading.SellFollowThroughPercent:+0.00;-0.00;0.00}%，命中率 {leading.HitRatePercent:F1}%。";
        }

        private static string FormatAmount(decimal amount)
        {
            return amount >= 100000000m
                ? (amount / 100000000m).ToString("0.0") + " 億"
                : (amount / 10000m).ToString("0") + " 萬";
        }

        private static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
