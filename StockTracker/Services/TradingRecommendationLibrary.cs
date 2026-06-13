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
        public int CrashRiskScore { get; set; }
        public string GlobalDecision { get; set; }
        public List<string> Reasons { get; set; }
        public List<PatternTag> PatternTags { get; set; }
    }

    public class IntradayRecommendationResult
    {
        public string Action { get; set; }
        public List<string> Reasons { get; set; }
    }

    internal class PatternAdjustmentResult
    {
        public string Name { get; set; }
        public double BaseScore { get; set; }
        public double AdjustedScore { get; set; }
        public double ContributionPoints { get; set; }
        public string FactorsText { get; set; }
    }

    public static class TradingRecommendationLibrary
    {
        private const double PatternTagDisplayThreshold = 60.0;

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
                    CrashRiskScore = 0,
                    GlobalDecision = "NEUTRAL",
                    Reasons = new List<string> { "數據不足：歷史資料少於 20 筆，無法計算技術指標。" },
                    PatternTags = new List<PatternTag>()
                };
            }

            var latest = data.Last();
            var prev = data[data.Count - 2];
            var reasons = new List<string>();
            var patternTags = new List<PatternTag>();

            // 雙軌制核心累加變數
            double opportunityRaw = 0;
            double crashRiskRaw = 0;

            double Clamp(double val, double min, double max) => Math.Max(min, Math.Min(max, val));
            void AddOpportunity(double value) => opportunityRaw += Math.Max(0, value);
            void AddCrashRisk(double value) => crashRiskRaw += Math.Max(0, value);

            double cp = (double)currentPrice;

            // =====================================================================
            // --- 1. 趨勢類 ---
            // =====================================================================

            // MA5 / MA20 多空排列
            if (cp > latest.MA5 && latest.MA5 > latest.MA20)
            {
                AddOpportunity(10);
                reasons.Add($"[趨勢+10] 多頭排列：現價({cp:F2}) > MA5({latest.MA5:F2}) > MA20({latest.MA20:F2})");
            }
            else if (cp < latest.MA5 && latest.MA5 < latest.MA20)
            {
                AddCrashRisk(15); // 改為增加風險值
                reasons.Add($"[風險+15] 空頭排列：現價({cp:F2}) < MA5({latest.MA5:F2}) < MA20({latest.MA20:F2})");
            }
            else
            {
                reasons.Add($"[趨勢±0] MA5/MA20 糾結，方向不明");
            }

            // MA20 斜率
            if (prev.MA20 > 0)
            {
                double ma20Slope = (latest.MA20 - prev.MA20) / prev.MA20;
                if (ma20Slope > 0.002)
                {
                    AddOpportunity(5);
                    reasons.Add($"[趨勢+5] MA20 斜率向上 ({ma20Slope * 100:F2}%)，中期多頭動能");
                }
                else if (ma20Slope < -0.002)
                {
                    AddCrashRisk(10); // 改為增加風險值
                    reasons.Add($"[風險+10] MA20 斜率向下 ({ma20Slope * 100:F2}%)，中期空頭壓力");
                }
            }

            // MA120 長期趨勢
            if (latest.MA120 > 0)
            {
                if (cp > latest.MA120 && latest.MA20 > latest.MA120)
                {
                    AddOpportunity(8);
                    reasons.Add($"[趨勢+8] 站上 MA120({latest.MA120:F2})，長多格局確立");
                }
                else if (cp < latest.MA120 && latest.MA20 < latest.MA120)
                {
                    AddCrashRisk(10); // 改為增加風險值
                    reasons.Add($"[風險+10] 跌破 MA120({latest.MA120:F2})，長空格局確立");
                }
            }

            // MA240 超長期趨勢
            if (latest.MA240 > 0)
            {
                if (cp > latest.MA240 && latest.MA120 > latest.MA240)
                {
                    AddOpportunity(6);
                    reasons.Add($"[趨勢+6] 站上 MA240({latest.MA240:F2}) 且 MA120 向上，超長多頭");
                }
                else if (cp < latest.MA240 && latest.MA120 < latest.MA240)
                {
                    AddCrashRisk(8); // 改為增加風險值
                    reasons.Add($"[風險+8] 跌破 MA240({latest.MA240:F2}) 且 MA120 向下，超長空頭");
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
                    bool isRebound = latest.Close > latest.Open || (range > 0 && (latest.Close - latest.Low) / range > 0.6m);

                    if (isMaUp && isRebound)
                    {
                        AddOpportunity(10);
                        reasons.Add($"[趨勢+10] {maNames[i]}({maValue:F2}) 支撐有效：均線向上且股價止跌反彈");
                    }
                    else if (cp < maValue)
                    {
                        AddCrashRisk(10); // 改為增加風險值
                        reasons.Add($"[風險+10] {maNames[i]}({maValue:F2}) 支撐失守：股價在均線下方 ({dist * 100:F1}%)");
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
                        AddCrashRisk(20); // 觸及上軌：純風險
                        reasons.Add($"[風險+20] 觸及布林上軌({latest.BollingerUpper:F2})，短線過熱，回檔風險高");
                    }
                    else if (cp <= latest.BollingerLower)
                    {
                        AddOpportunity(8); // 觸及下軌：純機會
                        reasons.Add($"[趨勢+8] 觸及布林下軌({latest.BollingerLower:F2})，超賣區間，具反彈機會");
                    }
                    else if (bbPos >= 0.8)
                    {
                        AddCrashRisk(8); // 接近上軌：增加風險
                        reasons.Add($"[風險+8] 接近布林上軌，位置偏高({bbPos * 100:F0}%)，留意高檔壓力");
                    }
                    else if (bbPos <= 0.2)
                    {
                        AddOpportunity(3); // 接近下軌：增加機會
                        reasons.Add($"[趨勢+3] 接近布林下軌，位置偏低({bbPos * 100:F0}%)，具支撐");
                    }

                    double bbWidthRatio = bbWidth / latest.BollingerMiddle;
                    if (bbWidthRatio < 0.04)
                    {
                        reasons.Add($"[趨勢★] 布林帶收窄 (帶寬{bbWidthRatio * 100:F1}%)，波動率低，可能即將變盤");
                    }
                }
            }

            // =====================================================================
            // --- 2. 動能類 ---
            // =====================================================================

            double macd = latest.MACD;
            double macdSignal = latest.MacdSignal;
            double macdHist = macd - macdSignal;
            double prevMacdHist = prev.MACD - prev.MacdSignal;
            double rsi = latest.RSI;

            // MACD 多空與柱狀體
            if (macd > macdSignal)
            {
                AddOpportunity(10);
                if (macdHist > prevMacdHist && macdHist > 0)
                {
                    AddOpportunity(5);
                    reasons.Add($"[動能+15] MACD多方控盤，且柱狀體擴大為正({macdHist:F4})，多頭加速");
                }
                else
                {
                    reasons.Add($"[動能+10] MACD多方控盤，但柱狀體開始收斂");
                }
            }
            else
            {
                AddCrashRisk(12); // MACD 空方控盤增加風險
                if (macdHist < prevMacdHist && macdHist < 0)
                {
                    AddCrashRisk(6);
                    reasons.Add($"[風險+18] MACD空方控盤，且紅柱/綠柱負向擴大({macdHist:F4})，空頭加速");
                }
                else
                {
                    reasons.Add($"[風險+12] MACD空方控盤，空頭動能稍緩");
                }
            }

            // RSI 區間判斷
            if (rsi > 80)
            {
                AddCrashRisk(25); // 嚴重的超買回檔風險
                reasons.Add($"[風險+25] RSI({rsi:F1}) 嚴重超買(>80)，高位反轉風險極大");
            }
            else if (rsi > 70)
            {
                AddCrashRisk(12);
                reasons.Add($"[風險+12] RSI({rsi:F1}) 偏高(>70)，短線過熱，注意追高風險");
            }
            else if (rsi < 20)
            {
                AddOpportunity(12);
                reasons.Add($"[動能+12] RSI({rsi:F1}) 嚴重超賣(<20)，醞釀強烈反彈");
            }
            else if (rsi < 30)
            {
                AddOpportunity(6);
                reasons.Add($"[動能+6] RSI({rsi:F1}) 偏低(<30)，具跌深反彈空間");
            }
            else if (rsi >= 50 && rsi <= 70)
            {
                AddOpportunity(4);
                reasons.Add($"[動能+4] RSI({rsi:F1}) 位於積極多頭健康區間");
            }

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
                        AddOpportunity(15);
                        reasons.Add($"[籌碼+15] 外資大舉買超 (佔比 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio > 0.02)
                    {
                        AddOpportunity(8);
                        reasons.Add($"[籌碼+8] 外資買超 (佔比 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio < -0.05)
                    {
                        AddCrashRisk(30); // 外資大出貨
                        reasons.Add($"[風險+30] 外資大舉賣超 (佔比 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }
                    else if (fRatio < -0.02)
                    {
                        AddCrashRisk(15);
                        reasons.Add($"[風險+15] 外資賣超 (佔比 {fRatio * 100:F1}%，{fNet:+0;-0} 張)");
                    }

                    // 投信買賣超
                    double tNet = (double)todayChip.InvestmentTrustNet;
                    double tRatio = tNet / vol;
                    if (tRatio > 0.02)
                    {
                        AddOpportunity(8);
                        reasons.Add($"[籌碼+8] 投信積極買超 (佔比 {tRatio * 100:F1}%，{tNet:+0;-0} 張)");
                    }
                    else if (tRatio < -0.02)
                    {
                        AddCrashRisk(15);
                        reasons.Add($"[風險+15] 投信賣超 (佔比 {tRatio * 100:F1}%，{tNet:+0;-0} 張)");
                    }

                    // 投信連買/連賣
                    int itBuyDays = records.TakeWhile(r => r.InvestmentTrustNet > 0).Count();
                    int itSellDays = records.TakeWhile(r => r.InvestmentTrustNet < 0).Count();
                    if (itBuyDays >= 3)
                    {
                        AddOpportunity(8);
                        reasons.Add($"[籌碼+8] 投信連買 {itBuyDays} 日，法人認養作帳訊號");
                    }
                    else if (itSellDays >= 3)
                    {
                        AddCrashRisk(15);
                        reasons.Add($"[風險+15] 投信連賣 {itSellDays} 日，法人持續出脫");
                    }

                    // 近5日三大法人合計買賣超
                    double sum5 = records.Sum(r => (double)r.ThreeMajorNet);
                    if (sum5 > 0 && vol > 0)
                    {
                        double sum5Ratio = sum5 / (vol * records.Count);
                        if (sum5Ratio > 0.03)
                        {
                            AddOpportunity(8);
                            reasons.Add($"[籌碼+8] 近5日法人合計買超 {sum5:+0;-0} 張 (日均佔比 {sum5Ratio * 100:F1}%)");
                        }
                    }
                    else if (sum5 < 0 && vol > 0)
                    {
                        double sum5Ratio = sum5 / (vol * records.Count);
                        if (sum5Ratio < -0.03)
                        {
                            AddCrashRisk(15);
                            reasons.Add($"[風險+15] 近5日法人合計賣超 {sum5:+0;-0} 張 (日均佔比 {sum5Ratio * 100:F1}%)");
                        }
                    }
                }
            }

            // =====================================================================
            // --- 4. K 線型態 + 量價 ---
            // =====================================================================

            double close = (double)latest.Close;
            double prevClose = previousClose ?? (double)prev.Close;
            double avgVol20 = data.Skip(Math.Max(0, data.Count - 20)).Average(x => (double)x.Volume);
            double curVol = (double)latest.Volume;
            double volRatio = avgVol20 > 0 ? curVol / avgVol20 : 1.0;

            // 量價不對稱拆解
            if (volRatio >= 2.0 && close > prevClose)
            {
                AddOpportunity(12);
                reasons.Add($"[量價+12] 爆量長紅 (量能 {volRatio:F1} 倍均量)，帶量突破主力發動");
            }
            else if (volRatio >= 1.3 && close > prevClose)
            {
                AddOpportunity(6);
                reasons.Add($"[量價+6] 量增價揚 (量能 {volRatio:F1} 倍均量)，追價意願高");
            }
            else if (volRatio >= 2.0 && close < prevClose)
            {
                AddCrashRisk(25); // 爆量出貨長黑
                reasons.Add($"[風險+25] 爆量長黑 (量能 {volRatio:F1} 倍均量)，高檔主力出貨訊號");
            }
            else if (volRatio < 0.5)
            {
                AddCrashRisk(3); // 量能窒息，缺乏攻擊動能
                reasons.Add($"[風險+3] 量能萎縮 ({volRatio:F1} 倍均量)，買盤觀望防守性差");
            }

            // MA20 乖離率
            double ma20val = latest.MA20;
            if (ma20val > 0)
            {
                double bias20 = (cp - ma20val) / ma20val;
                if (bias20 > 0.15)
                {
                    AddCrashRisk(30); // 正乖離過高：純風險
                    reasons.Add($"[風險+30] MA20 正乖離率過高 ({bias20 * 100:F1}%)，短線隨時可能大修正");
                }
                else if (bias20 > 0.08)
                {
                    AddCrashRisk(15);
                    reasons.Add($"[風險+15] MA20 正乖離率偏高 ({bias20 * 100:F1}%)，注意高檔獲利了結賣壓");
                }
                else if (bias20 < -0.15)
                {
                    AddOpportunity(10); // 負乖離過大：反彈機會
                    reasons.Add($"[量價+10] MA20 負乖離過大 ({bias20 * 100:F1}%)，嚴重超跌，反彈空間大");
                }
            }

            // 型態強度提取
            var bullishEngulfing = EvaluateAdjustedPattern("Bullish Engulfing", CalculateBullishEngulfingScore(data, volRatio), data, 2, true);
            var bearishEngulfing = EvaluateAdjustedPattern("Bearish Engulfing", CalculateBearishEngulfingScore(data, volRatio), data, 2, false);
            var piercingLine = EvaluateAdjustedPattern("Piercing Line", CalculatePiercingLineScore(data, volRatio), data, 2, true);
            var darkCloudCover = EvaluateAdjustedPattern("Dark Cloud Cover", CalculateDarkCloudCoverScore(data, volRatio), data, 2, false);
            var bullishHarami = EvaluateAdjustedPattern("Bullish Harami", CalculateBullishHaramiScore(data, volRatio), data, 2, true);
            var bearishHarami = EvaluateAdjustedPattern("Bearish Harami", CalculateBearishHaramiScore(data, volRatio), data, 2, false);
            var morningStar = EvaluateAdjustedPattern("Morning Star", CalculateMorningStarScore(data, volRatio), data, 3, true);
            var eveningStar = EvaluateAdjustedPattern("Evening Star", CalculateEveningStarScore(data, volRatio), data, 3, false);
            var threeWhiteSoldiers = EvaluateAdjustedPattern("Three White Soldiers", CalculateThreeWhiteSoldiersScore(data, volRatio), data, 3, true);
            var threeBlackCrows = EvaluateAdjustedPattern("Three Black Crows", CalculateThreeBlackCrowsScore(data, volRatio), data, 3, false);
            var risingThreeMethods = EvaluateAdjustedPattern("Rising Three Methods", CalculateRisingThreeMethodsScore(data, volRatio), data, 5, true);
            var fallingThreeMethods = EvaluateAdjustedPattern("Falling Three Methods", CalculateFallingThreeMethodsScore(data, volRatio), data, 5, false);

            var scoreBullishEngulfing = bullishEngulfing.AdjustedScore;
            var scoreBearishEngulfing = bearishEngulfing.AdjustedScore;
            var scorePiercingLine = piercingLine.AdjustedScore;
            var scoreDarkCloudCover = darkCloudCover.AdjustedScore;
            var scoreBullishHarami = bullishHarami.AdjustedScore;
            var scoreBearishHarami = bearishHarami.AdjustedScore;
            var scoreMorningStar = morningStar.AdjustedScore;
            var scoreEveningStar = eveningStar.AdjustedScore;
            var scoreThreeWhiteSoldiers = threeWhiteSoldiers.AdjustedScore;
            var scoreThreeBlackCrows = threeBlackCrows.AdjustedScore;
            var scoreRisingThreeMethods = risingThreeMethods.AdjustedScore;
            var scoreFallingThreeMethods = fallingThreeMethods.AdjustedScore;

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

            AppendPatternReason(reasons, bullishEngulfing);
            AppendPatternReason(reasons, bearishEngulfing);
            AppendPatternReason(reasons, piercingLine);
            AppendPatternReason(reasons, darkCloudCover);
            AppendPatternReason(reasons, bullishHarami);
            AppendPatternReason(reasons, bearishHarami);
            AppendPatternReason(reasons, morningStar);
            AppendPatternReason(reasons, eveningStar);
            AppendPatternReason(reasons, threeWhiteSoldiers);
            AppendPatternReason(reasons, threeBlackCrows);
            AppendPatternReason(reasons, risingThreeMethods);
            AppendPatternReason(reasons, fallingThreeMethods);

            var bullishScores = new[] { scoreBullishEngulfing, scorePiercingLine, scoreBullishHarami, scoreMorningStar, scoreThreeWhiteSoldiers, scoreRisingThreeMethods }
                .OrderByDescending(x => x).ToList();
            var bearishScores = new[] { scoreBearishEngulfing, scoreDarkCloudCover, scoreBearishHarami, scoreEveningStar, scoreThreeBlackCrows, scoreFallingThreeMethods }
                .OrderByDescending(x => x).ToList();

            // 複合型態分數計算
            var bullishComposite = bullishScores[0] * 0.6 + bullishScores[1] * 0.4;
            var bearishComposite = bearishScores[0] * 0.6 + bearishScores[1] * 0.4;

            // 將型態權重獨立對應至雙軌制：最高可提供 20 分的 Raw 分
            if (bullishComposite > 30)
            {
                AddOpportunity(bullishComposite * 0.2);
            }

            // 如果有顯著的空方反轉型態，直接按照強度比例灌入風險原始分（不設70分高門檻，改成漸進式）
            if (bearishComposite > 30)
            {
                AddCrashRisk(bearishComposite * 0.35); // 滿分100的型態最高可貢獻 35 分風險
            }

            reasons.Add($"[型態明細] 多頭權重:{bullishComposite:F0} 空頭權重:{bearishComposite:F0}");

            // =====================================================================
            // --- 5. 總結與標準化映射 ---
            // =====================================================================

            // 根據程式碼內各因子最大配置優化後的真實 MaxRaw 分母
            const double opportunityMaxRaw = 85.0;  // 理論極端利多總分
            const double crashRiskMaxRaw = 160.0;   // 理論極端利空與過熱總分

            var opportunityScore = (int)Math.Round(Clamp(opportunityRaw / opportunityMaxRaw * 100.0, 0, 100));
            var crashRiskScore = (int)Math.Round(Clamp(crashRiskRaw / crashRiskMaxRaw * 100.0, 0, 100));

            var globalDecision = "NEUTRAL";
            if (crashRiskScore >= 75)
            {
                globalDecision = "CRASH_WARNING";
                reasons.Insert(0, "【⚠ 大跌風險警告】大跌風險分數觸及危險水位，建議減碼防守、降槓桿。(High Crash Risk)");
            }
            else if (opportunityScore >= 75 && crashRiskScore < 35)
            {
                globalDecision = "STRONG_BUY";
            }
            else if (opportunityScore >= 60 && crashRiskScore < 45)
            {
                globalDecision = "BUY"; // 擴充中規中矩的多方標籤
            }

            // 自動補足系統 Summary Tag
            if (!patternTags.Any(x => x.IsBullish) && opportunityScore >= 60)
            {
                patternTags.Add(new PatternTag { Key = "opportunity_summary", Label = "多頭集結", Score = opportunityScore, IsRisk = false, IsBullish = true });
            }
            if (!patternTags.Any(x => x.IsRisk) && crashRiskScore >= 60)
            {
                patternTags.Add(new PatternTag { Key = "crash_risk_summary", Label = "風險爆表", Score = crashRiskScore, IsRisk = true, IsBullish = false });
            }

            reasons.Insert(0, $"【機會分數 {opportunityScore} / 風險分數 {crashRiskScore}】最終決策: {globalDecision}");
            reasons.Add($"[底層 Raw 偵錯] OpportunityRaw={opportunityRaw:F1}/{opportunityMaxRaw:F0} | CrashRiskRaw={crashRiskRaw:F1}/{crashRiskMaxRaw:F0}");

            return new TrendRecommendationResult
            {
                Score = opportunityScore,
                CrashRiskScore = crashRiskScore,
                GlobalDecision = globalDecision,
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

        private static void AppendPatternReason(List<string> reasons, PatternAdjustmentResult pattern)
        {
            if (pattern == null || pattern.BaseScore <= 0)
            {
                return;
            }

            reasons.Add($"{pattern.Name} +{pattern.ContributionPoints:F0} points ({pattern.FactorsText}) [base:{pattern.BaseScore:F0} -> adjusted:{pattern.AdjustedScore:F0}]");
        }

        private static PatternAdjustmentResult EvaluateAdjustedPattern(string patternName, double baseScore, List<CandleData> data, int lookbackBars, bool bullish)
        {
            var result = new PatternAdjustmentResult
            {
                Name = patternName,
                BaseScore = baseScore,
                AdjustedScore = 0,
                ContributionPoints = 0,
                FactorsText = "none"
            };

            if (baseScore <= 0 || data == null || data.Count == 0)
            {
                return result;
            }

            var factors = new List<string>();
            var pct = 0.0;
            var bars = data.Skip(Math.Max(0, data.Count - Math.Max(lookbackBars, 2))).ToList();
            var latest = bars.Last();

            // Volume
            var volumeRising = bars.Zip(bars.Skip(1), (a, b) => b.Volume > a.Volume).All(x => x);
            var volumeShrinking = bars.Zip(bars.Skip(1), (a, b) => b.Volume < a.Volume).All(x => x);
            var priceRising = bars.Zip(bars.Skip(1), (a, b) => b.Close > a.Close).All(x => x);
            var priceFalling = bars.Zip(bars.Skip(1), (a, b) => b.Close < a.Close).All(x => x);
            var volumeDivergence = (priceRising && volumeShrinking) || (priceFalling && volumeRising);
            if (volumeDivergence)
            {
                pct -= 15;
                factors.Add("volume divergence -15%");
            }
            else if (volumeRising)
            {
                pct += 10;
                factors.Add("rising volume +10%");
            }
            else if (volumeShrinking)
            {
                pct -= 10;
                factors.Add("shrinking volume -10%");
            }

            // Position
            var close = (double)latest.Close;
            if (latest.MA20 > 0)
            {
                if (close <= latest.MA20 * 0.97)
                {
                    pct += 15;
                    factors.Add("low-level +15%");
                }
                else if (close >= latest.MA20 * 1.05)
                {
                    pct -= 15;
                    factors.Add("high-level -15%");
                }
            }

            if (data.Count > 20)
            {
                if (bullish)
                {
                    var prevHigh = data.Skip(Math.Max(0, data.Count - 21)).Take(20).Max(x => (double)x.High);
                    if (close > prevHigh)
                    {
                        pct += 10;
                        factors.Add("breakout +10%");
                    }
                }
                else
                {
                    var prevLow = data.Skip(Math.Max(0, data.Count - 21)).Take(20).Min(x => (double)x.Low);
                    if (close < prevLow)
                    {
                        pct += 10;
                        factors.Add("breakout +10%");
                    }
                }
            }

            // Shadows
            var range = Math.Max(0.0000001, (double)(latest.High - latest.Low));
            var upperShadow = Math.Max(0, (double)latest.High - Math.Max((double)latest.Open, (double)latest.Close));
            var lowerShadow = Math.Max(0, Math.Min((double)latest.Open, (double)latest.Close) - (double)latest.Low);
            var upperRatio = upperShadow / range;
            var lowerRatio = lowerShadow / range;
            if (lowerRatio >= 0.4)
            {
                pct += 10;
                factors.Add("long lower shadow +10%");
            }
            if (upperRatio >= 0.4)
            {
                pct -= 10;
                factors.Add("long upper shadow -10%");
            }
            if (upperRatio <= 0.05 && lowerRatio <= 0.05)
            {
                pct += 5;
                factors.Add("no shadow +5%");
            }

            // Body size
            var body = Math.Abs((double)latest.Close - (double)latest.Open);
            var bodyRatio = body / range;
            if (bodyRatio >= 0.6)
            {
                pct += 10;
                factors.Add("long body +10%");
            }
            else if (bodyRatio <= 0.25)
            {
                pct -= 10;
                factors.Add("short body -10%");
            }

            if (bars.Count >= 3)
            {
                var bodies = bars.Select(x => Math.Abs((double)x.Close - (double)x.Open)).ToList();
                var shrinkingSequence = bodies.Zip(bodies.Skip(1), (a, b) => b < a).All(x => x);
                if (shrinkingSequence)
                {
                    pct -= 20;
                    factors.Add("shrinking sequence -20%");
                }
            }

            // Structure
            if (bars.Count >= 2)
            {
                var uniform = bullish
                    ? bars.Zip(bars.Skip(1), (a, b) => b.Close > a.Close).All(x => x)
                    : bars.Zip(bars.Skip(1), (a, b) => b.Close < a.Close).All(x => x);
                if (uniform)
                {
                    pct += 5;
                    factors.Add("uniform structure +5%");
                }
            }

            if (bars.Count >= 3)
            {
                var mid = bars[bars.Count / 2];
                var midRange = Math.Max(0.0000001, (double)(mid.High - mid.Low));
                var midBodyRatio = Math.Abs((double)mid.Close - (double)mid.Open) / midRange;
                if (midBodyRatio <= 0.2)
                {
                    pct -= 10;
                    factors.Add("mid doji/small body -10%");
                }
            }

            var isLatestBull = latest.Close > latest.Open;
            var isLatestBear = latest.Close < latest.Open;
            if ((bullish && isLatestBear) || (!bullish && isLatestBull))
            {
                pct -= 20;
                factors.Add("last bar reversal -20%");
            }

            var adjusted = ClampValue(baseScore * (1 + pct / 100.0), 0, 100);
            result.AdjustedScore = adjusted;
            result.ContributionPoints = adjusted * 0.12;
            result.FactorsText = factors.Count == 0 ? "none" : string.Join(" + ", factors);
            return result;
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
