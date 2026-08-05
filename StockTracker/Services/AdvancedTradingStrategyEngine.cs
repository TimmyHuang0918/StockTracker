using StockManager.Library;
using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class AdvancedTradingStrategyEngine
{
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
        double? ma5,
        double? ma20,
        double? previousMa20,
        double holdingCost,
        double? currentVolume = null,
        double? averageVolume20 = null,
        double? ma60 = null,
        double? ma120 = null,
        double? ma240 = null)
    {
        // 以昨日真實持股作為比較基準
        var normalizedHolding = ClampHolding(currentHoldingPercentage);
        var output = new StrategyOutputViewModel
        {
            GlobalDecision = "HOLD",
            ActionText = "觀望",
            CurrentHoldingPercentage = normalizedHolding,
            ExecutedHolding = normalizedHolding,
            ActionColor = "#A0A0A0"
        };

        // 驗證成交量：不應該有成交量為 0 的情況
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
        // 技術面基礎狀態判定 (MA 與 量價關係)
        // ----------------------------------------------------
        var bias20 = CalculateBias20(currentPrice, ma20);
        var isMa20Uptrend = ma20.HasValue && previousMa20.HasValue && (ma20.Value > previousMa20.Value);
        var isStrongMomentum = (score >= 70d) || (normalizedHolding >= 40d && ma20.HasValue && currentPrice > ma20.Value);

        // 長短期均線結構與牛熊趨勢判定
        var isLongTermBullish = ma20.HasValue && ma60.HasValue && ma120.HasValue &&
                                (ma20.Value > ma60.Value && ma60.Value > ma120.Value);
        var isAboveMa120 = ma120.HasValue && currentPrice > ma120.Value;
        var isAboveMa240 = ma240.HasValue && currentPrice > ma240.Value;

        // 成交量比值 (今日成交量 / 20日均量)
        var volumeRatio = (currentVolume.HasValue && averageVolume20.HasValue && averageVolume20.Value > 0)
            ? currentVolume.Value / averageVolume20.Value
            : 1.0d;

        // ----------------------------------------------------
        // 1) 方案 B：位階動態乖離率 (Bias20) 風險扣分模型
        // ----------------------------------------------------
        // 檢查是否處於均線支撐安全區 (乖離率 3% 以內)
        bool isNearSupportZone = ma20.HasValue && ma20.Value > 0 && Math.Abs(bias20) <= 0.03d;

        if (isNearSupportZone)
        {
            output.Reasons.Add($"🛡️ 股價回測均線支撐區 (Bias20={bias20:P2})，豁免正乖離扣分。");
        }
        else if (normalizedHolding >= 50d && bias20 > 0.15d)
        {
            // 高檔高持股平滑扣分，防止暴跌前追高死抱
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
                output.Reasons.Add($"⚠️正乖離偏高：Bias20={bias20:P2}，分數扣減 {biasPenalty}。");
            }
            else if (bias20 > 0.18d)
            {
                biasPenalty = 25d;
                adjustedScore = Math.Max(0, score - (int)biasPenalty);
                output.Reasons.Add($"🚨正乖離極大(過熱)：Bias20={bias20:P2}，分數扣減 {biasPenalty}。");
            }
        }
        else
        {
            output.Reasons.Add($"🚀 股價處於強勢多頭動能區，豁免 Bias20 乖離扣分限制。");
        }

        // ----------------------------------------------------
        // 2) 階梯式 MA 反彈計分與技術面強化機制
        // ----------------------------------------------------
        var techBonus = 0d;

        if (currentPrice > 0)
        {
            // 輔助判斷：股價是否落在均線支撐區 (-0.5% ~ +2.5%)
            bool IsInSupportZone(double? ma) => ma.HasValue && ma.Value > 0 &&
                ((currentPrice - ma.Value) / ma.Value >= -0.005d) &&
                ((currentPrice - ma.Value) / ma.Value <= 0.025d);

            // 【階梯1】：MA20 月線支撐測試 (基礎 +6 分)
            if (IsInSupportZone(ma20))
            {
                var baseScore = isMa20Uptrend ? 6d : 3d; // 均線下彎加分折半
                if (volumeRatio <= 0.8d)
                {
                    var pts = baseScore + 4d;
                    techBonus += pts;
                    output.Reasons.Add($"📈 【MA20 月線支撐】股價回測月線且量縮({volumeRatio:P0})，具備短線技術性反彈機率，加分 +{pts:F0}。");
                }
                else if (volumeRatio >= 1.25d)
                {
                    var pts = baseScore + 6d;
                    techBonus += pts;
                    output.Reasons.Add($"🚀 【MA20 月線帶量反彈】股價於月線帶量({volumeRatio:P0})強勢攻擊，具備高彈升機率，加分 +{pts:F0}。");
                }
                else
                {
                    techBonus += baseScore;
                    output.Reasons.Add($"🛡️ 【MA20 月線支撐】股價獲得 MA20 支撐，具備技術性反彈機率，加分 +{baseScore:F0}。");
                }
            }
            // 【階梯2】：MA60 季線生命線支撐測試 (基礎 +10 分)
            else if (IsInSupportZone(ma60))
            {
                var baseScore = 10d;
                if (volumeRatio <= 0.8d)
                {
                    techBonus += baseScore + 4d; // +14
                    output.Reasons.Add($"🚀 【MA60 季線生命線】股價回測季線且量縮沉澱({volumeRatio:P0})，具備波段強反彈高機率，加分 +14。");
                }
                else if (volumeRatio >= 1.25d)
                {
                    techBonus += baseScore + 6d; // +16
                    output.Reasons.Add($"🚀 【MA60 季線帶量反彈】股價於季線帶量({volumeRatio:P0})大舉反攻，極高機率發動波段行情，加分 +16。");
                }
                else
                {
                    techBonus += baseScore; // +10
                    output.Reasons.Add($"🛡️ 【MA60 季線支撐】獲得中期生命線關鍵支撐，預估具備強反彈機率，加分 +10。");
                }
            }
            // 【階梯3】：MA120 半年線支撐測試 (基礎 +14 分)
            else if (IsInSupportZone(ma120) && isAboveMa120)
            {
                var baseScore = 14d;
                if (volumeRatio <= 0.8d)
                {
                    techBonus += baseScore + 4d; // +18
                    output.Reasons.Add($"🛡️ 【MA120 半年線】抵達長線護城河且量縮({volumeRatio:P0})，法人防衛買盤顯現，預期引發中期強勢反彈機率，加分 +18。");
                }
                else if (volumeRatio >= 1.25d)
                {
                    techBonus += baseScore + 6d; // +20
                    output.Reasons.Add($"🏛️ 【MA120 半年線帶量反彈】半年線爆量({volumeRatio:P0})大反彈，法人護盤明確，高機率展開大級別反彈，加分 +20。");
                }
                else
                {
                    techBonus += baseScore; // +14
                    output.Reasons.Add($"🛡️ 【MA120 半年線支撐】半年線防衛線發揮作用，預估具備中期反彈機率，加分 +14。");
                }
            }
            // 【階梯4】：MA240 年線牛熊護城河支撐測試 (基礎 +18 分)
            else if (IsInSupportZone(ma240) && isAboveMa240)
            {
                var baseScore = 18d;
                if (volumeRatio <= 0.8d)
                {
                    techBonus += baseScore + 4d; // +22
                    output.Reasons.Add($"🏛️ 【MA240 年線終極支撐】觸及年線底線且量縮({volumeRatio:P0})，大資金進場護盤，極高機率觸發波段大反彈，加分 +22。");
                }
                else if (volumeRatio >= 1.25d)
                {
                    techBonus += baseScore + 7d; // +25
                    output.Reasons.Add($"🏛️ 【MA240 年線爆量反彈】觸及年線底線且帶量({volumeRatio:P0})強攻，極高機率觸發超級波段大反彈，加分 +25。");
                }
                else
                {
                    techBonus += baseScore; // +18
                    output.Reasons.Add($"🛡️ 【MA240 年線牛熊護城河】獲得年線終極支撐，具備強烈長線反彈機率，加分 +18。");
                }
            }
        }

        // B. 均線結構與排列加扣分
        if (ma5.HasValue && ma20.HasValue && currentPrice > ma5.Value && ma5.Value > ma20.Value && isMa20Uptrend)
        {
            if (isLongTermBullish)
            {
                techBonus += 8d;
                output.Reasons.Add($"🔥 【大多頭排列】Price > MA5 > MA20 > MA60 > MA120，長短期均線多頭發散，加分 +8。");
            }
            else
            {
                techBonus += 5d;
                output.Reasons.Add($"🔥 【短線多頭排列】Price > MA5 > MA20 且月線持續上揚，趨勢強勢加分 +5。");
            }
        }
        else if (ma20.HasValue && ma60.HasValue && ma120.HasValue && (ma20.Value < ma60.Value && ma60.Value < ma120.Value))
        {
            techBonus -= 10d;
            output.Reasons.Add($"❄️ 【空頭排列警告】MA20 < MA60 < MA120 呈空頭架構，扣減分數 10。");
        }

        // C. 年線牛熊護城河扣分
        if (ma240.HasValue && currentPrice < ma240.Value)
        {
            techBonus -= 5d;
            output.Reasons.Add($"⚠️ 股價位於 MA240 年線之下，長線格局偏弱，扣減分數 5。");
        }

        var techScore = Math.Max(0d, Math.Min(100d, adjustedScore + techBonus));

        // ----------------------------------------------------
        // 3) 技術/籌碼動態權重解耦 (偏重技術面)
        // ----------------------------------------------------
        var chipScore = CalculateChipScore(currentRecommendation, recent5DayRecommendations);

        // 預設偏重技術面：技術 60% / 籌碼 40%
        var techWeight = 0.60d;
        var chipWeight = 0.40d;

        if (techScore >= 70d)
        {
            techWeight = 0.75d;
            chipWeight = 0.25d;
            output.Reasons.Add("🔥 啟動【強勢技術主導模式】，技術面權重提升至 75%。");
        }

        var weightedScore = Math.Max(0d, Math.Min(100d, (techScore * techWeight) + (chipScore * chipWeight)));

        // 籌碼防禦機制
        var recentChipSupport = CalculateRecentChipSupport(recent5DayRecommendations);
        if (techScore < 55d && recentChipSupport >= 60d)
        {
            weightedScore = Math.Max(weightedScore, Math.Min(100d, (techScore * 0.4d) + (recentChipSupport * 0.6d)));
            chipDefenseActivated = true;
            chipDefenseText = $"✅技術面拉回但籌碼主力防禦啟動。";
        }

        var currentDayFinalScore = (double)Math.Round(weightedScore);

        // ----------------------------------------------------
        // 4) 決策分數歷史平滑機制 (EMA)
        // ----------------------------------------------------
        double estimatedYesterdayScore = (normalizedHolding / 100d) * (LinearFullScore - LinearStartScore) + LinearStartScore;
        if (normalizedHolding <= 0) estimatedYesterdayScore = 40d;

        var smoothedScore = (normalizedHolding > 0)
            ? (estimatedYesterdayScore * 0.60d) + (currentDayFinalScore * 0.40d)
            : currentDayFinalScore;

        var finalScore = (int)Math.Round(smoothedScore);
        output.Reasons.Add($"今日實算分: {currentDayFinalScore:F0} (含階梯技術加分)，歷史平滑後最終分: {finalScore}");

        // ----------------------------------------------------
        // 5) 硬停損風控 (-7% 機制)
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
                output.Reasons.Add($"觸發 -7% 機械停損");
                return output;
            }
        }

        // ----------------------------------------------------
        // 6) 線性滑動倉位計算與【過熱天花板與鎖倉安全閥】
        // ----------------------------------------------------
        var action = "HOLD";
        var originalTarget = ResolveLinearTargetHolding(finalScore);
        var targetHolding = originalTarget;
        var isOverheat = crashRiskScore >= 75 || string.Equals(currentRecommendation.GlobalDecision, "CRASH_WARNING", StringComparison.OrdinalIgnoreCase);

        // 防線 1：修正過熱天花板限制 (強制壓低部位上限至 30% 以下)
        if (isOverheat)
        {
            targetHolding = Math.Min(30d, targetHolding);
            output.Reasons.Add($"⚠️ 短線極度過熱/觸發崩盤預警，強制控管目標部位上限於 {targetHolding:F0}%。");
        }

        // 防線 2：多階梯均線反彈鎖倉 (含安全閥機制)
        bool isScoreHealthy = finalScore >= 45; // 安全閥：實算分數高於 45 分才允許鎖倉保護

        if (targetHolding < normalizedHolding && normalizedHolding >= 30d)
        {
            if (isScoreHealthy)
            {
                bool IsNearMa(double? ma) => ma.HasValue && ma.Value > 0 && currentPrice > ma.Value && ((currentPrice - ma.Value) / ma.Value <= 0.03d);

                if (IsNearMa(ma20) && isMa20Uptrend)
                {
                    targetHolding = normalizedHolding;
                    output.Reasons.Add("🛡️ 月線反彈鎖倉：股價踩在 MA20 支撐區，為預期反彈點，強制抱緊觀望。");
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
                else if (ma20.HasValue && currentPrice > ma20.Value)
                {
                    targetHolding = normalizedHolding;
                    output.Reasons.Add("🛡️ 多頭鎖倉守護：股價仍穩守 MA20 月線之上，無視今日減碼訊號，強制抱緊鎖倉。");
                }
            }
            else
            {
                output.Reasons.Add($"⚠️ 綜合評分過低 ({finalScore}分)，解除均線鎖倉保護，執行正常減碼。");
            }
        }
        else if (targetHolding < 30d && ma20.HasValue && currentPrice > ma20.Value && normalizedHolding > 0d && isScoreHealthy)
        {
            targetHolding = Math.Max(targetHolding, 30d); // 保底底倉防護
        }

        var diff = targetHolding - normalizedHolding;

        // 7) 判斷交易動作與防震盪緩衝（Deadzone）
        if (Math.Abs(diff) < DeadzoneThreshold)
        {
            action = "HOLD";
            targetHolding = normalizedHolding;
            output.Reasons.Add($"ℹ️ 變動量 {Math.Abs(diff):F1}% 未達緩衝閾值 {DeadzoneThreshold}%，維持原倉位。");
        }
        else
        {
            targetHolding = Math.Round(ClampHolding(targetHolding), 0, MidpointRounding.AwayFromZero);
            if (targetHolding > normalizedHolding + ComparisonEpsilon)
            {
                action = "BUY_LINEAR";
            }
            else if (targetHolding < normalizedHolding - ComparisonEpsilon)
            {
                action = isOverheat ? "EXIT_OVERHEAT" : "REDUCE_LINEAR";
            }
            else
            {
                action = "HOLD";
                targetHolding = normalizedHolding;
            }
        }

        var executedHolding = targetHolding;

        // ----------------------------------------------------
        // 8) 輸出封裝與 UI 資料綁定
        // ----------------------------------------------------
        output.GlobalDecision = action;
        output.CurrentHoldingPercentage = executedHolding;
        output.ExecutedHolding = executedHolding;
        output.StageLabel = BuildLinearHoldingLabel(executedHolding);
        output.ActionText = BuildActionText(action, executedHolding);
        output.Description = BuildDescription(normalizedHolding, finalScore, action, executedHolding, executedHolding);
        output.ActionColor = ResolveActionColor(action);

        if (chipDefenseActivated) output.Description += " " + chipDefenseText;

        // 標記圖表訊號
        if (!string.Equals(action, "HOLD", StringComparison.OrdinalIgnoreCase))
        {
            var isBuy = executedHolding > normalizedHolding;
            string markerText = isBuy ? "加" : "減";
            if (action == "EXIT_OVERHEAT") markerText = "熱";
            AddChartMarker(output, currentPrice, markerText, output.ActionColor, isBuy ? "BUY" : "SELL");
        }

        return output;
    }

    private static void AddChartMarker(StrategyOutputViewModel output, double price, string text, string colorHex, string type)
    {
        output.ChartMarkers.Add(new ChartMarker
        {
            Time = DateTime.Now,
            Text = text,
            Price = price,
            ColorHex = colorHex,
            MarkerType = type
        });
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
        if (recent.Count == 0) return 50d;
        return recent.Average(x => CalculateChipScore(x, null));
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
            if (reason.Contains("[籌碼+") || (reason.Contains("外資") && reason.Contains("買超")) || (reason.Contains("投信") && reason.Contains("買超")) || (reason.Contains("法人") && reason.Contains("買超")))
                chipPositive += value;
            else
                chipNegative += value;
        }

        var chipScore = 50d + chipPositive - chipNegative;
        return Math.Max(0d, Math.Min(100d, chipScore));
    }

    private static double ExtractScoreValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0d;

        // 優先匹配 [籌碼+5] 或 [風險+10] 格式
        var match = Regex.Match(text, @"\[(?:籌碼|風險)[+-]?(\d+(\.\d+)?)\]");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double val))
        {
            return Math.Min(15d, Math.Abs(val)); // 限縮單項上限最大 15 分
        }

        // 備用防護：未標明分數的自然文字，限制最大扣除權重為 5 分，防止張數(如1000張)誤當分數
        var genericMatch = Regex.Match(text, @"[+-]?(\d+(\.\d+)?)");
        if (genericMatch.Success && double.TryParse(genericMatch.Value, out double genericVal))
        {
            return Math.Min(5d, Math.Abs(genericVal));
        }

        return 5d;
    }

    private static double ClampHolding(double value)
    {
        return Math.Max(0d, Math.Min(100d, value));
    }

    private static string BuildLinearHoldingLabel(double holding)
    {
        if (holding <= ComparisonEpsilon) return "線性倉位｜空倉 0%";
        if (holding >= 100d - ComparisonEpsilon) return "線性倉位｜滿倉 100%";
        return $"線性倉位｜{holding:F0}%";
    }

    private static string BuildActionText(string action, double executedHolding)
    {
        switch (action)
        {
            case "EXIT_OVERHEAT": return $"過熱控倉 ({executedHolding:F0}%)";
            case "BUY_LINEAR": return $"線性加碼至 {executedHolding:F0}%";
            case "REDUCE_LINEAR": return $"線性減碼至 {executedHolding:F0}%";
            case "CLEAR": return "停損清倉 (0%)";
            default: return $"HOLD (鎖倉 {executedHolding:F0}%)";
        }
    }

    private static string BuildDescription(double currentHolding, int score, string action, double targetHolding, double executedHolding)
    {
        if (action == "EXIT_OVERHEAT") return $"短線過熱天花板防禦，部位平滑控制於 {executedHolding:F0}%。";
        if (action == "CLEAR") return $"觸發 -7% 機械停損，全數清倉。";
        if (action == "BUY_LINEAR") return $"技術面強勢/階梯支撐，分數 {score}，平滑加碼至 {executedHolding:F0}%。";
        if (action == "REDUCE_LINEAR") return $"指標轉弱破位，分數 {score}，平滑減碼至 {executedHolding:F0}%。";
        return $"維持多頭鎖倉排列/階梯均線支撐，部位穩定抱緊於 {executedHolding:F0}%。";
    }

    private static string ResolveActionColor(string action)
    {
        switch (action)
        {
            case "CLEAR": return "#FF3333";
            case "EXIT_OVERHEAT": return "#E0A040";
            case "BUY_LINEAR": return "#00CC66";
            case "REDUCE_LINEAR": return "#E0A040";
            default: return "#A0A0A0";
        }
    }

    private static double ResolveLinearTargetHolding(int finalScore)
    {
        if (finalScore < LinearStartScore) return 0d;
        if (finalScore > LinearFullScore) return 100d;
        return ((finalScore - LinearStartScore) / (LinearFullScore - LinearStartScore)) * 100d;
    }
}
