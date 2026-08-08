using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Services
{
    /// <summary>
    /// 代表一條水平支撐 / 壓力價格區。
    /// </summary>
    public class PriceZone
    {
        /// <summary>區域中心價格。</summary>
        public double Price { get; set; }
        /// <summary>此區域被測試的次數（>= 2 視為有效）。</summary>
        public int TouchCount { get; set; }
        /// <summary>是否為有效區（TouchCount >= 2）。</summary>
        public bool IsValid => TouchCount >= 2;
        /// <summary>區域性質（Support / Resistance）。</summary>
        public string Kind { get; set; }
    }

    /// <summary>
    /// 代表一條趨勢線（由兩點定義的直線 y = a·x + b）。
    /// </summary>
    public class TrendLine
    {
        /// <summary>斜率（每根 K 棒的價格變化量）。</summary>
        public double Slope { get; set; }
        /// <summary>截距（x=0 時的估算價格）。</summary>
        public double Intercept { get; set; }
        /// <summary>連接的轉折點數量。</summary>
        public int AnchorCount { get; set; }
        /// <summary>趨勢方向：Bullish / Bearish。</summary>
        public string Direction { get; set; }

        /// <summary>估算在第 <paramref name="barIndex"/> 根 K 棒時的趨勢線價格。</summary>
        public double PriceAt(int barIndex) => Slope * barIndex + Intercept;
    }

    /// <summary>
    /// 量化技術線型工具：分形偵測、水平支撐壓力聚類、趨勢線斜率計算。
    /// </summary>
    public static class TechnicalLineQuantizer
    {
        /// <summary>
        /// 找出 N 棒分形高點（Pivot High）與低點（Pivot Low）。
        /// </summary>
        /// <param name="candles">K 棒序列（時間升序）。</param>
        /// <param name="n">左右各需多少根相鄰 K 棒（預設 2）。</param>
        /// <returns>分形轉折點清單，含索引與分類。</returns>
        public static List<(int Index, double Price, string Type)> FindPivots(
            IReadOnlyList<CandleData> candles, int n = 2)
        {
            var pivots = new List<(int, double, string)>();
            if (candles == null || candles.Count < 2 * n + 1) return pivots;

            for (var i = n; i < candles.Count - n; i++)
            {
                var high = (double)candles[i].High;
                var low  = (double)candles[i].Low;

                bool isPivotHigh = true;
                bool isPivotLow  = true;

                for (var j = i - n; j <= i + n; j++)
                {
                    if (j == i) continue;
                    if ((double)candles[j].High >= high) isPivotHigh = false;
                    if ((double)candles[j].Low  <= low)  isPivotLow  = false;
                }

                if (isPivotHigh) pivots.Add((i, high, "High"));
                if (isPivotLow)  pivots.Add((i, low,  "Low"));
            }

            return pivots;
        }

        /// <summary>
        /// 將分形高低點聚類成水平支撐 / 壓力區。
        /// 同一聚類的容忍誤差為 <paramref name="tolerancePct"/>（±1.5% 預設）。
        /// 有效區需 TouchCount >= 2。
        /// </summary>
        public static List<PriceZone> BuildPriceZones(
            IReadOnlyList<(int Index, double Price, string Type)> pivots,
            double tolerancePct = 0.015d)
        {
            if (pivots == null || pivots.Count == 0) return new List<PriceZone>();

            var zones = new List<PriceZone>();

            foreach (var pivot in pivots)
            {
                bool merged = false;
                foreach (var zone in zones)
                {
                    if (Math.Abs(pivot.Price - zone.Price) / zone.Price <= tolerancePct)
                    {
                        // 合併：更新中心為現有 TouchCount 與新點的加權平均
                        zone.Price = (zone.Price * zone.TouchCount + pivot.Price) / (zone.TouchCount + 1);
                        zone.TouchCount++;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    zones.Add(new PriceZone
                    {
                        Price = pivot.Price,
                        TouchCount = 1,
                        Kind = pivot.Type == "High" ? "Resistance" : "Support"
                    });
                }
            }

            return zones.OrderByDescending(z => z.TouchCount).ToList();
        }

        /// <summary>
        /// 從分形低點序列計算上升趨勢線（最小二乘法），
        /// 從分形高點序列計算下降趨勢線。
        /// 至少需要 2 個分形點。
        /// </summary>
        public static List<TrendLine> BuildTrendLines(
            IReadOnlyList<(int Index, double Price, string Type)> pivots)
        {
            var results = new List<TrendLine>();
            if (pivots == null || pivots.Count < 2) return results;

            var highs = pivots.Where(p => p.Type == "High").OrderBy(p => p.Index).ToList();
            var lows  = pivots.Where(p => p.Type == "Low").OrderBy(p => p.Index).ToList();

            var bearLine = FitLine(highs);
            if (bearLine != null)
            {
                bearLine.Direction  = "Bearish";
                bearLine.AnchorCount = highs.Count;
                results.Add(bearLine);
            }

            var bullLine = FitLine(lows);
            if (bullLine != null)
            {
                bullLine.Direction  = "Bullish";
                bullLine.AnchorCount = lows.Count;
                results.Add(bullLine);
            }

            return results;
        }

        /// <summary>
        /// 一次完整分析：給定 K 棒資料，回傳支撐壓力區與趨勢線。
        /// </summary>
        public static (List<PriceZone> Zones, List<TrendLine> TrendLines) Analyze(
            IReadOnlyList<CandleData> candles, int pivotN = 2, double tolerancePct = 0.015d)
        {
            var pivots = FindPivots(candles, pivotN);
            var zones  = BuildPriceZones(pivots, tolerancePct);
            var lines  = BuildTrendLines(pivots);
            return (zones, lines);
        }

        // ── 私有工具 ──────────────────────────────────────────────────────
        private static TrendLine FitLine(List<(int Index, double Price, string Type)> points)
        {
            if (points.Count < 2) return null;

            double n   = points.Count;
            double sumX  = 0, sumY  = 0, sumXY = 0, sumX2 = 0;

            foreach (var p in points)
            {
                sumX  += p.Index;
                sumY  += p.Price;
                sumXY += p.Index * p.Price;
                sumX2 += p.Index * (double)p.Index;
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-9) return null;

            double slope     = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;

            return new TrendLine { Slope = slope, Intercept = intercept };
        }
    }
}
