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
        private const decimal MinimumFlowRatio = 0.005m;
        private const int MinimumSignalDays = 12;

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

            var result = new InstitutionalSensitivityResult
            {
                Foreign = Calculate(dailyCandles, records, record => record.ForeignNet),
                InvestmentTrust = Calculate(dailyCandles, records, record => record.InvestmentTrustNet)
            };

            var foreign = result.Foreign;
            var trust = result.InvestmentTrust;
            if (!foreign.HasSufficientData && !trust.HasSufficientData)
                return result;

            var leading = foreign.Score >= trust.Score ? foreign : trust;
            var label = foreign.Score >= trust.Score ? "外資" : "投信";
            var other = label == "外資" ? trust : foreign;
            if (foreign.HasSufficientData && trust.HasSufficientData && Math.Abs(foreign.Score - trust.Score) < 10)
                label = "法人共振";
            else if (leading.Score < 55)
                label = "法人敏感度不明顯";
            else
                label += "敏感型";

            result.LeadershipLabel = label;
            result.Summary = BuildSummary(label, foreign, trust, other);
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

                var averageVolume = candles.Skip(Math.Max(0, index - 19)).Take(Math.Min(20, index + 1))
                    .Average(candle => (decimal)candle.Volume);
                var denominator = Math.Max((decimal)candles[index].Volume, averageVolume * 0.5m);
                if (denominator <= 0) continue;
                var flowRatio = getNet(record) / denominator;
                if (Math.Abs(flowRatio) < MinimumFlowRatio) continue;

                var forwardClose = candles[index + ForwardDays].Close;
                var forwardReturn = (forwardClose / candles[index].Close - 1m) * 100m;
                observations.Add(new Observation { FlowRatio = flowRatio, ForwardReturn = forwardReturn });
            }

            metric.SignalDays = observations.Count;
            if (observations.Count < MinimumSignalDays)
                return metric;

            var buy = observations.Where(item => item.FlowRatio > 0).ToList();
            var sell = observations.Where(item => item.FlowRatio < 0).ToList();
            if (buy.Count < 3 || sell.Count < 3)
                return metric;

            var correct = observations.Count(item => Math.Sign(item.FlowRatio) == Math.Sign(item.ForwardReturn));
            metric.HitRatePercent = Math.Round(correct * 100m / observations.Count, 1);
            metric.BuyFollowThroughPercent = Math.Round(buy.Average(item => item.ForwardReturn), 2);
            metric.SellFollowThroughPercent = Math.Round(sell.Average(item => item.ForwardReturn), 2);
            metric.SpreadPercent = Math.Round(metric.BuyFollowThroughPercent - metric.SellFollowThroughPercent, 2);

            var hitScore = Clamp((metric.HitRatePercent - 50m) / 25m * 40m, 0m, 40m);
            var effectScore = Clamp(metric.SpreadPercent / 8m * 35m, 0m, 35m);
            var sampleScore = Clamp(observations.Count / 20m * 15m, 0m, 15m);
            var balanceScore = Clamp(Math.Min(buy.Count, sell.Count) / 8m * 10m, 0m, 10m);
            metric.Score = (int)Math.Round(hitScore + effectScore + sampleScore + balanceScore, MidpointRounding.AwayFromZero);
            metric.Confidence = (int)Math.Round(
                Clamp(observations.Count / 20m * 60m, 0m, 60m) +
                Clamp(Math.Min(buy.Count, sell.Count) / 8m * 20m, 0m, 20m) +
                Clamp((metric.HitRatePercent - 50m) / 25m * 20m, 0m, 20m),
                MidpointRounding.AwayFromZero);
            metric.HasSufficientData = true;
            return metric;
        }

        private static string BuildSummary(string label, InstitutionalSensitivityMetric foreign, InstitutionalSensitivityMetric trust, InstitutionalSensitivityMetric other)
        {
            if (label == "法人共振")
                return $"近 60 日外資與投信皆有可重複的後續價格反應（外資 {foreign.Score}／投信 {trust.Score}）。";
            if (label == "法人敏感度不明顯")
                return $"有效法人訊號日已足夠，但後續兩日走勢的方向一致性不足（外資 {foreign.Score}／投信 {trust.Score}）。";
            var leading = label.StartsWith("外資", StringComparison.Ordinal) ? foreign : trust;
            return $"近 60 日有效訊號 {leading.SignalDays} 日；顯著買超後兩日平均 {leading.BuyFollowThroughPercent:+0.00;-0.00;0.00}%、賣超後 {leading.SellFollowThroughPercent:+0.00;-0.00;0.00}%，命中率 {leading.HitRatePercent:F1}%。";
        }

        private static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
