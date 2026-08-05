using StockManager.Library;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Services
{
    public class BacktestTrade
    {
        public DateTime EntryDate { get; set; }
        public DateTime ExitDate { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal ReturnPercent { get; set; }
        public string ExitReason { get; set; }
    }

    public class BacktestResult
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public decimal WinRate { get { return TotalTrades == 0 ? 0m : Math.Round(WinningTrades * 100m / TotalTrades, 2); } }
        public decimal TotalReturnPercent { get; set; }
        public decimal MaxDrawdownPercent { get; set; }
        public IReadOnlyList<BacktestTrade> Trades { get; set; }
        public bool MeetsWinRateTarget { get { return TotalTrades >= 20 && WinRate >= 80m; } }
        public string ValidationMessage
        {
            get
            {
                if (TotalTrades < 20) return "樣本交易數不足 20 筆，不能宣稱策略達到 80% 勝率。";
                return MeetsWinRateTarget ? "達到設定的 80% 勝率門檻；仍應以不同市場週期做樣本外驗證。" : "未達 80% 勝率門檻，請勿將此參數視為可用策略。";
            }
        }
    }

    // Uses next-session execution and conservative exits. It is intentionally pure so it can be
    // evaluated on historical data without looking ahead.
    public static class StrategyBacktester
    {
        public static BacktestResult Run(IReadOnlyList<CandleData> candles, int entryScore = 72, int exitScore = 50, decimal stopLossPercent = 0.07m, decimal takeProfitPercent = 0.14m)
        {
            var ordered = (candles ?? new List<CandleData>()).OrderBy(x => x.Time).ToList();
            PopulateIndicators(ordered);
            var trades = new List<BacktestTrade>();
            decimal equity = 1m, peakEquity = 1m, maxDrawdown = 0m;
            BacktestTrade openTrade = null;

            for (var index = 20; index < ordered.Count - 1; index++)
            {
                var history = ordered.Take(index + 1).ToList();
                var current = history[history.Count - 1];
                var previous = history[history.Count - 2];
                var recommendation = TradingRecommendationLibrary.CalculateAdvancedRecommendation(history, (double)current.Close, (double?)current.PercentageChange, (double)previous.Close);
                var nextOpen = ordered[index + 1].Open > 0 ? ordered[index + 1].Open : ordered[index + 1].Close;

                if (openTrade == null)
                {
                    // Enter only with trend confirmation. Conservative entry reduces false positives.
                    if (recommendation.Score >= entryScore && recommendation.CrashRiskScore < 45 && current.Close > (decimal)current.MA20 && current.MA5 > current.MA20)
                        openTrade = new BacktestTrade { EntryDate = ordered[index + 1].Time, EntryPrice = nextOpen };
                    continue;
                }

                var stopPrice = openTrade.EntryPrice * (1m - stopLossPercent);
                var targetPrice = openTrade.EntryPrice * (1m + takeProfitPercent);
                var exitReason = current.Low <= stopPrice ? "STOP_LOSS" : current.High >= targetPrice ? "TAKE_PROFIT" : recommendation.CrashRiskScore >= 70 ? "RISK_EXIT" : recommendation.Score < exitScore ? "SCORE_EXIT" : null;
                if (exitReason == null) continue;

                // Protective orders can fill during this already-completed session. A score/risk exit is
                // deliberately delayed to the following open, because that signal exists only at close.
                var exitPrice = exitReason == "STOP_LOSS" ? stopPrice : exitReason == "TAKE_PROFIT" ? targetPrice : nextOpen;
                openTrade.ExitDate = exitReason == "STOP_LOSS" || exitReason == "TAKE_PROFIT" ? current.Time : ordered[index + 1].Time;
                openTrade.ExitPrice = exitPrice;
                openTrade.ExitReason = exitReason;
                openTrade.ReturnPercent = Math.Round((exitPrice / openTrade.EntryPrice - 1m) * 100m, 2);
                trades.Add(openTrade);
                equity *= 1m + openTrade.ReturnPercent / 100m;
                peakEquity = Math.Max(peakEquity, equity);
                maxDrawdown = Math.Min(maxDrawdown, (equity / peakEquity - 1m) * 100m);
                openTrade = null;
            }

            if (openTrade != null && ordered.Count > 0)
            {
                var final = ordered[ordered.Count - 1];
                openTrade.ExitDate = final.Time; openTrade.ExitPrice = final.Close; openTrade.ExitReason = "END_OF_DATA";
                openTrade.ReturnPercent = Math.Round((final.Close / openTrade.EntryPrice - 1m) * 100m, 2); trades.Add(openTrade);
                equity *= 1m + openTrade.ReturnPercent / 100m;
                peakEquity = Math.Max(peakEquity, equity);
                maxDrawdown = Math.Min(maxDrawdown, (equity / peakEquity - 1m) * 100m);
            }

            return new BacktestResult { TotalTrades = trades.Count, WinningTrades = trades.Count(x => x.ReturnPercent > 0m), TotalReturnPercent = Math.Round((equity - 1m) * 100m, 2), MaxDrawdownPercent = Math.Round(maxDrawdown, 2), Trades = trades };
        }

        private static void PopulateIndicators(IReadOnlyList<CandleData> candles)
        {
            var closes = candles.Select(x => (double)x.Close).ToList();
            var ema12 = 0d;
            var ema26 = 0d;
            var signal = 0d;
            for (var i = 0; i < candles.Count; i++)
            {
                candles[i].PercentageChange = i == 0 || candles[i - 1].Close == 0m ? 0m : (candles[i].Close / candles[i - 1].Close - 1m) * 100m;
                candles[i].MA5 = Average(closes, i, 5);
                candles[i].MA20 = Average(closes, i, 20);
                candles[i].MA120 = Average(closes, i, 120);
                candles[i].MA240 = Average(closes, i, 240);
                candles[i].RSI = CalculateRsi(closes, i, 14);
                ema12 = i == 0 ? closes[i] : closes[i] * 2d / 13d + ema12 * 11d / 13d;
                ema26 = i == 0 ? closes[i] : closes[i] * 2d / 27d + ema26 * 25d / 27d;
                candles[i].MACD = ema12 - ema26;
                signal = i == 0 ? candles[i].MACD : candles[i].MACD * 2d / 10d + signal * 8d / 10d;
                candles[i].MacdSignal = signal;
                candles[i].MacdHistogram = candles[i].MACD - signal;
            }
        }

        private static double Average(IReadOnlyList<double> values, int index, int period)
        {
            var start = Math.Max(0, index - period + 1);
            var count = index - start + 1;
            return values.Skip(start).Take(count).Average();
        }

        private static double CalculateRsi(IReadOnlyList<double> closes, int index, int period)
        {
            if (index == 0) return 50d;
            var start = Math.Max(1, index - period + 1);
            var gains = 0d;
            var losses = 0d;
            for (var i = start; i <= index; i++)
            {
                var change = closes[i] - closes[i - 1];
                if (change >= 0d) gains += change; else losses -= change;
            }
            return losses <= 0d ? 100d : 100d - 100d / (1d + gains / losses);
        }
    }
}
