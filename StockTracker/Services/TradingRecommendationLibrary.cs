using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockManager.Library
{
    public class TrendRecommendationResult
    {
        public int Score { get; set; }
        public List<string> Reasons { get; set; }
    }

    public static class TradingRecommendationLibrary
    {
        public static TrendRecommendationResult CalculateAdvancedRecommendation(
            List<CandleData> data,
            double currentPrice,
            double? changePercent,
            double? previousClose = null,
            TwseT86History twseHistory = null,
            DateTime? analysisDate = null)
        {
            var reasons = new List<string>();
            if (data == null || data.Count < 20)
            {
                var simpleScore = CalculateSimpleScore(currentPrice, previousClose, changePercent);
                reasons.Add("歷史資料不足，使用即時價格簡化模型評分");
                return new TrendRecommendationResult
                {
                    Score = simpleScore,
                    Reasons = reasons
                };
            }

            var getLastData = data.Last();
            var closes = data.Select(d => (double)d.Close).ToList();
            var avgPrice = getLastData.MA20;
	    var latest = data[data.Count - 1];

            var score = 50;

            var ma5 = getLastData.MA5;
	    var ma20 = getLastData.MA20;
	    if (currentPrice > ma5 && ma5 > ma20)
            {
                score += 12;
                reasons.Add($"MA 多頭排列（現價 {currentPrice:F2} > MA5 {ma5:F2} > MA20 {ma20:F2}）");
            }
            else if (currentPrice < ma5 && ma5 < ma20)
            {
                score -= 12;
                reasons.Add($"MA 空頭排列（現價 {currentPrice:F2} < MA5 {ma5:F2} < MA20 {ma20:F2}）");
            }
            else
            {
                reasons.Add("MA 結構中性，趨勢尚未明確");
            }

            var macd = getLastData.MACD;
	    var signal = getLastData.MacdSignal;
            var hist = getLastData.MACD - getLastData.MacdSignal;
	    var preData = data[data.Count - 2];
            var prevHist = preData.MACD - preData.MacdSignal;

	    if (macd > signal)
            {
                score += 14;
                reasons.Add($"MACD 位於訊號線上方（MACD {macd:F3} > Signal {signal:F3}）");
            }
            else
            {
                score -= 14;
                reasons.Add($"MACD 位於訊號線下方（MACD {macd:F3} < Signal {signal:F3}）");
            }

            if (hist > prevHist)
            {
                score += 6;
                reasons.Add("MACD 柱狀體擴大，短線動能轉強");
            }
            else if (hist < prevHist)
            {
                score -= 6;
                reasons.Add("MACD 柱狀體縮小，短線動能轉弱");
            }

            var rsi = getLastData.RSI;
            if (rsi < 30)
            {
                score += 10;
                reasons.Add($"RSI={rsi:F1} 處於超賣區，具反彈機會");
            }
            else if (rsi > 70)
            {
                score -= 10;
                reasons.Add($"RSI={rsi:F1} 處於超買區，短線回檔風險較高");
            }
            else if (rsi >= 45 && rsi <= 60)
            {
                score += 3;
                reasons.Add($"RSI={rsi:F1} 位於中性偏多區間");
            }
            else
            {
                reasons.Add($"RSI={rsi:F1}，未出現極端訊號");
            }

            var avgVol20 = data.Skip(Math.Max(0, data.Count - 20)).Average(x => (double)x.Volume);
            var volumeRatio = avgVol20 > 0 ? latest.Volume / avgVol20 : 1.0;
            if (volumeRatio >= 1.2)
            {
                if (macd > signal)
                {
                    score += 8;
                    reasons.Add($"成交量放大 {volumeRatio:F2} 倍，且多方訊號成立");
                }
                else
                {
                    score -= 8;
                    reasons.Add($"成交量放大 {volumeRatio:F2} 倍，但空方訊號較強");
                }
            }
            else
            {
                reasons.Add($"成交量為 20 日均量的 {volumeRatio:F2} 倍，量能一般");
            }

            if (currentPrice > avgPrice * 1.08)
            {
                score -= 4;
                reasons.Add("現價偏離均價較高，追價風險上升");
            }
            else if (currentPrice < avgPrice * 0.92)
            {
                score += 4;
                reasons.Add("現價低於均價，具均值回歸空間");
            }

            if (changePercent.HasValue)
            {
                if (changePercent.Value >= 3) score += 2;
                else if (changePercent.Value <= -3) score -= 2;
            }

	    if (twseHistory != null && twseHistory.RecordsByDate != null && twseHistory.RecordsByDate.Count > 0)
	    {
		var targetDate = (analysisDate ?? latest.Time).Date;
		var orderedInstitutional = twseHistory.RecordsByDate
		    .Where(x => x.Key.Date <= targetDate)
		    .OrderBy(x => x.Key)
		    .Select(x => x.Value)
		    .ToList();

		var latestInstitutional = orderedInstitutional.LastOrDefault();
		var previousInstitutional = orderedInstitutional.Count > 1
		    ? orderedInstitutional[orderedInstitutional.Count - 2]
		    : null;

		if (latestInstitutional != null)
		{
		    var f = latestInstitutional.ForeignNet;
		    var t = latestInstitutional.InvestmentTrustNet;
		    var d = latestInstitutional.DealerSelfNet; // 建議改用自行買賣

		    // --- 絕對值方向判斷 ---
		    if (f > 0) { score += 4; reasons.Add($"外資買超 {f:N0} 股，偏多加分"); }
		    else if (f < 0) { score -= 4; reasons.Add($"外資賣超 {Math.Abs(f):N0} 股，偏空扣分"); }

		    if (t > 0) { score += 3; reasons.Add($"投信買超 {t:N0} 股，趨勢支撐"); }
		    else if (t < 0) { score -= 3; reasons.Add($"投信賣超 {Math.Abs(t):N0} 股，趨勢轉弱"); }

		    if (d > 0) { score += 2; reasons.Add($"自營商自行買超 {d:N0} 股，短線偏多"); }
		    else if (d < 0) { score -= 2; reasons.Add($"自營商自行賣超 {Math.Abs(d):N0} 股，短線偏空"); }

		    // --- 三大法人同向 ---
		    var sameDirectionBuy = f > 0 && t > 0 && d > 0;
		    var sameDirectionSell = f < 0 && t < 0 && d < 0;
		    if (sameDirectionBuy) { score += 4; reasons.Add("三大法人同向買超，籌碼共振偏多"); }
		    else if (sameDirectionSell) { score -= 4; reasons.Add("三大法人同向賣超，籌碼共振偏空"); }
		}

		// --- 前一日比較，算百分比變化率 ---
		if (previousInstitutional != null && latestInstitutional != null)
		{
		    // 外資
		    if (previousInstitutional.ForeignNet != 0)
		    {
			var fRate = (double)(latestInstitutional.ForeignNet - previousInstitutional.ForeignNet) / Math.Abs(previousInstitutional.ForeignNet) * 100.0;
			score = BuySellRating(reasons, score, fRate, "外資", 3);
		    }

		    // 投信
		    if (previousInstitutional.InvestmentTrustNet != 0)
		    {
			var tRate = (double)(latestInstitutional.InvestmentTrustNet - previousInstitutional.InvestmentTrustNet) / Math.Abs(previousInstitutional.InvestmentTrustNet) * 100.0;
			score = BuySellRating(reasons, score, tRate, "投信", 1.5);
		    }

		    // 自營商 (自行買賣)
		    if (previousInstitutional.DealerSelfNet != 0)
		    {
			var dRate = (double)(latestInstitutional.DealerSelfNet - previousInstitutional.DealerSelfNet) / Math.Abs(previousInstitutional.DealerSelfNet) * 100.0;
			score = BuySellRating(reasons, score, dRate, "自營商", 1);
		    }
		}

		// --- 近五日累計 ---
		var recent5 = orderedInstitutional.Skip(Math.Max(0, orderedInstitutional.Count - 5)).ToList();
		if (recent5.Count > 0)
		{
		    var rollingSum = recent5.Sum(x => x.ThreeMajorNet);
		    if (rollingSum > 0) { score += 4; reasons.Add($"近 {recent5.Count} 日三大法人累計買超 {rollingSum:N0} 股"); }
		    else if (rollingSum < 0) { score -= 4; reasons.Add($"近 {recent5.Count} 日三大法人累計賣超 {Math.Abs(rollingSum):N0} 股"); }
		}
	    }

	    score = Math.Max(0, Math.Min(100, score));

            return new TrendRecommendationResult
            {
                Score = score,
                Reasons = reasons
            };
        }

	private static int BuySellRating(List<string> reasons, int score, double fRate, string name , double scoreScale = 1)
	{
	    if (fRate >= 200) { score += (int)(6 * scoreScale); reasons.Add($"外資淨買賣超相較前一日增加 {fRate:F2}%，強烈改善"); }
	    else if (fRate >= 100) { score += (int)(3 * scoreScale); reasons.Add($"外資淨買賣超相較前一日增加 {fRate:F2}%，中度改善"); }
	    else if (fRate > 0) { score += (int)(1 * scoreScale); reasons.Add($"外資淨買賣超相較前一日增加 {fRate:F2}%，輕微改善"); }
	    else if (fRate <= -90) { score -= (int)(6 * scoreScale); reasons.Add($"外資淨買賣超相較前一日減少 {Math.Abs(fRate):F2}%，強烈轉弱"); }
	    else if (fRate <= -50) { score -=  (int)(3 * scoreScale); reasons.Add($"外資淨買賣超相較前一日減少 {Math.Abs(fRate):F2}%，中度轉弱"); }
	    else if (fRate < 0) { score -= (int)(1 * scoreScale); reasons.Add($"外資淨買賣超相較前一日減少 {Math.Abs(fRate):F2}%，輕微轉弱"); }

	    return score;
	}

	public static int CalculateSimpleScore(double? price, double? previousClose, double? changePercent)
        {
            var score = 50;

            if (changePercent.HasValue)
            {
                if (changePercent.Value >= 4) score += 20;
                else if (changePercent.Value >= 2) score += 12;
                else if (changePercent.Value > 0) score += 5;
                else if (changePercent.Value <= -4) score -= 20;
                else if (changePercent.Value <= -2) score -= 12;
                else if (changePercent.Value < 0) score -= 5;
            }
            else
            {
                score -= 5;
            }

            if (price.HasValue && previousClose.HasValue)
            {
                var gap = price.Value - previousClose.Value;
                if (gap > 0) score += 3;
                else if (gap < 0) score -= 3;
            }

            return Math.Max(0, Math.Min(100, score));
        }

        public static string GetSimpleSuggestion(int score)
        {
            if (score >= 70) return "偏多（買入）";
            if (score >= 50) return "中性（觀望）";
            return "偏空（賣出）";
        }

        public static string GetAdvancedSuggestion(int score)
        {
            if (score >= 70) return "偏多（買入）";
            if (score >= 50) return "中性（觀望）";
            return "偏空（賣出）";
        }

        public static List<Tuple<int, string, string>> BuildBacktestSignals(List<CandleData> data)
        {
            var signals = new List<Tuple<int, string, string>>();
            if (data == null || data.Count < 30)
            {
                return signals;
            }

            var lastSignal = string.Empty;

            for (int i = 20; i < data.Count; i++)
            {
                var slice = data.Take(i + 1).ToList();
                var closes = slice.Select(x => (double)x.Close).ToList();
                var current = slice[slice.Count - 1];
                var currentClose = (double)current.Close;

                var ma5 = CalculateMA(closes, 5);
                var ma20 = CalculateMA(closes, 20);
                var rsi = CalculateRSI(closes, 14);
                var macd = BuildMACDComponents(closes);
                var macdNow = macd.Item1[macd.Item1.Count - 1];
                var signalNow = macd.Item2[macd.Item2.Count - 1];
                var avgVol20 = slice.Skip(Math.Max(0, slice.Count - 20)).Average(x => (double)x.Volume);
                var volRatio = avgVol20 > 0 ? current.Volume / avgVol20 : 1.0;

                var buy = macdNow > signalNow && currentClose > ma5 && ma5 > ma20 && rsi < 70;
                var sell = macdNow < signalNow && currentClose < ma5 && ma5 < ma20 && rsi > 30;

                var strengthScore = 0;
                strengthScore += macdNow > signalNow ? 1 : -1;
                strengthScore += ma5 > ma20 ? 1 : -1;
                if (rsi >= 40 && rsi <= 60) strengthScore += 1;
                if (volRatio >= 1.2) strengthScore += 1;

                if (buy && lastSignal != "BUY")
                {
                    var action = strengthScore >= 3 ? "STRONG_BUY" : "BUY";
                    var levelText = action == "STRONG_BUY" ? "強買" : "買入";
                    var reason = $"{levelText}｜MACD多頭 + MA5>MA20 + RSI={rsi:F1} + 量比{volRatio:F2}";
                    signals.Add(Tuple.Create(i, action, reason));
                    lastSignal = "BUY";
                }
                else if (sell && lastSignal != "SELL")
                {
                    var action = strengthScore <= -3 ? "STRONG_SELL" : "SELL";
                    var levelText = action == "STRONG_SELL" ? "強賣" : "賣出";
                    var reason = $"{levelText}｜MACD空頭 + MA5<MA20 + RSI={rsi:F1} + 量比{volRatio:F2}";
                    signals.Add(Tuple.Create(i, action, reason));
                    lastSignal = "SELL";
                }
            }

            return signals;
        }

        private static double CalculateMA(List<double> prices, int period)
        {
            if (prices.Count < period) return prices[prices.Count - 1];
            return prices.Skip(prices.Count - period).Take(period).Average();
        }

        private static double CalculateRSI(List<double> prices, int period)
        {
            if (prices.Count < period + 1) return 50;

            var gains = new List<double>();
            var losses = new List<double>();

            for (int i = prices.Count - period; i < prices.Count; i++)
            {
                var change = prices[i] - prices[i - 1];
                gains.Add(change > 0 ? change : 0);
                losses.Add(change < 0 ? -change : 0);
            }

            var avgGain = gains.Average();
            var avgLoss = losses.Average();

            if (avgLoss == 0) return 100;
            var rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        private static Tuple<List<double>, List<double>, List<double>> BuildMACDComponents(List<double> closes)
        {
            var macdSeries = new List<double>();
            var signalSeries = new List<double>();
            var histSeries = new List<double>();

            if (closes == null || closes.Count == 0)
            {
                return Tuple.Create(macdSeries, signalSeries, histSeries);
            }

            var macdLineForSignal = new List<double>();
            for (int i = 0; i < closes.Count; i++)
            {
                var slice = closes.Take(i + 1).ToList();
                if (slice.Count < 26)
                {
                    macdSeries.Add(0);
                    signalSeries.Add(0);
                    histSeries.Add(0);
                    macdLineForSignal.Add(0);
                    continue;
                }

                var ema12 = CalculateEMA(slice, 12);
                var ema26 = CalculateEMA(slice, 26);
                var macd = ema12 - ema26;
                macdSeries.Add(macd);
                macdLineForSignal.Add(macd);

                var signal = CalculateEMA(macdLineForSignal, 9);
                signalSeries.Add(signal);
                histSeries.Add(macd - signal);
            }

            return Tuple.Create(macdSeries, signalSeries, histSeries);
        }

        private static double CalculateEMA(List<double> prices, int period)
        {
            if (prices.Count < period) return prices[prices.Count - 1];

            var multiplier = 2.0 / (period + 1);
            var ema = prices.Take(period).Average();

            for (int i = period; i < prices.Count; i++)
            {
                ema = (prices[i] - ema) * multiplier + ema;
            }

            return ema;
        }
    }
}
