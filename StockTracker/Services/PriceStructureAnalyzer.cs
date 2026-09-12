using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Services
{
    /// <summary>
    /// Finds daily support and resistance as price areas clustered around prior
    /// swing highs/lows. The result is a planning aid, never an execution signal.
    /// </summary>
    public static class PriceStructureAnalyzer
    {
        private const int MinimumDailyBars = 30;
        private const int MaximumLookbackBars = 120;
        private const int PivotRadius = 2;

        private sealed class LevelCandidate
        {
            public decimal Price { get; set; }
            public decimal Weight { get; set; }
            public DateTime Date { get; set; }
            public string Basis { get; set; }
        }

        public static PriceStructureAnalysis Analyze(IEnumerable<CandleData> sourceCandles)
        {
            var dailyCandles = NormalizeDailyCandles(sourceCandles);
            var result = new PriceStructureAnalysis();
            if (dailyCandles.Count == 0)
            {
                result.Message = "尚無日 K 資料，無法辨識支撐與壓力區。";
                return result;
            }

            result.LatestPrice = dailyCandles.Last().Close;
            if (dailyCandles.Count < MinimumDailyBars)
            {
                result.Message = "日 K 資料不足 30 根，請補足日 K 後再建立結構化交易計畫。";
                return result;
            }

            var candles = dailyCandles.Skip(Math.Max(0, dailyCandles.Count - MaximumLookbackBars)).ToList();
            var atr = CalculateAtr14(candles);
            result.Atr14 = atr;
            var zoneHalfWidth = Math.Max(atr * 0.35m, result.LatestPrice * 0.003m);
            var candidates = FindCandidates(candles);
            if (candidates.Count == 0)
            {
                result.Message = "找不到足夠的波段高低點，暫不自動產生交易計畫。";
                return result;
            }

            result.Supports = BuildZones(candidates, result.LatestPrice, zoneHalfWidth, true);
            result.Resistances = BuildZones(candidates, result.LatestPrice, zoneHalfWidth, false);
            result.HasSufficientData = result.Supports.Count > 0 || result.Resistances.Count > 0;
            result.Message = result.HasSufficientData
                ? "支撐／壓力區由近 120 日日 K 的波段高低點、反應次數與量能聚集而成。"
                : "目前價格附近沒有足夠的結構區，請改以手動判斷。";
            return result;
        }

        public static decimal GetPriceTick(decimal price)
        {
            if (price < 10m) return 0.01m;
            if (price < 50m) return 0.05m;
            if (price < 100m) return 0.1m;
            if (price < 500m) return 0.5m;
            if (price < 1000m) return 1m;
            return 5m;
        }

        public static decimal RoundToTick(decimal price, bool roundUp)
        {
            if (price <= 0m) return 0m;
            var tick = GetPriceTick(price);
            var units = price / tick;
            return (roundUp ? Math.Ceiling(units) : Math.Floor(units)) * tick;
        }

        private static List<CandleData> NormalizeDailyCandles(IEnumerable<CandleData> sourceCandles)
        {
            return (sourceCandles ?? Enumerable.Empty<CandleData>())
                .Where(item => item != null && item.Close > 0m)
                .GroupBy(item => item.Time.Date)
                .Select(group =>
                {
                    var ordered = group.OrderBy(item => item.Time).ToList();
                    return new CandleData
                    {
                        Time = ordered.Last().Time.Date,
                        Open = ordered.First().Open > 0m ? ordered.First().Open : ordered.First().Close,
                        High = ordered.Max(item => item.High > 0m ? item.High : item.Close),
                        Low = ordered.Min(item => item.Low > 0m ? item.Low : item.Close),
                        Close = ordered.Last().Close,
                        Volume = ordered.Sum(item => item.Volume)
                    };
                })
                .OrderBy(item => item.Time)
                .ToList();
        }

        private static decimal CalculateAtr14(IReadOnlyList<CandleData> candles)
        {
            if (candles == null || candles.Count < 2)
            {
                return 0m;
            }

            var trueRanges = new List<decimal>();
            var start = Math.Max(1, candles.Count - 14);
            for (var index = start; index < candles.Count; index++)
            {
                var current = candles[index];
                var previousClose = candles[index - 1].Close;
                var high = current.High > 0m ? current.High : current.Close;
                var low = current.Low > 0m ? current.Low : current.Close;
                trueRanges.Add(new[] { high - low, Math.Abs(high - previousClose), Math.Abs(low - previousClose) }.Max());
            }

            return trueRanges.Count == 0 ? 0m : Math.Round(trueRanges.Average(), 4);
        }

        private static List<LevelCandidate> FindCandidates(IReadOnlyList<CandleData> candles)
        {
            var candidates = new List<LevelCandidate>();
            for (var index = PivotRadius; index < candles.Count - PivotRadius; index++)
            {
                var current = candles[index];
                var window = candles.Skip(index - PivotRadius).Take(PivotRadius * 2 + 1).ToList();
                var averageVolume = candles.Skip(Math.Max(0, index - 20)).Take(Math.Min(20, index + 1))
                    .Average(item => (decimal)Math.Max(1L, item.Volume));
                var volumeWeight = Math.Min(2m, Math.Max(0.5m, current.Volume / averageVolume));
                var recencyWeight = 1m + Math.Max(0m, 0.8m - (candles.Count - 1 - index) / 100m);

                var high = current.High > 0m ? current.High : current.Close;
                var low = current.Low > 0m ? current.Low : current.Close;
                if (high >= window.Max(item => item.High > 0m ? item.High : item.Close))
                {
                    candidates.Add(new LevelCandidate
                    {
                        Price = high,
                        Weight = 10m * volumeWeight * recencyWeight,
                        Date = current.Time.Date,
                        Basis = "波段高點"
                    });
                }
                if (low <= window.Min(item => item.Low > 0m ? item.Low : item.Close))
                {
                    candidates.Add(new LevelCandidate
                    {
                        Price = low,
                        Weight = 10m * volumeWeight * recencyWeight,
                        Date = current.Time.Date,
                        Basis = "波段低點"
                    });
                }
            }

            AddRangeBoundaryCandidate(candidates, candles, 20, "20 日區間邊界");
            AddRangeBoundaryCandidate(candidates, candles, 60, "60 日區間邊界");
            return candidates;
        }

        private static void AddRangeBoundaryCandidate(ICollection<LevelCandidate> candidates, IReadOnlyList<CandleData> candles, int windowSize, string basis)
        {
            var period = candles.Skip(Math.Max(0, candles.Count - windowSize)).ToList();
            if (period.Count == 0) return;
            candidates.Add(new LevelCandidate
            {
                Price = period.Max(item => item.High > 0m ? item.High : item.Close),
                Weight = 12m,
                Date = period.Last().Time.Date,
                Basis = basis
            });
            candidates.Add(new LevelCandidate
            {
                Price = period.Min(item => item.Low > 0m ? item.Low : item.Close),
                Weight = 12m,
                Date = period.Last().Time.Date,
                Basis = basis
            });
        }

        private static List<PriceStructureZone> BuildZones(IEnumerable<LevelCandidate> candidates, decimal latestPrice, decimal halfWidth, bool support)
        {
            var relevant = (support
                    ? candidates.Where(item => item.Price <= latestPrice + halfWidth).OrderByDescending(item => item.Price)
                    : candidates.Where(item => item.Price >= latestPrice - halfWidth).OrderBy(item => item.Price))
                .ToList();
            var clusters = new List<List<LevelCandidate>>();
            foreach (var candidate in relevant)
            {
                var cluster = clusters.FirstOrDefault(items => Math.Abs(items.Average(item => item.Price) - candidate.Price) <= halfWidth * 2m);
                if (cluster == null)
                {
                    cluster = new List<LevelCandidate>();
                    clusters.Add(cluster);
                }
                cluster.Add(candidate);
            }

            var zones = clusters.Select(cluster =>
            {
                var distinctBasis = string.Join("、", cluster.Select(item => item.Basis).Distinct().Take(2));
                var strength = (int)Math.Min(100m, Math.Round(cluster.Sum(item => item.Weight) + cluster.Count * 8m));
                return new PriceStructureZone
                {
                    Low = RoundToTick(Math.Max(0.01m, cluster.Min(item => item.Price) - halfWidth), false),
                    High = RoundToTick(cluster.Max(item => item.Price) + halfWidth, true),
                    Strength = strength,
                    Touches = cluster.Count,
                    Basis = distinctBasis
                };
            });

            return (support
                    ? zones.OrderByDescending(zone => zone.High).ThenByDescending(zone => zone.Strength)
                    : zones.OrderBy(zone => zone.Low).ThenByDescending(zone => zone.Strength))
                .Take(3)
                .ToList();
        }
    }
}
