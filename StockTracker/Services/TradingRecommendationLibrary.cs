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

	    // 基礎檢查
	    if (data == null || data.Count < 20)
	    {
		return new TrendRecommendationResult
		{
		    Score = 50,
		    Reasons = new List<string> { "數據不足：歷史資料少於 20 筆，無法計算技術指標。" }
		};
	    }

	    var latest = data.Last();
	    var prev = data[data.Count - 2];
	    double trendScore = 0, momentumScore = 0, chipScore = 0, candlestickScore = 0, volScore = 0;

	    // --- 1. 趨勢類 (權重 40%) ---
	    // MA 排列
	    if (currentPrice > latest.MA5 && latest.MA5 > latest.MA20)
	    {
		trendScore += 20;
		reasons.Add($"[趨勢+20] MA 多頭排列：現價({currentPrice:F2}) > MA5({latest.MA5:F2}) > MA20({latest.MA20:F2})");
	    }
	    else if (currentPrice < latest.MA5 && latest.MA5 < latest.MA20)
	    {
		trendScore -= 20;
		reasons.Add($"[趨勢-20] MA 空頭排列：現價({currentPrice:F2}) < MA5({latest.MA5:F2}) < MA20({latest.MA20:F2})");
	    }
	    else
	    {
		reasons.Add("[趨勢±0] MA 交疊糾結，方向不明");
	    }

	    // MA120 長期趨勢
	    if (latest.MA120 > 0)
	    {
		if (currentPrice > latest.MA120 && latest.MA20 > latest.MA120)
		{
		    trendScore += 10;
		    reasons.Add($"[趨勢+10] 站上 MA120({latest.MA120:F2})，長多格局");
		}
		else if (currentPrice < latest.MA120 && latest.MA20 < latest.MA120)
		{
		    trendScore -= 10;
		    reasons.Add($"[趨勢-10] 跌破 MA120({latest.MA120:F2})，長空格局");
		}
		else
		{
		    reasons.Add($"[趨勢±0] MA120({latest.MA120:F2}) 多空不明確");
		}
	    }

	    // MA240 超長期趨勢
	    if (latest.MA240 > 0)
	    {
		if (currentPrice > latest.MA240 && latest.MA120 > latest.MA240)
		{
		    trendScore += 8;
		    reasons.Add($"[趨勢+8] 站上 MA240({latest.MA240:F2})，超長多頭");
		}
		else if (currentPrice < latest.MA240 && latest.MA120 < latest.MA240)
		{
		    trendScore -= 8;
		    reasons.Add($"[趨勢-8] 跌破 MA240({latest.MA240:F2})，超長空頭");
		}
		else
		{
		    reasons.Add($"[趨勢±0] MA240({latest.MA240:F2}) 方向不明");
		}
	    }

	    // 布林通道位置
	    if (latest.BollingerUpper > 0 && latest.BollingerLower > 0)
	    {
		var bbWidth = latest.BollingerUpper - latest.BollingerLower;
		if (bbWidth > 0)
		{
		    if (currentPrice >= latest.BollingerUpper)
		    {
			trendScore -= 8;
			reasons.Add($"[趨勢-8] 觸及布林上軌({latest.BollingerUpper:F2})，短線過熱風險");
		    }
		    else if (currentPrice <= latest.BollingerLower)
		    {
			trendScore += 8;
			reasons.Add($"[趨勢+8] 觸及布林下軌({latest.BollingerLower:F2})，超賣反彈機會");
		    }
		    else
		    {
			var bbPosition = (currentPrice - latest.BollingerLower) / bbWidth;
			if (bbPosition > 0.8)
			    reasons.Add($"[趨勢±0] 接近布林上軌 ({bbPosition * 100:F0}%)，留意壓力");
			else if (bbPosition < 0.2)
			    reasons.Add($"[趨勢±0] 接近布林下軌 ({bbPosition * 100:F0}%)，留意支撐");
			else
			    reasons.Add($"[趨勢±0] 布林通道中段 ({bbPosition * 100:F0}%)");
		    }
		}
	    }

	    // MA20 斜率
	    double ma20Slope = (latest.MA20 - prev.MA20) / prev.MA20;
	    if (ma20Slope > 0.002)
	    {
		trendScore += 10;
		reasons.Add($"[趨勢+10] MA20 斜率明顯向上 ({ma20Slope * 100:F2}%)");
	    }
	    else if (ma20Slope < -0.002)
	    {
		trendScore -= 10;
		reasons.Add($"[趨勢-10] MA20 斜率明顯向下 ({ma20Slope * 100:F2}%)");
	    }

	    // 價格距離 MA5 的乖離
	    double ma5Bias = (currentPrice - latest.MA5) / latest.MA5;
	    if (ma5Bias > 0.03)
	    {
		trendScore -= 8;
		reasons.Add($"[趨勢-8] 價格超越 MA5 過多 ({ma5Bias * 100:F1}%)，短線過熱");
	    }
	    else if (ma5Bias < -0.03)
	    {
		trendScore += 5;
		reasons.Add($"[趨勢+5] 價格低於 MA5 ({Math.Abs(ma5Bias) * 100:F1}%)，具反彈空間");
	    }

	    // --- 2. 動能類 (權重 20%) ---
	    var hist = latest.MACD - latest.MacdSignal;
	    var prevHist = prev.MACD - prev.MacdSignal;
	    // MACD 交叉
	    if (latest.MACD > latest.MacdSignal)
	    {
		momentumScore += 10;
		reasons.Add($"[動能+10] MACD({latest.MACD:F3}) > Signal({latest.MacdSignal:F3}) 多方控盤");
	    }
	    else
	    {
		momentumScore -= 10;
		reasons.Add($"[動能-10] MACD({latest.MACD:F3}) < Signal({latest.MacdSignal:F3}) 空方控盤");
	    }

	    // MACD 柱狀體增減
	    if (hist > prevHist && hist > 0)
	    {
		momentumScore += 6;
		reasons.Add($"[動能+6] MACD 柱狀體擴大 ({hist:F3} > {prevHist:F3})，動能轉強");
	    }
	    else if (hist < prevHist && hist < 0)
	    {
		momentumScore -= 6;
		reasons.Add($"[動能-6] MACD 柱狀體擴大 ({hist:F3} < {prevHist:F3})，動能轉弱");
	    }

	    // RSI
	    if (latest.RSI > 80)
	    {
		momentumScore -= 8;
		reasons.Add($"[動能-8] RSI 超買 ({latest.RSI:F1})，回檔風險高");
	    }
	    else if (latest.RSI < 20)
	    {
		momentumScore += 8;
		reasons.Add($"[動能+8] RSI 超賣 ({latest.RSI:F1})，反彈機會大");
	    }
	    else if (latest.RSI > 70)
	    {
		momentumScore -= 3;
		reasons.Add($"[動能-3] RSI 偏高 ({latest.RSI:F1})");
	    }
	    else if (latest.RSI < 30)
	    {
		momentumScore += 3;
		reasons.Add($"[動能+3] RSI 偏低 ({latest.RSI:F1})");
	    }
	    else
	    {
		reasons.Add($"[動能±0] RSI 常態 ({latest.RSI:F1})");
	    }

	    // --- 3. K 線型態 (權重 10%) ---
	    double high = (double)latest.High;
	    double low = (double)latest.Low;
	    double open = (double)latest.Open;
	    double close = (double)latest.Close;
	    double range = high - low;
	    if (range > 0)
	    {
		double bodyTop = Math.Max(open, close);
		double bodyBottom = Math.Min(open, close);
		double upperShadow = high - bodyTop;
		double lowerShadow = bodyBottom - low;
		double bodySize = bodyTop - bodyBottom;

		if (upperShadow > bodySize * 2 && upperShadow > range * 0.4)
		{
		    candlestickScore -= 8;
		    reasons.Add("[型態-8] 長上引線，上攻受阻");
		}
		else if (lowerShadow > bodySize * 2 && lowerShadow > range * 0.4)
		{
		    candlestickScore += 8;
		    reasons.Add("[型態+8] 長下引線，下檔承接強");
		}
		else if (bodySize > range * 0.8 && close > open)
		{
		    candlestickScore += 6;
		    reasons.Add("[型態+6] 飽滿長紅，買氣強勁");
		}
		else if (bodySize > range * 0.8 && close < open)
		{
		    candlestickScore -= 6;
		    reasons.Add("[型態-6] 飽滿長黑，賣壓沉重");
		}
		else
		{
		    reasons.Add("[型態±0] K 線平衡");
		}
	    }

	    // --- 4. 籌碼類 (權重 30%) ---
	    if (twseHistory?.RecordsByDate != null)
	    {
		var targetDate = (analysisDate ?? latest.Time).Date;
		var orderedInstitutional = twseHistory.RecordsByDate
		    .Where(x => x.Key.Date <= targetDate)
		    .OrderBy(x => x.Key).Select(x => x.Value).ToList();

		var latestChip = orderedInstitutional.LastOrDefault();
		var prevChip = orderedInstitutional.Count > 1 ? orderedInstitutional[orderedInstitutional.Count - 2] : null;

		if (latestChip != null && latest.Volume > 0)
		{
		    double fRatio = (double)latestChip.ForeignNet / latest.Volume;
		    // 絕對佔比
		    if (fRatio > 0.03)
		    {
			chipScore += 10;
			reasons.Add($"[籌碼+10] 外資強力買超 (佔量 {fRatio * 100:F1}%)");
		    }
		    else if (fRatio < -0.03)
		    {
			chipScore -= 10;
			reasons.Add($"[籌碼-10] 外資強力賣超 (佔量 {Math.Abs(fRatio) * 100:F1}%)");
		    }
		    else if (fRatio > 0.01)
		    {
			chipScore += 4;
			reasons.Add($"[籌碼+4] 外資買超 ({fRatio * 100:F1}%)");
		    }
		    else if (fRatio < -0.01)
		    {
			chipScore -= 4;
			reasons.Add($"[籌碼-4] 外資賣超 ({Math.Abs(fRatio) * 100:F1}%)");
		    }

		    // 積極度 (與昨日比較)
		    if (prevChip != null && prev.Volume > 0)
		    {
			double fRatioPrev = (double)prevChip.ForeignNet / prev.Volume;
			double fIntensity = fRatio - fRatioPrev;
			if (fIntensity > 0.03)
			{
			    chipScore += 8;
			    reasons.Add($"[籌碼+8] 外資轉積極買入 (+{fIntensity * 100:F1}%)");
			}
			else if (fIntensity < -0.03)
			{
			    chipScore -= 8;
			    reasons.Add($"[籌碼-8] 外資轉賣出 ({fIntensity * 100:F1}%)");
			}
		    }

		    // 投信
		    double tRatio = (double)latestChip.InvestmentTrustNet / latest.Volume;
		    if (tRatio > 0.02)
		    {
			chipScore += 6;
			reasons.Add($"[籌碼+6] 投信力挺買超 ({tRatio * 100:F1}%)");
		    }
		    else if (tRatio < -0.02)
		    {
			chipScore -= 4;
			reasons.Add($"[籌碼-4] 投信賣超 ({Math.Abs(tRatio) * 100:F1}%)");
		    }

		    // 五日累計
		    var sum5 = orderedInstitutional.Skip(Math.Max(0, orderedInstitutional.Count - 5)).Sum(x => x.ThreeMajorNet);
		    if (sum5 > 100000)
		    {
			chipScore += 12;
			reasons.Add($"[籌碼+12] 近5日法人持續買超 {sum5:N0} 張");
		    }
		    else if (sum5 < -100000)
		    {
			chipScore -= 12;
			reasons.Add($"[籌碼-12] 近5日法人持續賣超 {Math.Abs(sum5):N0} 張");
		    }
		}
	    }

	    // --- 5. 量價與乖離 (補分項) ---
	    var avgVol20 = data.Skip(Math.Max(0, data.Count - 20)).Average(x => (double)x.Volume);
	    var volRatio = avgVol20 > 0 ? latest.Volume / avgVol20 : 1.0;
	    if (volRatio >= 2.0 && close > open && close > (previousClose ?? 0))
	    {
		volScore += 8;
		reasons.Add($"[量價+8] 爆量長紅 (量能 {volRatio:F1} 倍)");
	    }
	    else if (volRatio >= 1.3 && close > open)
	    {
		volScore += 4;
		reasons.Add($"[量價+4] 量增價揚 ({volRatio:F1} 倍)");
	    }
	    else if (volRatio < 0.5)
	    {
		volScore -= 3;
		reasons.Add($"[量價-3] 量能萎縮 ({volRatio:F1} 倍)");
	    }

	    double bias20 = (currentPrice - latest.MA20) / latest.MA20;
	    if (bias20 > 0.15)
	    {
		volScore -= 8;
		reasons.Add($"[量價-8] 乖離率過高 ({bias20 * 100:F1}%)，追高風險大");
	    }
	    else if (bias20 < -0.15)
	    {
		volScore += 5;
		reasons.Add($"[量價+5] 乖離率過低 ({bias20 * 100:F1}%)，反彈機會");
	    }

	    // 總分計算
	    double finalScore = 50 + trendScore + momentumScore + chipScore + candlestickScore + volScore;
	    finalScore = Math.Max(0, Math.Min(100, finalScore));

	    reasons.Insert(0, $"【總分 {(int)Math.Round(finalScore)}】趨勢{trendScore:+0;-0;0} 動能{momentumScore:+0;-0;0} 籌碼{chipScore:+0;-0;0} 型態{candlestickScore:+0;-0;0} 量價{volScore:+0;-0;0}");

	    return new TrendRecommendationResult
	    {
		Score = (int)Math.Round(finalScore),
		Reasons = reasons
	    };
	}

	public static string GetAdvancedSuggestion(int score)
	{
	    if (score >= 85) return "強烈買入 ★★★";
	    if (score >= 70) return "買入 ★★";
	    if (score >= 55) return "偏多 ★";
	    if (score >= 45) return "中性（觀望）";
	    if (score >= 30) return "偏空 ☆";
	    if (score >= 15) return "賣出 ☆☆";
	    return "強烈賣出 ☆☆☆";
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
