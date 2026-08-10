using StockManager.Library;
using StockTracker.Models;
using StockTracker.Services;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class AdvancedTradingStrategyEngine
{
    /// <summary>
    /// Shared state for the deterministic, zero-holding strategy simulation.
    /// Every caller advances this state one trading day at a time.
    /// </summary>
    public sealed class SimulationState
    {
        public double HoldingPercentage { get; private set; }
        public double HoldingCost { get; private set; }
        public double? PreviousFinalScore { get; private set; }

        public void Advance(StrategyOutputViewModel output, double closingPrice)
        {
            if (output == null) return;

            var nextHolding = ClampHolding(output.ExecutedHolding);
            if (nextHolding <= ComparisonEpsilon)
            {
                HoldingCost = 0d;
            }
            else if (nextHolding > HoldingPercentage + ComparisonEpsilon)
            {
                if (HoldingPercentage <= ComparisonEpsilon)
                    HoldingCost = closingPrice;
                else
                {
                    var addedHolding = nextHolding - HoldingPercentage;
                    HoldingCost = ((HoldingPercentage * HoldingCost) + (addedHolding * closingPrice)) / nextHolding;
                }
            }

            HoldingPercentage = nextHolding;
            PreviousFinalScore = output.FinalScore;
        }
    }

    public static StrategyOutputViewModel EvaluateSimulatedDay(
        SimulationState state,
        TrendRecommendationResult currentRecommendation,
        IReadOnlyList<TrendRecommendationResult> recent5DayRecommendations,
        double currentPrice,
        double? yesterdayPrice,
        double? price20DaysAgo,
        double? ma5,
        double? ma20,
        double? previousMa20,
        double? currentVolume = null,
        double? averageVolume20 = null,
        double? ma60 = null,
        double? ma120 = null,
        double? ma240 = null,
        double? openPrice = null,
        IReadOnlyList<CandleData> candles = null)
    {
        state = state ?? new SimulationState();
        var output = EvaluateStrategy(
            currentRecommendation, recent5DayRecommendations, state.HoldingPercentage,
            currentPrice, yesterdayPrice, price20DaysAgo, ma5, ma20, previousMa20,
            state.HoldingCost, currentVolume, averageVolume20, ma60, ma120, ma240,
            openPrice, state.PreviousFinalScore, candles);
        state.Advance(output, currentPrice);
        return output;
    }
    private static readonly double[] StageLevels = { 0d, 30d, 60d, 100d };
    private const double ComparisonEpsilon = 0.0001d;
    private const double LinearStartScore = 50d;
    private const double LinearFullScore = 90d;
    private const double DeadzoneThreshold = 8d; // 震盪緩衝帶 8%

    public static StrategyOutputViewModel EvaluateStrategy(
        TrendRecommendationResult currentRecommendation,
        IReadOnlyList<TrendRecommendationResult> recent5DayRecommendations,
        double currentHoldingPercentage,
        double currentPrice,
        double? yesterdayPrice,
        double? price20DaysAgo,
        double? ma5,
        double? ma20,
        double? previousMa20,
        double holdingCost,
        double? currentVolume = null,
        double? averageVolume20 = null,
        double? ma60 = null,
        double? ma120 = null,
        double? ma240 = null,
        double? openPrice = null,
        double? previousFinalScore = null,
        IReadOnlyList<CandleData> candles = null) // 傳入 K 棒資料以啟用量化線型分析
    {
        // 1. 初始化與邊界保護
        var normalizedHolding = ClampHolding(currentHoldingPercentage);
        var output = new StrategyOutputViewModel
        {
            GlobalDecision = "HOLD",
            ActionText = "觀望",
            CurrentHoldingPercentage = normalizedHolding,
            ExecutedHolding = normalizedHolding,
            ActionColor = "#A0A0A0"
        };

        if (currentVolume.HasValue && currentVolume.Value <= 0)
        {
            output.Reasons.Add("⚠️ 成交量為 0，無法進行策略評估，維持觀望。");
            output.StageLabel = BuildLinearHoldingLabel(normalizedHolding);
            output.Description = $"目前持股 {normalizedHolding:F0}%，成交量異常(0)，部位維持鎖定。";
            return output;
        }

        if (averageVolume20.HasValue && averageVolume20.Value <= 0)
        {
            output.Reasons.Add("⚠️ 20日均量為 0，無法進行策略評估，維持觀望。");
            output.StageLabel = BuildLinearHoldingLabel(normalizedHolding);
            output.Description = $"目前持股 {normalizedHolding:F0}%，均量異常(0)，部位維持鎖定。";
            return output;
        }

        if (currentRecommendation == null)
        {
            output.Reasons.Add("缺少當日策略輸入，維持觀望。");
            output.StageLabel = BuildLinearHoldingLabel(normalizedHolding);
            output.Description = $"目前持股 {normalizedHolding:F0}%，策略輸入不足，部位維持鎖定。";
            return output;
        }

        var score = Math.Max(0, Math.Min(100, currentRecommendation.Score));
        var crashRiskScore = Math.Max(0, Math.Min(100, currentRecommendation.CrashRiskScore));
        var adjustedScore = score;
        var biasPenalty = 0d;
        var chipDefenseActivated = false;
        var chipDefenseText = string.Empty;

        // ----------------------------------------------------
        // 2. 技術面基礎狀態判定
        // ----------------------------------------------------
        var bias20 = CalculateBias20(currentPrice, ma20);
        var isMa20Uptrend = ma20.HasValue && previousMa20.HasValue && (ma20.Value > previousMa20.Value);
        var isStrongMomentum = (score >= 70d) || (normalizedHolding >= 40d && ma20.HasValue && currentPrice > ma20.Value);

        var isLongTermBullish = ma20.HasValue && ma60.HasValue && ma120.HasValue &&
                                (ma20.Value > ma60.Value && ma60.Value > ma120.Value);
        var isAboveMa120 = ma120.HasValue && currentPrice > ma120.Value;
        var isAboveMa240 = ma240.HasValue && currentPrice > ma240.Value;

        // 成交量與 K 線型態
        var volumeRatio = (currentVolume.HasValue && averageVolume20.HasValue && averageVolume20.Value > 0)
            ? currentVolume.Value / averageVolume20.Value
            : 1.0d;
        bool isRedCandle = openPrice.HasValue ? (currentPrice >= openPrice.Value) : false;
        bool isInstitutionalBuy = currentRecommendation.Reasons != null &&
                                  currentRecommendation.Reasons.Any(r => r.Contains("投信") || r.Contains("外資買超") || r.Contains("法人買超"));

        // 填充 output 指標欄位（供 UI 顯示）
        output.Bias20 = bias20;
        output.VolumeRatio = volumeRatio;

        // ----------------------------------------------------
        // 3. 位階動態乖離率 (Bias20) 風險扣分
        // ----------------------------------------------------
        bool isNearSupportZone = ma20.HasValue && ma20.Value > 0 && Math.Abs(bias20) <= 0.03d;

        if (isNearSupportZone)
        {
            output.Reasons.Add($"🛡️ 股價回測均線支撐區 (Bias20={bias20:P2})，豁免正乖離扣分。");
        }
        else if (normalizedHolding >= 50d && bias20 > 0.15d)
        {
            biasPenalty = 15d;
            adjustedScore = Math.Max(0, score - (int)biasPenalty);
            output.Reasons.Add($"⚠️ 高檔位階過熱：持股達 {normalizedHolding:F0}% 且 Bias20={bias20:P2}，風險扣減 {biasPenalty} 分。");
        }
        else if (!isStrongMomentum)
        {
            if (bias20 > 0.12d && bias20 <= 0.18d)
            {
                biasPenalty = 10d;
                adjustedScore = Math.Max(0, score - (int)biasPenalty);
                output.Reasons.Add($"⚠️ 正乖離偏高：Bias20={bias20:P2}，分數扣減 {biasPenalty}。");
            }
            else if (bias20 > 0.18d)
            {
                biasPenalty = 25d;
                adjustedScore = Math.Max(0, score - (int)biasPenalty);
                output.Reasons.Add($"🚨 正乖離極大(過熱)：Bias20={bias20:P2}，分數扣減 {biasPenalty}。");
            }
        }
        else
        {
            output.Reasons.Add($"🚀 股價處於強勢多頭動能區，豁免 Bias20 乖離扣分限制。");
        }

        // ----------------------------------------------------
        // 4. 【距離衰減 + 老王扣抵/站穩/強彈豁免】品質計分模組
        // ----------------------------------------------------
        var techBonus = 0d;

        if (currentPrice > 0)
        {
            // A. 距離衰減權重函數
            double CalculateDistanceWeight(double? ma)
            {
                if (!ma.HasValue || ma.Value <= 0) return 0d;
                double distPct = (currentPrice - ma.Value) / ma.Value;

                if (distPct >= 0d && distPct <= 0.005d) return 1.0d; // 甜點區 (100%)
                if (distPct > 0.005d && distPct <= 0.025d) return 1.0d - ((distPct - 0.005d) / (0.025d - 0.005d)); // 平滑衰減
                if (distPct < 0d && distPct >= -0.005d) return 0.6d; // 洗盤防守區 (60%)
                return 0d;
            }

            // B. 老王增強版技術品質乘數 (含扣抵預判與強彈豁免)
            double CalculateWangQualityMultiplier(
                double maValue,
                double? maDeductionPrice,
                out string statusText)
            {
                // 老王鐵律：爆量黑 K 貫穿，直接防範接刀
                if (volumeRatio >= 1.25d && !isRedCandle)
                {
                    statusText = "爆量貫穿(禁接刀)";
                    return 0.0d;
                }

                double q = 1.0d;
                var details = new List<string>();
                bool isStrongRebound = volumeRatio >= 1.2d && isRedCandle; // 強勢反彈特徵

                // 1. 扣抵預判 + 強彈豁免
                if (maDeductionPrice.HasValue && maDeductionPrice.Value > 0)
                {
                    if (currentPrice > maDeductionPrice.Value)
                    {
                        q += 0.2d;
                        details.Add("扣低轉揚");
                    }
                    else if (isStrongRebound)
                    {
                        // ⚡ 強彈豁免：雖扣高，但因爆量長紅，不給予 0.6x 重罰，維持 1.0x 敏捷反彈
                        details.Add("扣高(強彈豁免)");
                    }
                    else
                    {
                        q *= 0.6d; // 弱勢反彈且扣高，判定均線向下壓制
                        details.Add("扣高下彎風險");
                    }
                }

                // 2. 連續兩天站穩驗證
                if (yesterdayPrice.HasValue)
                {
                    bool yesterdayAbove = yesterdayPrice.Value > maValue;
                    bool todayAbove = currentPrice > maValue;

                    if (todayAbove && yesterdayAbove)
                    {
                        q += 0.15d;
                        details.Add("連2天站穩");
                    }
                    else if (todayAbove && !yesterdayAbove)
                    {
                        q *= 0.85d;
                        details.Add("首日試探");
                    }
                }

                // 3. 籌碼與 K 線基礎品質
                if (isInstitutionalBuy) { q += 0.2d; details.Add("法人大買"); }
                if (volumeRatio <= 0.8d) { q += 0.15d; details.Add("量縮沉澱"); }
                if (isRedCandle) { q += 0.1d; details.Add("收紅K"); }

                statusText = details.Count > 0 ? string.Join("+", details) : "平穩";
                return Math.Max(0.0d, Math.Min(2.0d, q));
            }

            // --- 各均線得分運算 ---

            // MA20 (月線, 滿分 8 分)
            double ma20DistWeight = CalculateDistanceWeight(ma20);
            if (ma20DistWeight > 0)
            {
                double qMult = CalculateWangQualityMultiplier(ma20.Value, price20DaysAgo, out string qText);
                double scoreAdded = Math.Round(8.0d * ma20DistWeight * qMult, 1);
                if (scoreAdded > 0)
                {
                    techBonus += scoreAdded;
                    output.Reasons.Add($"📈 【MA20 月線反彈】距離權重 {ma20DistWeight:P0}，型態 [{qText}]，加分 +{scoreAdded}。");
                }
            }

            // MA60 (季線, 滿分 14 分)
            double ma60DistWeight = CalculateDistanceWeight(ma60);
            if (ma60DistWeight > 0)
            {
                double qMult = CalculateWangQualityMultiplier(ma60.Value, null, out string qText);
                double scoreAdded = Math.Round(14.0d * ma60DistWeight * qMult, 1);
                if (scoreAdded > 0)
                {
                    techBonus += scoreAdded;
                    output.Reasons.Add($"🚀 【MA60 季線生命線】距離權重 {ma60DistWeight:P0}，型態 [{qText}]，加分 +{scoreAdded}。");
                }
            }

            // MA120 (半年線, 滿分 18 分)
            double ma120DistWeight = CalculateDistanceWeight(ma120);
            if (ma120DistWeight > 0 && isAboveMa120)
            {
                double qMult = CalculateWangQualityMultiplier(ma120.Value, null, out string qText);
                double scoreAdded = Math.Round(18.0d * ma120DistWeight * qMult, 1);
                if (scoreAdded > 0)
                {
                    techBonus += scoreAdded;
                    output.Reasons.Add($"🛡️ 【MA120 半年線】距離權重 {ma120DistWeight:P0}，型態 [{qText}]，護城河加分 +{scoreAdded}。");
                }
            }

            // MA240 (年線, 滿分 22 分)
            double ma240DistWeight = CalculateDistanceWeight(ma240);
            if (ma240DistWeight > 0 && isAboveMa240)
            {
                double qMult = CalculateWangQualityMultiplier(ma240.Value, null, out string qText);
                double scoreAdded = Math.Round(22.0d * ma240DistWeight * qMult, 1);
                if (scoreAdded > 0)
                {
                    techBonus += scoreAdded;
                    output.Reasons.Add($"🏛️ 【MA240 年線底線】距離權重 {ma240DistWeight:P0}，型態 [{qText}]，加分 +{scoreAdded}。");
                }
            }
        }

        // 多空排列加扣分
        if (ma5.HasValue && ma20.HasValue && currentPrice > ma5.Value && ma5.Value > ma20.Value && isMa20Uptrend)
        {
            if (isLongTermBullish)
            {
                techBonus += 8d;
                output.Reasons.Add($"🔥 【大多頭排列】Price > MA5 > MA20 > MA60 > MA120，加分 +8。");
            }
            else
            {
                techBonus += 5d;
                output.Reasons.Add($"🔥 【短線多頭排列】Price > MA5 > MA20 且月線持續上揚，加分 +5。");
            }
        }

        var techScore = Math.Max(0d, Math.Min(100d, adjustedScore + techBonus));

        // ----------------------------------------------------
        // 5. 技術/籌碼權重解耦
        // ----------------------------------------------------
        var chipScore = CalculateChipScore(currentRecommendation, recent5DayRecommendations);
        var techWeight = 0.60d;
        var chipWeight = 0.40d;

        if (techScore >= 70d)
        {
            techWeight = 0.75d;
            chipWeight = 0.25d;
            output.Reasons.Add("🔥 啟動【強勢技術主導模式】，技術面權重提升至 75%。");
        }

        var weightedScore = Math.Max(0d, Math.Min(100d, (techScore * techWeight) + (chipScore * chipWeight)));

        var recentChipSupport = CalculateRecentChipSupport(recent5DayRecommendations);
        if (techScore < 55d && recentChipSupport >= 60d)
        {
            weightedScore = Math.Max(weightedScore, Math.Min(100d, (techScore * 0.4d) + (recentChipSupport * 0.6d)));
            chipDefenseActivated = true;
            chipDefenseText = $"✅ 技術面拉回但籌碼主力防禦啟動。";
        }

        var currentDayRawScore = (double)Math.Round(weightedScore);

        // ----------------------------------------------------
        // 6. 動態 Alpha 平滑分數引擎 (EMA + Overdrive Bypass)
        // ----------------------------------------------------
        double baseAlpha = 0.30d;
        double effectiveAlpha = baseAlpha;

        double lastScore = previousFinalScore.HasValue
            ? previousFinalScore.Value
            : ((normalizedHolding / 100d) * (LinearFullScore - LinearStartScore) + LinearStartScore);

        if (normalizedHolding <= 0 && !previousFinalScore.HasValue) lastScore = 40d;

        // 檢查 Overdrive Bypass 條件：分差 >= 20 且 爆量長紅 K
        double scoreDiff = currentDayRawScore - lastScore;
        if (scoreDiff >= 20.0d && volumeRatio >= 1.2d && isRedCandle)
        {
            effectiveAlpha = 0.65d; // 提升 Alpha 加速反應
            output.Reasons.Add($"⚡ 觸發【Overdrive 爆發豁免】(分差+{scoreDiff:F0} 且爆量紅K)，Alpha 提升至 0.65 加速部位追擊。");
        }

        // EMA 平滑計算
        double smoothedScore = (effectiveAlpha * currentDayRawScore) + ((1.0d - effectiveAlpha) * lastScore);
        var finalScore = (int)Math.Round(smoothedScore);

        output.Reasons.Add($"今日實算分: {currentDayRawScore:F0}，前日平滑分: {lastScore:F1}，最終平滑分: {finalScore}");

        // ----------------------------------------------------
        // 7. 硬停損風控 (-7% 機制)
        // ----------------------------------------------------
        if (currentPrice > 0 && holdingCost > 0)
        {
            var drawdown = (currentPrice - holdingCost) / holdingCost;
            if (drawdown <= -0.07d)
            {
                output.GlobalDecision = "CLEAR";
                output.ActionText = "機械停損清倉";
                output.CurrentHoldingPercentage = 0d;
                output.ExecutedHolding = 0d;
                output.ActionColor = "#FF3333";
                output.FinalScore = finalScore;
                output.TechScore = Math.Round(techScore, 1);
                output.ChipScore = Math.Round(chipScore, 1);
                output.DecisionSummary = "跌幅已達預設停損門檻，優先清空模擬部位控制損失。";
                output.PositionPlanText = $"模擬持股：{normalizedHolding:F0}% → 0%（降低 {normalizedHolding:F0}%）";
                output.KeyReasons.Clear();
                output.KeyReasons.Add($"現價相對持倉成本下跌 {drawdown:P1}，低於 -7% 停損門檻。");
                output.KeyReasons.Add($"綜合分數 {finalScore} 分（技術 {techScore:F0}、籌碼 {chipScore:F0}）。");
                output.Reasons.Add($"觸發 -7% 機械停損");
                return output;
            }
        }

        // ----------------------------------------------------
        // 8. 線性滑動倉位與均線防守鎖倉
        // ----------------------------------------------------
        var action = "HOLD";
        var originalTarget = ResolveLinearTargetHolding(finalScore);
        var targetHolding = originalTarget;
        var isOverheat = crashRiskScore >= 75 || string.Equals(currentRecommendation.GlobalDecision, "CRASH_WARNING", StringComparison.OrdinalIgnoreCase);

        if (isOverheat)
        {
            targetHolding = Math.Min(30d, targetHolding);
            output.Reasons.Add($"⚠️ 短線極度過熱/觸發崩盤預警，強制控管目標部位上限於 {targetHolding:F0}%。");
        }

        bool isScoreHealthy = finalScore >= 45;

        if (targetHolding < normalizedHolding && normalizedHolding >= 30d)
        {
            if (isScoreHealthy)
            {
                bool IsNearMa(double? ma) => ma.HasValue && ma.Value > 0 && currentPrice > ma.Value && ((currentPrice - ma.Value) / ma.Value <= 0.025d);

                if (IsNearMa(ma20) && isMa20Uptrend)
                {
                    targetHolding = normalizedHolding;
                    output.Reasons.Add("🛡️ 月線反彈鎖倉：股價緊貼 MA20 支撐區，為預期反彈點，強制抱緊觀望。");
                }
                else if (IsNearMa(ma60))
                {
                    targetHolding = normalizedHolding;
                    output.Reasons.Add("🛡️ 季線防禦鎖倉：股價進入 MA60 季線關鍵支撐區，為波段強反彈點，強制抱緊觀望。");
                }
                else if (IsNearMa(ma120) || IsNearMa(ma240))
                {
                    targetHolding = normalizedHolding;
                    output.Reasons.Add("🛡️ 長期均線鎖倉：股價位於半年線/年線護城河，嚴防洗盤，強制鎖倉觀望。");
                }
            }
            else
            {
                output.Reasons.Add($"⚠️ 綜合評分過低 ({finalScore}分)，解除均線鎖倉保護，執行正常減碼。");
            }
        }

        var diff = targetHolding - normalizedHolding;

        // 9. 交易動作與 Deadzone 緩衝
        if (Math.Abs(diff) < DeadzoneThreshold)
        {
            action = "HOLD";
            targetHolding = normalizedHolding;
            output.Reasons.Add($"ℹ️ 變動量 {Math.Abs(diff):F1}% 未達緩衝閾值 {DeadzoneThreshold}%，維持原倉位。");
        }
        else
        {
            targetHolding = Math.Round(ClampHolding(targetHolding), 0, MidpointRounding.AwayFromZero);
            if (targetHolding > normalizedHolding + ComparisonEpsilon) action = "BUY_LINEAR";
            else if (targetHolding < normalizedHolding - ComparisonEpsilon) action = isOverheat ? "EXIT_OVERHEAT" : "REDUCE_LINEAR";
            else { action = "HOLD"; targetHolding = normalizedHolding; }
        }

        var executedHolding = targetHolding;

        // 10. 輸出封裝
        output.GlobalDecision = action;
        output.CurrentHoldingPercentage = executedHolding;
        output.ExecutedHolding = executedHolding;
        output.StageLabel = BuildLinearHoldingLabel(executedHolding);
        output.ActionText = BuildActionText(action, executedHolding);
        output.Description = BuildDescription(normalizedHolding, finalScore, action, executedHolding, executedHolding);
        output.ActionColor = ResolveActionColor(action);

        if (chipDefenseActivated) output.Description += " " + chipDefenseText;

        if (!string.Equals(action, "HOLD", StringComparison.OrdinalIgnoreCase))
        {
            var isBuy = executedHolding > normalizedHolding;
            string markerText = isBuy ? "加" : "減";
            if (action == "EXIT_OVERHEAT") markerText = "熱";
            AddChartMarker(output, currentPrice, markerText, output.ActionColor, isBuy ? "BUY" : "SELL");
        }

        // 11. 填充量化指標（供儀表板 UI 顯示）
        output.FinalScore = finalScore;
        output.TechScore  = Math.Round(techScore, 1);
        output.ChipScore  = Math.Round(CalculateChipScore(currentRecommendation, recent5DayRecommendations), 1);
        PopulateUserFacingSummary(output, normalizedHolding, originalTarget, currentDayRawScore, isOverheat);

        // 12. 量化線型分析（TechnicalLineQuantizer）
        if (candles != null && candles.Count >= 10)
        {
            var analysis = TechnicalLineQuantizer.Analyze(candles);
            output.SupportZones = analysis.Zones.Where(z => z.IsValid).ToList();
            output.TrendLines   = analysis.TrendLines;
        }

        return output;
    }

    // 私有輔助函數
    private static void AddChartMarker(StrategyOutputViewModel output, double price, string text, string colorHex, string type)
    {
        output.ChartMarkers.Add(new ChartMarker { Time = DateTime.Now, Text = text, Price = price, ColorHex = colorHex, MarkerType = type });
    }

    private static void PopulateUserFacingSummary(
        StrategyOutputViewModel output,
        double previousHolding,
        double scoreTargetHolding,
        double rawScore,
        bool isOverheat)
    {
        var holdingChange = output.ExecutedHolding - previousHolding;
        var direction = holdingChange > ComparisonEpsilon ? "增加" :
            holdingChange < -ComparisonEpsilon ? "降低" : "維持";
        output.PositionPlanText = $"模擬持股：{previousHolding:F0}% → {output.ExecutedHolding:F0}%（{direction} {Math.Abs(holdingChange):F0}%）";

        if (output.GlobalDecision == "BUY_LINEAR")
            output.DecisionSummary = "分數與趨勢轉強，採分批加碼，不一次投入。";
        else if (output.GlobalDecision == "REDUCE_LINEAR")
            output.DecisionSummary = "分數轉弱，先分批降低部位，保留後續觀察空間。";
        else if (output.GlobalDecision == "EXIT_OVERHEAT")
            output.DecisionSummary = "大跌風險偏高，優先降部位控制風險。";
        else
            output.DecisionSummary = "訊號尚未達到調整門檻，先維持目前模擬部位。";

        output.KeyReasons.Clear();
        output.KeyReasons.Add($"綜合分數 {output.FinalScore} 分（今日原始 {rawScore:F0}；技術 {output.TechScore:F0}、籌碼 {output.ChipScore:F0}）");
        output.KeyReasons.Add($"20 日乖離 {output.Bias20:P1}；量能為 20 日均量的 {output.VolumeRatio:F2} 倍");
        if (isOverheat)
            output.KeyReasons.Add("風險警示：系統偵測到高檔／大跌風險，目標持股已受限。");
        else
            output.KeyReasons.Add($"分數對應的理論持股為 {scoreTargetHolding:F0}%，再套用緩衝規則後執行 {output.ExecutedHolding:F0}%。");
    }

    private static double CalculateBias20(double currentPrice, double? ma20)
    {
        if (!ma20.HasValue || ma20.Value <= 0d || currentPrice <= 0d) return 0d;
        return (currentPrice - ma20.Value) / ma20.Value;
    }

    private static double CalculateRecentChipSupport(IReadOnlyList<TrendRecommendationResult> recent5DayRecommendations)
    {
        if (recent5DayRecommendations == null || recent5DayRecommendations.Count == 0) return 50d;
        var recent = recent5DayRecommendations.Where(x => x != null).Take(5).ToList();
        return recent.Count == 0 ? 50d : recent.Average(x => CalculateChipScore(x, null));
    }

    private static double CalculateChipScore(TrendRecommendationResult currentRecommendation, IReadOnlyList<TrendRecommendationResult> recent5DayRecommendations)
    {
        if (currentRecommendation == null) return 50d;
        var reasons = currentRecommendation.Reasons ?? new List<string>();
        var chipPositive = 0d;
        var chipNegative = 0d;

        foreach (var reason in reasons)
        {
            if (string.IsNullOrWhiteSpace(reason)) continue;
            var isChip = reason.Contains("[籌碼+") || (reason.Contains("[風險+") && reason.Contains("外資")) || reason.Contains("投信") || reason.Contains("法人");
            if (!isChip) continue;

            var value = ExtractScoreValue(reason);
            if (reason.Contains("[籌碼+") || reason.Contains("買超")) chipPositive += value;
            else chipNegative += value;
        }

        return Math.Max(0d, Math.Min(100d, 50d + chipPositive - chipNegative));
    }

    private static double ExtractScoreValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0d;
        var match = Regex.Match(text, @"\[(?:籌碼|風險)[+-]?(\d+(\.\d+)?)\]");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double val)) return Math.Min(15d, Math.Abs(val));
        return 5d;
    }

    private static double ClampHolding(double value) => Math.Max(0d, Math.Min(100d, value));
    private static string BuildLinearHoldingLabel(double holding) => holding <= ComparisonEpsilon ? "線性倉位｜空倉 0%" : holding >= 100d - ComparisonEpsilon ? "線性倉位｜滿倉 100%" : $"線性倉位｜{holding:F0}%";
    private static string BuildActionText(string action, double executedHolding) => action == "BUY_LINEAR" ? $"線性加碼至 {executedHolding:F0}%" : action == "REDUCE_LINEAR" ? $"線性減碼至 {executedHolding:F0}%" : action == "CLEAR" ? "停損清倉 (0%)" : $"HOLD (鎖倉 {executedHolding:F0}%)";
    private static string BuildDescription(double currentHolding, int score, string action, double targetHolding, double executedHolding) => action == "BUY_LINEAR" ? $"技術面強勢/階梯支撐，分數 {score}，平滑加碼至 {executedHolding:F0}%。" : $"維持多頭鎖倉排列/階梯均線支撐，部位穩定抱緊於 {executedHolding:F0}%。";
    private static string ResolveActionColor(string action) => action == "CLEAR" ? "#FF3333" : action == "BUY_LINEAR" ? "#00CC66" : "#A0A0A0";
    private static double ResolveLinearTargetHolding(int finalScore) => finalScore < LinearStartScore ? 0d : finalScore > LinearFullScore ? 100d : ((finalScore - LinearStartScore) / (LinearFullScore - LinearStartScore)) * 100d;
}
