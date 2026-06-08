using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockManager.Library
{
    public class PatternTag
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public double Score { get; set; }
        public bool IsRisk { get; set; }
        public bool IsBullish { get; set; }
    }

    public class TrendRecommendationResult
    {
        public int Score { get; set; }
        public List<string> Reasons { get; set; }
        public List<PatternTag> PatternTags { get; set; }
    }

    public class IntradayRecommendationResult
    {
        public string Action { get; set; }
        public List<string> Reasons { get; set; }
    }

    public static class TradingRecommendationLibrary
    {
        private const double PatternTagDisplayThreshold = 70.0;

        public static TrendRecommendationResult CalculateAdvancedRecommendation(
            List<CandleData> data,
            double currentPriceDouble,
            double? changePercent,
            double? previousClose = null,
            TwseT86History twseHistory = null,
            DateTime? analysisDate = null)
        {
            decimal currentPrice = (decimal)currentPriceDouble;

            if (data == null || data.Count < 20)
            {
                return new TrendRecommendationResult
                {
                    Score = 50,
                    Reasons = new List<string> { "數據不足：歷史資料少於 20 筆，無法計算技術指標。" },
                    PatternTags = new List<PatternTag>()
                };
            }

            var latest = data.Last();
            var prev = data[data.Count - 2];
            var reasons = new List<string>();
            var patternTags = new List<PatternTag>();

            double trendScore = 0, momentumScore = 0, chipScore = 0, candleScore = 0, volScore = 0;
            double Clamp(double val, double min, double max) => Math.Max(min, Math.Min(max, val));
            double cp = (double)currentPrice;

            // =====================================================================
            // --- 1. 趨勢類 ---
            // =====================================================================

            // MA5 / MA20 多空排列
            if (cp > latest.MA5 && latest.MA5 > latest.MA20)
            {
                trendScore += 10;
                reasons.Add($"[趨勢+10] 多頭排列：現價({cp:F2}) > MA5({latest.MA5:F2}) > MA20({latest.MA20:F2})");
            }
            else if (cp < latest.MA5 && latest.MA5 < latest.MA20)
            {
                trendScore -= 15;
                reasons.Add($"[趨勢-15] 空頭排列：現價({cp:F2}) < MA5({latest.MA5:F2}) < MA20({latest.MA20:F2})");
            }
            else
            {
                reasons.Add($"[趨勢±0] MA5({latest.MA5:F2}) / MA20({latest.MA20:F2}) 糾結，方向不明");
            }

            // MA20 斜率
            if (prev.MA20 > 0)
            {
                double ma20Slope = (latest.MA20 - prev.MA20) / prev.MA20;
                if (ma20Slope > 0.002)
                {
                    trendScore += 5;
                    reasons.Add($"[趨勢+5] MA20 斜率向上 ({ma20Slope * 100:F2}%)，中期多頭動能");
                }
                else if (ma20Slope < -0.002)
                {
                    trendScore -= 5;
                    reasons.Add($"[趨勢-5] MA20 斜率向下 ({ma20Slope * 100:F2}%)，中期空頭壓力");
                }
                else
                {
                    reasons.Add($"[趨勢±0] MA20 斜率平緩 ({ma20Slope * 100:F2}%)，趨勢不明確");
                }
            }

            // MA120 長期趨勢
            if (latest.MA120 > 0)
            {
                if (cp > latest.MA120 && latest.MA20 > latest.MA120)
                {
                    trendScore += 8;
                    reasons.Add($"[趨勢+8] 站上 MA120({latest.MA120:F2})，長多格局確立");
                }
                else if (cp < latest.MA120 && latest.MA20 < latest.MA120)
                {
                    trendScore -= 8;
                    reasons.Add($"[趨勢-8] 跌破 MA120({latest.MA120:F2})，長空格局確立");
                }
                else
                {
                    reasons.Add($"[趨勢±0] MA120({latest.MA120:F2}) 多空訊號混雜，觀望");
                }
            }

            // MA240 超長期趨勢
            if (latest.MA240 > 0)
            {
                if (cp > latest.MA240 && latest.MA120 > latest.MA240)
                {
                    trendScore += 6;
                    reasons.Add($"[趨勢+6] 站上 MA240({latest.MA240:F2}) 且 MA120 向上，超長多頭");
                }
                else if (cp < latest.MA240 && latest.MA120 < latest.MA240)
                {
                    trendScore -= 6;
                    reasons.Add($"[趨勢-6] 跌破 MA240({latest.MA240:F2}) 且 MA120 向下，超長空頭");
                }
                else
                {
                    reasons.Add($"[趨勢±0] MA240({latest.MA240:F2}) 方向不明");
                }
            }

            // 長期均線支撐 / 壓力 (MA120 & MA240)
            double[] longTermMAs = { latest.MA120, latest.MA240 };
            string[] maNames = { "MA120", "MA240" };
            for (int i = 0; i < longTermMAs.Length; i++)
            {
                double maValue = longTermMAs[i];
                if (maValue <= 0) continue;
                double dist = (cp - maValue) / maValue;
                if (Math.Abs(dist) <= 0.015)
                {
                    double prevMa = (i == 0) ? prev.MA120 : prev.MA240;
                    bool isMaUp = maValue > prevMa;
                    decimal range = latest.High - latest.Low;
                    bool isRebound = latest.Close > latest.Open ||
                                     (range > 0 && (latest.Close - latest.Low) / range > 0.6m);
                    if (isMaUp && isRebound)
                    {
                        trendScore += 10;
                        reasons.Add($"[趨勢+10] {maNames[i]}({maValue:F2}) 支撐有效：均線向上且股價止跌反彈");
                    }
                    else if (cp < maValue)
                    {
                        trendScore -= 6;
                        reasons.Add($"[趨勢-6] {maNames[i]}({maValue:F2}) 支撐失守：股價在均線下方 ({dist * 100:F1}%)");
                    }
                    else
                    {
                        reasons.Add($"[趨勢±0] 股價正測試 {maNames[i]}({maValue:F2}) 壓力/支撐");
                    }
                }
            }

            // 布林通道位置
            if (latest.BollingerUpper > 0 && latest.BollingerLower > 0)
            {
                double bbWidth = latest.BollingerUpper - latest.BollingerLower;
                if (bbWidth > 0)
                {
                    double bbPos = (cp - latest.BollingerLower) / bbWidth;
                    if (cp >= latest.BollingerUpper)
                    {
                        trendScore -= 6;
                        reasons.Add($"[趨勢-6] 觸及布林上軌({latest.BollingerUpper:F2})，短線過熱，回檔風險高");
                    }
                    else if (cp <= latest.BollingerLower)
                    {
                        trendScore += 6;
                        reasons.Add($"[趨勢+6] 觸及布林下軌({latest.BollingerLower:F2})，超賣區間，反彈機會");
                    }
                    else if (bbPos >= 0.8)
                    {
                        trendScore -= 2;
                        reasons.Add($"[趨勢-2] 接近布林上軌({latest.BollingerUpper:F2})，位置偏高({bbPos * 100:F0}%)，留意壓力");
                    }
                    else if (bbPos <= 0.2)
                    {
                        trendScore += 2;
                        reasons.Add($"[趨勢+2] 接近布林下軌({latest.BollingerLower:F2})，位置偏低({bbPos * 100:F0}%)，具支撐");
                    }
                    else
                    {
                        reasons.Add($"[趨勢±0] 布林通道中段({bbPos * 100:F0}%)，中軌({latest.BollingerMiddle:F2})");
                    }
                    // 布林帶寬縮窄（波動率低）
                    double bbWidthRatio = bbWidth / latest.BollingerMiddle;
                    if (bbWidthRatio < 0.04)
                    {
                        reasons.Add($"[趨勢★] 布林帶收窄 (帶寬{bbWidthRatio * 100:F1}%)，可能即將出現大波動");
                    }
                }
            }

            trendScore = Clamp(trendScore, -40, 40);

            // =====================================================================
            // --- 2. 動能類 ---
            // =====================================================================

            double macd = latest.MACD;
            double macdSignal = latest.MacdSignal;
            double macdHist = macd - macdSignal;
            double prevMacdHist = prev.MACD - prev.MacdSignal;
            double rsi = latest.RSI;

            // MACD 多空
            if (macd > macdSignal)
            {
                momentumScore += 10;
                reasons.Add($"[動能+10] MACD({macd:F4}) > DEA({macdSignal:F4})，多方控盤");
            }
            else
            {
                momentumScore -= 10;
                reasons.Add($"[動能-10] MACD({macd:F4}) < DEA({macdSignal:F4})，空方控盤");
            }

            // MACD 柱狀體動能
            if (macdHist > prevMacdHist && macdHist > 0)
            {
                momentumScore += 5;
                reasons.Add($"[動能+5] MACD 柱狀體擴大且為正({macdHist:F4})，多頭動能加速");
            }
            else if (macdHist < prevMacdHist && macdHist > 0)
            {
                momentumScore -= 3;
                reasons.Add($"[動能-3] MACD 柱狀體縮小({macdHist:F4})，多頭動能減弱");
            }
            else if (macdHist < prevMacdHist && macdHist < 0)
            {
                momentumScore -= 5;
                reasons.Add($"[動能-5] MACD 柱狀體負向擴大({macdHist:F4})，空頭動能加速");
            }
            else if (macdHist > prevMacdHist && macdHist < 0)
            {
                momentumScore += 3;
                reasons.Add($"[動能+3] MACD 柱狀體負向縮小({macdHist:F4})，空頭動能減弱");
            }

            // RSI 區間判斷
            if (rsi > 80)
            {
                momentumScore -= 8;
                reasons.Add($"[動能-8] RSI({rsi:F1}) 嚴重超買(>80)，高位回檔風險大");
            }
            else if (rsi > 70)
            {
                momentumScore -= 4;
                reasons.Add($"[動能-4] RSI({rsi:F1}) 偏高(>70)，短線過熱，注意追高風險");
            }
            else if (rsi < 20)
            {
                momentumScore += 8;
                reasons.Add($"[動能+8] RSI({rsi:F1}) 嚴重超賣(<20)，強烈反彈機會");
            }
            else if (rsi < 30)
            {
                momentumScore += 4;
                reasons.Add($"[動能+4] RSI({rsi:F1}) 偏低(<30)，具反彈空間");
            }
            else if (rsi >= 50 && rsi <= 70)
            {
                momentumScore += 2;
                reasons.Add($"[動能+2] RSI({rsi:F1}) 健康多頭區間(50~70)");
            }
            else
            {
                reasons.Add($"[動能±0] RSI({rsi:F1}) 中性區間");
            }

            momentumScore = Clamp(momentumScore, -25, 25);

            // =====================================================================
            // --- 3. 籌碼類 ---
            // =====================================================================

            if (twseHistory?.RecordsByDate != null)
            {
                var targetDate = (analysisDate ?? latest.Time).Date;
                var records = twseHistory.RecordsByDate.Values
                    .Where(x => x.TradeDate <= targetDate)
                    .OrderByDescending(x => x.TradeDate).Take(5).ToList();

                if (records.Count > 0 && latest.Volume > 0)
                {
                    var todayChip = records.First();
                    double vol = (double)latest.Volume;

                    // 外資買賣超
                    double fNet = (double)todayChip.ForeignNet;
                    double fRatio = fNet / vol;
                    if (fRatio > 0.05)
                    {
                        chipScore += 15;
                        reasons.Add($"[籌碼+15] 外資大力買超 (佔量 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio > 0.02)
                    {
                        chipScore += 8;
                        reasons.Add($"[籌碼+8] 外資買超 (佔量 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio < -0.05)
                    {
                        chipScore -= 15;
                        reasons.Add($"[籌碼-15] 外資大力賣超 (佔量 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio < -0.02)
                    {
                        chipScore -= 8;
                        reasons.Add($"[籌碼-8] 外資賣超 (佔量 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else
                    {
                        reasons.Add($"[籌碼±0] 外資動向中性 (佔量 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }

                    // 投信買賣超
                    double tNet = (double)todayChip.InvestmentTrustNet;
                    double tRatio = tNet / vol;
                    if (tRatio > 0.02)
                    {
                        chipScore += 8;
                        reasons.Add($"[籌碼+8] 投信積極買超 (佔量 {tRatio * 100:F1}%，{tNet:+0;-0} 張)");
                    }
                    else if (tRatio > 0.005)
                    {
                        chipScore += 4;
                        reasons.Add($"[籌碼+4] 投信小幅買超 (佔量 {tRatio * 100:F1}%，{tNet:+0;-0} 張)");
                    }
                    else if (tRatio < -0.02)
                    {
                        chipScore -= 6;
                        reasons.Add($"[籌碼-6] 投信賣超 (佔量 {tRatio * 100:F1}%，{tNet:+0;-0} 張)");
                    }
                    else
                    {
                        reasons.Add($"[籌碼±0] 投信動向中性 ({tNet:+0;-0} 張)");
                    }

                    // 投信連買/連賣天數
                    int itBuyDays = records.TakeWhile(r => r.InvestmentTrustNet > 0).Count();
                    int itSellDays = records.TakeWhile(r => r.InvestmentTrustNet < 0).Count();
                    if (itBuyDays >= 3)
                    {
                        chipScore += 8;
                        reasons.Add($"[籌碼+8] 投信連買 {itBuyDays} 日，持續佈局訊號");
                    }
                    else if (itBuyDays >= 2)
                    {
                        chipScore += 3;
                        reasons.Add($"[籌碼+3] 投信連買 {itBuyDays} 日");
                    }
                    else if (itSellDays >= 3)
                    {
                        chipScore -= 6;
                        reasons.Add($"[籌碼-6] 投信連賣 {itSellDays} 日，持續出脫訊號");
                    }

                    // 近5日三大法人合計買賣超
                    double sum5 = records.Sum(r => (double)r.ThreeMajorNet);
                    double avgVol = records.Count > 0 ? records.Average(r => (double)(r.ForeignNet + r.InvestmentTrustNet + r.DealerNet)) : 0;
                    if (sum5 > 0 && vol > 0)
                    {
                        double sum5Ratio = sum5 / (vol * records.Count);
                        if (sum5Ratio > 0.03)
                        {
                            chipScore += 8;
                            reasons.Add($"[籌碼+8] 近{records.Count}日法人合計買超 {sum5:+0;-0} 張 (日均佔量 {sum5Ratio * 100:F1}%)");
                        }
                        else if (sum5Ratio > 0.01)
                        {
                            chipScore += 3;
                            reasons.Add($"[籌碼+3] 近{records.Count}日法人合計買超 {sum5:+0;-0} 張");
                        }
                    }
                    else if (sum5 < 0 && vol > 0)
                    {
                        double sum5Ratio = sum5 / (vol * records.Count);
                        if (sum5Ratio < -0.03)
                        {
                            chipScore -= 8;
                            reasons.Add($"[籌碼-8] 近{records.Count}日法人合計賣超 {sum5:+0;-0} 張 (日均佔量 {sum5Ratio * 100:F1}%)");
                        }
                        else if (sum5Ratio < -0.01)
                        {
                            chipScore -= 3;
                            reasons.Add($"[籌碼-3] 近{records.Count}日法人合計賣超 {sum5:+0;-0} 張");
                        }
                    }
                }
            }
            chipScore = Clamp(chipScore, -30, 30);

            // =====================================================================
            // --- 4. K 線型態 + 量價 ---
            // =====================================================================

            double close = (double)latest.Close;
            double open = (double)latest.Open;
            double high = (double)latest.High;
            double low = (double)latest.Low;
            double body = Math.Abs(close - open);
            double kRange = Math.Max(0.0000001, high - low);
            double upperShadow = Math.Max(0, high - Math.Max(open, close));
            double lowerShadow = Math.Max(0, Math.Min(open, close) - low);
            double upperShadowRatio = upperShadow / kRange;
            double lowerShadowRatio = lowerShadow / kRange;
            double prevClose = previousClose ?? (double)prev.Close;
            double dailyReturn = prevClose != 0 ? (close - prevClose) / prevClose : 0;
            bool isRedCandle = close > open;
            bool isBlackCandle = close < open;

            double avgVol20 = data.Skip(Math.Max(0, data.Count - 20)).Average(x => (double)x.Volume);
            double curVol = (double)latest.Volume;
            double volRatio = avgVol20 > 0 ? curVol / avgVol20 : 1.0;

            if (volRatio >= 2.0 && close > prevClose)
            {
                volScore += 8;
                reasons.Add($"[量價+8] 爆量長紅 (量能 {volRatio:F1} 倍均量)，強勢突破");
            }
            else if (volRatio >= 1.3 && close > prevClose)
            {
                volScore += 4;
                reasons.Add($"[量價+4] 量增價揚 (量能 {volRatio:F1} 倍均量)，買氣擴增");
            }
            else if (volRatio >= 2.0 && close < prevClose)
            {
                volScore -= 8;
                reasons.Add($"[量價-8] 爆量長黑 (量能 {volRatio:F1} 倍均量)，強勢賣壓");
            }
            else if (volRatio < 0.5)
            {
                volScore -= 3;
                reasons.Add($"[量價-3] 量能萎縮 ({volRatio:F1} 倍均量)，市場觀望");
            }
            else
            {
                reasons.Add($"[量價±0] 量能正常 ({volRatio:F1} 倍均量)");
            }

            // MA20 乖離率
            double ma20val = latest.MA20;
            if (ma20val > 0)
            {
                double bias20 = (cp - ma20val) / ma20val;
                if (bias20 > 0.15)
                {
                    volScore -= 8;
                    reasons.Add($"[量價-8] MA20 乖離率過高 ({bias20 * 100:F1}%)，追高風險大");
                }
                else if (bias20 > 0.08)
                {
                    volScore -= 4;
                    reasons.Add($"[量價-4] MA20 乖離率偏高 ({bias20 * 100:F1}%)，短線注意");
                }
                else if (bias20 < -0.15)
                {
                    volScore += 6;
                    reasons.Add($"[量價+6] MA20 乖離率過低 ({bias20 * 100:F1}%)，超跌反彈空間大");
                }
                else if (bias20 < -0.08)
                {
                    volScore += 3;
                    reasons.Add($"[量價+3] MA20 乖離率偏低 ({bias20 * 100:F1}%)，具回測支撐價值");
                }
                else
                {
                    reasons.Add($"[量價±0] MA20 乖離率正常 ({bias20 * 100:F1}%)");
                }
            }

            var scoreBullishEngulfing = CalculateBullishEngulfingScore(data, volRatio);
            var scoreBearishEngulfing = CalculateBearishEngulfingScore(data, volRatio);
            var scorePiercingLine = CalculatePiercingLineScore(data, volRatio);
            var scoreDarkCloudCover = CalculateDarkCloudCoverScore(data, volRatio);
            var scoreBullishHarami = CalculateBullishHaramiScore(data, volRatio);
            var scoreBearishHarami = CalculateBearishHaramiScore(data, volRatio);
            var scoreMorningStar = CalculateMorningStarScore(data, volRatio);
            var scoreEveningStar = CalculateEveningStarScore(data, volRatio);
            var scoreThreeWhiteSoldiers = CalculateThreeWhiteSoldiersScore(data, volRatio);
            var scoreThreeBlackCrows = CalculateThreeBlackCrowsScore(data, volRatio);
            var scoreRisingThreeMethods = CalculateRisingThreeMethodsScore(data, volRatio);
            var scoreFallingThreeMethods = CalculateFallingThreeMethodsScore(data, volRatio);

            AddPatternTag(patternTags, reasons, "bullish_engulfing", "多頭吞噬", scoreBullishEngulfing, false);
            AddPatternTag(patternTags, reasons, "bearish_engulfing", "空頭吞噬", scoreBearishEngulfing, true);
            AddPatternTag(patternTags, reasons, "piercing_line", "穿刺線", scorePiercingLine, false);
            AddPatternTag(patternTags, reasons, "dark_cloud_cover", "烏雲蓋頂", scoreDarkCloudCover, true);
            AddPatternTag(patternTags, reasons, "bullish_harami", "多頭孕育", scoreBullishHarami, false);
            AddPatternTag(patternTags, reasons, "bearish_harami", "空頭孕育", scoreBearishHarami, true);
            AddPatternTag(patternTags, reasons, "morning_star", "晨星", scoreMorningStar, false);
            AddPatternTag(patternTags, reasons, "evening_star", "黃昏星", scoreEveningStar, true);
            AddPatternTag(patternTags, reasons, "three_white_soldiers", "紅三兵", scoreThreeWhiteSoldiers, false);
            AddPatternTag(patternTags, reasons, "three_black_crows", "黑三鴉", scoreThreeBlackCrows, true);
            AddPatternTag(patternTags, reasons, "rising_three_methods", "上升三法", scoreRisingThreeMethods, false);
            AddPatternTag(patternTags, reasons, "falling_three_methods", "下降三法", scoreFallingThreeMethods, true);

            var bullishScores = new[] { scoreBullishEngulfing, scorePiercingLine, scoreBullishHarami, scoreMorningStar, scoreThreeWhiteSoldiers, scoreRisingThreeMethods }
                .OrderByDescending(x => x)
                .ToList();
            var bearishScores = new[] { scoreBearishEngulfing, scoreDarkCloudCover, scoreBearishHarami, scoreEveningStar, scoreThreeBlackCrows, scoreFallingThreeMethods }
                .OrderByDescending(x => x)
                .ToList();

            var bullishComposite = bullishScores[0] * 0.6 + bullishScores[1] * 0.4;
            var bearishComposite = bearishScores[0] * 0.6 + bearishScores[1] * 0.4;

            var patternContribution = Clamp((bullishComposite - bearishComposite) / 4.0, -20, 20);
            reasons.Add($"[型態評分{patternContribution:+0.0;-0.0;0.0}] 多吞:{scoreBullishEngulfing:F0} 空吞:{scoreBearishEngulfing:F0} 穿刺:{scorePiercingLine:F0} 烏雲:{scoreDarkCloudCover:F0} 多孕:{scoreBullishHarami:F0} 空孕:{scoreBearishHarami:F0} 晨星:{scoreMorningStar:F0} 黃昏:{scoreEveningStar:F0} 紅三兵:{scoreThreeWhiteSoldiers:F0} 黑三鴉:{scoreThreeBlackCrows:F0} 上升三法:{scoreRisingThreeMethods:F0} 下降三法:{scoreFallingThreeMethods:F0}");

            double extraScore = Clamp(candleScore + volScore + patternContribution, -20, 20);

            // =====================================================================
            // --- 6. 總結 ---
            // =====================================================================

            double finalScore = 50 + trendScore + momentumScore + chipScore + extraScore;
            finalScore = Clamp(finalScore, 0, 100);

            reasons.Insert(0, $"【總分 {(int)Math.Round(finalScore)}】 趨勢{trendScore:+0;-0;0}  動能{momentumScore:+0;-0;0}  籌碼{chipScore:+0;-0;0}  型態+量價{extraScore:+0;-0;0}");

            return new TrendRecommendationResult
            {
                Score = (int)Math.Round(finalScore),
                Reasons = reasons,
                PatternTags = patternTags
            };
        }

        private static void AddPatternTag(List<PatternTag> tags, List<string> reasons, string key, string label, double score, bool isRisk)
        {
            if (score < PatternTagDisplayThreshold)
            {
                return;
            }

            tags.Add(new PatternTag
            {
                Key = key,
                Label = label,
                Score = score,
                IsRisk = isRisk,
                IsBullish = !isRisk
            });

            reasons.Add(isRisk
                ? $"[風險標籤] {label} ({score:F0})"
                : $"[形態標籤] {label} ({score:F0})");
        }

        private static double CalculateBullishEngulfingScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var engulf = IsBlack(p) && IsRed(c) && c.Open <= p.Close && c.Close >= p.Open;
            if (!engulf) return 0;
            var score = 75d;
            if (c.MA20 > 0 && (double)c.Close < c.MA20) score += 15;
            if (volRatio >= 1.2) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateBearishEngulfingScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var engulf = IsRed(p) && IsBlack(c) && c.Open >= p.Close && c.Close <= p.Open;
            if (!engulf) return 0;
            var score = 75d;
            if (c.MA20 > 0 && (double)c.Close > c.MA20) score += 15;
            if (volRatio >= 1.2) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculatePiercingLineScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var pMid = ((double)p.Open + (double)p.Close) / 2.0;
            var valid = IsLongBlack(p) && IsRed(c) && c.Open < p.Close && c.Close > (decimal)pMid;
            if (!valid) return 0;
            var score = 78d;
            if (c.MA20 > 0 && (double)c.Close < c.MA20) score += 12;
            if (volRatio >= 1.2) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateDarkCloudCoverScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var pMid = ((double)p.Open + (double)p.Close) / 2.0;
            var valid = IsLongRed(p) && IsBlack(c) && c.Open > p.Close && c.Close < (decimal)pMid;
            if (!valid) return 0;
            var score = 78d;
            if (c.MA20 > 0 && (double)c.Close > c.MA20) score += 12;
            if (volRatio >= 1.2) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateBullishHaramiScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var pLowBody = Math.Min((double)p.Open, (double)p.Close);
            var pHighBody = Math.Max((double)p.Open, (double)p.Close);
            var cLowBody = Math.Min((double)c.Open, (double)c.Close);
            var cHighBody = Math.Max((double)c.Open, (double)c.Close);
            var inside = IsLongBlack(p) && IsSmallBody(c) && cLowBody >= pLowBody && cHighBody <= pHighBody;
            if (!inside) return 0;
            var score = 70d;
            if (c.MA20 > 0 && (double)c.Close < c.MA20) score += 15;
            if (volRatio >= 1.1) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateBearishHaramiScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 2) return 0;
            var p = data[data.Count - 2];
            var c = data[data.Count - 1];
            var pLowBody = Math.Min((double)p.Open, (double)p.Close);
            var pHighBody = Math.Max((double)p.Open, (double)p.Close);
            var cLowBody = Math.Min((double)c.Open, (double)c.Close);
            var cHighBody = Math.Max((double)c.Open, (double)c.Close);
            var inside = IsLongRed(p) && IsSmallBody(c) && cLowBody >= pLowBody && cHighBody <= pHighBody;
            if (!inside) return 0;
            var score = 70d;
            if (c.MA20 > 0 && (double)c.Close > c.MA20) score += 15;
            if (volRatio >= 1.1) score += 10;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateMorningStarScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 3) return 0;
            var a = data[data.Count - 3];
            var b = data[data.Count - 2];
            var c = data[data.Count - 1];
            var aMid = ((double)a.Open + (double)a.Close) / 2.0;
            var valid = IsLongBlack(a) && IsSmallBody(b) && IsLongRed(c) && c.Close > (decimal)aMid;
            if (!valid) return 0;
            var score = 85d;
            if (c.MA20 > 0 && (double)c.Close < c.MA20) score += 10;
            if (volRatio >= 1.2) score += 5;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateEveningStarScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 3) return 0;
            var a = data[data.Count - 3];
            var b = data[data.Count - 2];
            var c = data[data.Count - 1];
            var aMid = ((double)a.Open + (double)a.Close) / 2.0;
            var valid = IsLongRed(a) && IsSmallBody(b) && IsLongBlack(c) && c.Close < (decimal)aMid;
            if (!valid) return 0;
            var score = 85d;
            if (c.MA20 > 0 && (double)c.Close > c.MA20) score += 10;
            if (volRatio >= 1.2) score += 5;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateThreeWhiteSoldiersScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 3) return 0;
            var a = data[data.Count - 3];
            var b = data[data.Count - 2];
            var c = data[data.Count - 1];
            var valid = IsRed(a) && IsRed(b) && IsRed(c) && a.Close < b.Close && b.Close < c.Close;
            if (!valid) return 0;
            var score = 82d;
            if (volRatio >= 1.2) score += 10;
            if (c.MA20 > 0 && (double)c.Close > c.MA20) score += 8;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateThreeBlackCrowsScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 3) return 0;
            var a = data[data.Count - 3];
            var b = data[data.Count - 2];
            var c = data[data.Count - 1];
            var valid = IsBlack(a) && IsBlack(b) && IsBlack(c) && a.Close > b.Close && b.Close > c.Close;
            if (!valid) return 0;
            var score = 82d;
            if (volRatio >= 1.2) score += 10;
            if (c.MA20 > 0 && (double)c.Close < c.MA20) score += 8;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateRisingThreeMethodsScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 5) return 0;
            var a = data[data.Count - 5];
            var b = data[data.Count - 4];
            var c = data[data.Count - 3];
            var d = data[data.Count - 2];
            var e = data[data.Count - 1];

            var rangeLow = (double)Math.Min(a.Open, a.Close);
            var rangeHigh = (double)Math.Max(a.Open, a.Close);
            var middleInRange = new[] { b, c, d }.All(x => Math.Min((double)x.Open, (double)x.Close) >= rangeLow && Math.Max((double)x.Open, (double)x.Close) <= rangeHigh);
            var valid = IsLongRed(a) && IsSmallBody(b) && IsSmallBody(c) && IsSmallBody(d) && IsLongRed(e) && middleInRange && e.Close > a.Close;
            if (!valid) return 0;

            var score = 84d;
            if (volRatio >= 1.2) score += 8;
            if (e.MA20 > 0 && (double)e.Close > e.MA20) score += 8;
            return ClampValue(score, 0, 100);
        }

        private static double CalculateFallingThreeMethodsScore(List<CandleData> data, double volRatio)
        {
            if (data == null || data.Count < 5) return 0;
            var a = data[data.Count - 5];
            var b = data[data.Count - 4];
            var c = data[data.Count - 3];
            var d = data[data.Count - 2];
            var e = data[data.Count - 1];

            var rangeLow = (double)Math.Min(a.Open, a.Close);
            var rangeHigh = (double)Math.Max(a.Open, a.Close);
            var middleInRange = new[] { b, c, d }.All(x => Math.Min((double)x.Open, (double)x.Close) >= rangeLow && Math.Max((double)x.Open, (double)x.Close) <= rangeHigh);
            var valid = IsLongBlack(a) && IsSmallBody(b) && IsSmallBody(c) && IsSmallBody(d) && IsLongBlack(e) && middleInRange && e.Close < a.Close;
            if (!valid) return 0;

            var score = 84d;
            if (volRatio >= 1.2) score += 8;
            if (e.MA20 > 0 && (double)e.Close < e.MA20) score += 8;
            return ClampValue(score, 0, 100);
        }

        private static bool IsRed(CandleData c) => c != null && c.Close > c.Open;
        private static bool IsBlack(CandleData c) => c != null && c.Close < c.Open;

        private static double GetBodyRatio(CandleData c)
        {
            if (c == null)
            {
                return 0;
            }

            var range = Math.Max(0.0000001, (double)(c.High - c.Low));
            return Math.Abs((double)(c.Close - c.Open)) / range;
        }

        private static bool IsLongBody(CandleData c) => GetBodyRatio(c) >= 0.6;
        private static bool IsSmallBody(CandleData c) => GetBodyRatio(c) <= 0.35;
        private static bool IsLongRed(CandleData c) => IsRed(c) && IsLongBody(c);
        private static bool IsLongBlack(CandleData c) => IsBlack(c) && IsLongBody(c);

        private static double CalculateWashPlateScore(double volumeRatio, double lowerShadowRatio, double dailyReturn)
        {
            double score = 0;

            if (volumeRatio <= 0.5)
            {
                score += 40;
            }
            else if (volumeRatio <= 0.7)
            {
                score += 25;
            }
            else if (volumeRatio <= 1.0)
            {
                score += 10;
            }

            score += LinearScore(lowerShadowRatio, 0.2, 0.5, 40);

            var absRet = Math.Abs(dailyReturn);
            if (absRet <= 0.005)
            {
                score += 20;
            }
            else if (absRet <= 0.01)
            {
                score += 10;
            }

            return ClampValue(score, 0, 100);
        }

        private static double CalculateHighTurnoverScore(double volumeRatio, double upperShadowRatio, double dailyReturn, bool isRedCandle)
        {
            double score = 0;

            if (volumeRatio >= 2.0)
            {
                score += 40;
            }
            else if (volumeRatio >= 1.5)
            {
                score += 25;
            }
            else if (volumeRatio >= 1.2)
            {
                score += 10;
            }

            score += LinearScore(upperShadowRatio, 0.1, 0.4, 30);

            if (isRedCandle)
            {
                if (dailyReturn >= 0.03)
                {
                    score += 30;
                }
                else if (dailyReturn >= 0.01)
                {
                    score += 15;
                }
            }

            return ClampValue(score, 0, 100);
        }

        private static double CalculateThreeSoldiersScore(List<CandleData> data)
        {
            if (data == null || data.Count < 4)
            {
                return 0;
            }

            var d0 = data[data.Count - 1];
            var d1 = data[data.Count - 2];
            var d2 = data[data.Count - 3];
            var d3 = data[data.Count - 4];

            var isContinuous =
                d2.Close > d2.Open && d2.Close > d3.Close &&
                d1.Close > d1.Open && d1.Close > d2.Close &&
                d0.Close > d0.Open && d0.Close > d1.Close;

            if (!isContinuous)
            {
                return 0;
            }

            double score = 40;

            if (d2.Close < d1.Close && d1.Close < d0.Close)
            {
                score += 30;
            }

            var volGrow = d2.Volume < d1.Volume && d1.Volume < d0.Volume;
            if (volGrow)
            {
                score += 30;
            }
            else
            {
                var i0 = data.Count - 1;
                var i1 = data.Count - 2;
                var i2 = data.Count - 3;
                var d0Above = (double)d0.Volume > AverageVolume(data, i0, 5);
                var d1Above = (double)d1.Volume > AverageVolume(data, i1, 5);
                var d2Above = (double)d2.Volume > AverageVolume(data, i2, 5);
                if (d0Above && d1Above && d2Above)
                {
                    score += 15;
                }
            }

            return ClampValue(score, 0, 100);
        }

        private static double CalculateBearTrapScore(List<CandleData> data, double volumeRatio)
        {
            if (data == null || data.Count < 21)
            {
                return 0;
            }

            var latest = data[data.Count - 1];
            var lookback = data.Skip(data.Count - 21).Take(20).ToList();
            var low20 = lookback.Min(x => (double)x.Low);

            var breaksLow = (double)latest.Low < low20;
            if (!breaksLow)
            {
                return 0;
            }

            double score = 40;
            var recovers = (double)latest.Close > low20;
            if (recovers)
            {
                score += 40;
                if (volumeRatio >= 1.5)
                {
                    score += 20;
                }
                else if (volumeRatio >= 1.2)
                {
                    score += 10;
                }
            }

            return ClampValue(score, 0, 100);
        }

        private static double CalculateGappingUpScore(List<CandleData> data, double volumeRatio)
        {
            if (data == null || data.Count < 2)
            {
                return 0;
            }

            var latest = data[data.Count - 1];
            var prev = data[data.Count - 2];
            var prevClose = (double)prev.Close;
            if (prevClose <= 0)
            {
                return 0;
            }

            var gapUp = (double)latest.Low > (double)prev.High;
            if (!gapUp)
            {
                return 0;
            }

            double score = 0;
            var gapPct = ((double)latest.Low - (double)prev.High) / prevClose;
            score += ClampValue(Math.Floor(gapPct / 0.01) * 10, 0, 50);

            var isRed = latest.Close > latest.Open;
            var isBlack = latest.Close < latest.Open;
            var kRange = Math.Max(0.0000001, (double)latest.High - (double)latest.Low);
            var lowerShadow = Math.Max(0, Math.Min((double)latest.Open, (double)latest.Close) - (double)latest.Low);
            var lowerShadowRatio = lowerShadow / kRange;

            if (isRed)
            {
                score += 30;
            }
            else if (isBlack && lowerShadowRatio >= 0.4)
            {
                score += 15;
            }

            if (volumeRatio >= 1.5)
            {
                score += 20;
            }
            else if (volumeRatio >= 1.1)
            {
                score += 10;
            }

            return ClampValue(score, 0, 100);
        }

        private static double CalculateDarkCloudScore(List<CandleData> data, double volumeRatio)
        {
            if (data == null || data.Count < 2)
            {
                return 0;
            }

            var latest = data[data.Count - 1];
            var prev = data[data.Count - 2];
            double score = 0;

            var isBlack = latest.Close < latest.Open;
            var prevIsRed = prev.Close > prev.Open;
            var prevBodyMid = ((double)prev.Open + (double)prev.Close) / 2.0;
            if (isBlack && prevIsRed && (double)latest.Close < prevBodyMid)
            {
                score += 40;
            }

            if (latest.MA20 > 0)
            {
                var bias20 = ((double)latest.Close - latest.MA20) / latest.MA20;
                if (bias20 >= 0.08)
                {
                    score += 40;
                }
                else if (bias20 >= 0.05)
                {
                    score += 20;
                }
            }

            if (isBlack && volumeRatio >= 1.8)
            {
                score += 20;
            }

            return ClampValue(score, 0, 100);
        }

        private static double LinearScore(double x, double start, double end, double maxScore)
        {
            if (x <= start)
            {
                return 0;
            }

            if (x >= end)
            {
                return maxScore;
            }

            return (x - start) / (end - start) * maxScore;
        }

        private static double ClampValue(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static double AverageVolume(IReadOnlyList<CandleData> data, int index, int period)
        {
            var start = Math.Max(0, index - period + 1);
            var count = index - start + 1;
            if (count <= 0)
            {
                return 0;
            }

            return data.Skip(start).Take(count).Average(x => (double)x.Volume);
        }

        public static IntradayRecommendationResult CalculateIntradayRecommendation(
            List<CandleData> data,
            double currentPriceDouble)
        {
            decimal currentPrice = (decimal)currentPriceDouble;

            if (data == null || data.Count < 3)
            {
                return new IntradayRecommendationResult
                {
                    Action = "觀望",
                    Reasons = new List<string> { "數據不足：當沖需要至少 3 根 K 線資料。" }
                };
            }

            var latest = data.Last();
            var prev1 = data[data.Count - 2];
            var prev2 = data[data.Count - 3];
            var reasons = new List<string>();

            double cp = (double)currentPrice;
            double vwap = (double)data.Sum(x => x.Close * x.Volume) / (double)Math.Max(1, data.Sum(x => x.Volume));

            int trendScore = 0;

            // 均價線判斷
            if (cp > vwap)
            {
                trendScore += 1;
                reasons.Add($"現價站上均價線 ({vwap:F2})，偏多。");
            }
            else if (cp < vwap)
            {
                trendScore -= 1;
                reasons.Add($"現價落於均價線 ({vwap:F2}) 之下，偏空。");
            }

            // K線形態與突破
            if (latest.Close > prev1.High && prev1.Close > prev2.High)
            {
                trendScore += 2;
                reasons.Add("連兩根K穿頭，短多動能強。");
            }
            else if (latest.Close < prev1.Low && prev1.Close < prev2.Low)
            {
                trendScore -= 2;
                reasons.Add("連兩根K破底，短空壓力大。");
            }

            // 爆量判斷 (當前K線量大於前兩根)
            if (latest.Volume > prev1.Volume * 1.5 && latest.Volume > prev2.Volume * 1.5)
            {
                if (latest.Close > latest.Open)
                {
                    trendScore += 1;
                    reasons.Add("爆量收紅，買盤積極。");
                }
                else if (latest.Close < latest.Open)
                {
                    trendScore -= 1;
                    reasons.Add("爆量收黑，賣壓沉重。");
                }
            }

            string action = "觀望";
            if (trendScore >= 3) action = "強烈做多";
            else if (trendScore > 0) action = "偏多操作";
            else if (trendScore <= -3) action = "強烈做空";
            else if (trendScore < 0) action = "偏空操作";

            return new IntradayRecommendationResult
            {
                Action = action,
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
