using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Services
{
    public static class RiskManagementService
    {
        public static RiskRecommendation Evaluate(OpenPosition position, decimal lastPrice, IEnumerable<CandleData> candles, int strategyScore, int crashRiskScore)
        {
            if (position == null || position.Quantity <= 0 || position.AverageCost <= 0)
                return new RiskRecommendation { Action = "NO_POSITION", Summary = "尚無持倉，請先記錄買進交易。" };

            var recent = (candles ?? Enumerable.Empty<CandleData>()).OrderByDescending(x => x.Time).Take(20).ToList();
            var atr = CalculateAtr(recent);
            var costStop = position.AverageCost * 0.93m;
            var volatilityStop = lastPrice - atr * 2m;
            var stop = Math.Max(costStop, volatilityStop);
            var takeProfit = position.AverageCost + (position.AverageCost - stop) * 2m;
            var highestClose = recent.Count == 0 ? lastPrice : recent.Max(x => x.Close);
            var trailing = Math.Max(stop, highestClose - atr * 2m);
            var action = lastPrice <= stop || crashRiskScore >= 75 ? "EXIT" : lastPrice >= takeProfit ? "TAKE_PROFIT" : "HOLD";
            return new RiskRecommendation
            {
                StopLossPrice = Math.Round(stop, 2), TakeProfitPrice = Math.Round(takeProfit, 2), TrailingStopPrice = Math.Round(trailing, 2), Action = action,
                Summary = string.Format("風險建議：{0}；停損 {1:F2}、移動停損 {2:F2}、分批停利 {3:F2}。策略分數 {4}、風險分數 {5}。", action, stop, trailing, takeProfit, strategyScore, crashRiskScore)
            };
        }

        private static decimal CalculateAtr(IReadOnlyList<CandleData> recent)
        {
            if (recent == null || recent.Count < 2) return recent != null && recent.Count == 1 ? recent[0].Close * 0.03m : 0m;
            decimal total = 0m;
            for (var i = 0; i < recent.Count - 1; i++)
            {
                var current = recent[i]; var previous = recent[i + 1];
                total += Math.Max(current.High - current.Low, Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));
            }
            return total / (recent.Count - 1);
        }
    }
}
