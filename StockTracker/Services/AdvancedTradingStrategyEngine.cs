using StockManager.Library;
using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StockTracker.Services
{
    public static class AdvancedTradingStrategyEngine
    {
        private static readonly double[] StageLevels = { 0d, 30d, 60d, 100d };
        private const double ComparisonEpsilon = 0.0001d;
        private const double LinearStartScore = 50d;
        private const double LinearFullScore = 90d;
        private const double DeadzoneThreshold = 8d; // 調高震盪緩衝帶至 8%

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
            double? averageVolume20 = null)
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

            // 1) 正乖離分階懲罰（主升段動能發動期豁免，避免閹割利潤）
            var bias20 = CalculateBias20(currentPrice, ma20);
            var isStrongMomentum = (score >= 75d) || (normalizedHolding >= 50d && currentPrice > ma20);

            if (!isStrongMomentum)
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
                output.Reasons.Add($"🚀 股價處於強勢多頭動能區或已有核心持股，豁免 Bias20 乖離扣分限制。");
            }

            // 2) 技術/籌碼動態權重解耦
            var techScore = Math.Max(0d, Math.Min(100d, adjustedScore));
            var chipScore = CalculateChipScore(currentRecommendation, recent5DayRecommendations);
            var techWeight = 0.4d;
            var chipWeight = 0.6d;

            if (techScore >= 75d)
            {
                techWeight = 0.7d;
                chipWeight = 0.3d;
                output.Reasons.Add("🔥 啟動【動能主導模式】，技術面權重提升至 70%。");
            }

            var weightedScore = Math.Max(0d, Math.Min(100d, (techScore * techWeight) + (chipScore * chipWeight)));

            // 籌碼防禦
            var recentChipSupport = CalculateRecentChipSupport(recent5DayRecommendations);
            if (techScore < 55d && recentChipSupport >= 60d)
            {
                weightedScore = Math.Max(weightedScore, Math.Min(100d, (techScore * 0.3d) + (recentChipSupport * 0.7d)));
                chipDefenseActivated = true;
                chipDefenseText = $"✅技術轉弱但籌碼防禦啟動。";
            }

            var currentDayFinalScore = (double)Math.Round(weightedScore);

            // ----------------------------------------------------
            // 【關鍵抗震修正：決策分數歷史平滑機制 (EMA)】
            // ----------------------------------------------------
            // 透過昨日持股反推昨日的有效決策分數，以此進行平滑，消滅神經質跳水
            double estimatedYesterdayScore = (normalizedHolding / 100d) * (LinearFullScore - LinearStartScore) + LinearStartScore;
            if (normalizedHolding <= 0) estimatedYesterdayScore = 40d; // 空倉預設低分

            // 採用 60% 歷史權重 + 40% 今日新權重 進行平滑
            var smoothedScore = (normalizedHolding > 0)
                ? (estimatedYesterdayScore * 0.60d) + (currentDayFinalScore * 0.40d)
                : currentDayFinalScore;

            var finalScore = (int)Math.Round(smoothedScore);
            output.Reasons.Add($"今日實算分: {currentDayFinalScore:F0}，歷史平滑後最終分: {finalScore} (昨日反推分: {estimatedYesterdayScore:F0})");

            // ----------------------------------------------------
            // 風控最高優先級 (僅保留 -7% 機械硬停損清倉)
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
            // 線性滑動倉位計算與【多頭鎖倉防線】
            // ----------------------------------------------------
            var action = "HOLD";
            var originalTarget = ResolveLinearTargetHolding(finalScore);
            var targetHolding = originalTarget;
            var isOverheat = crashRiskScore >= 75 || string.Equals(currentRecommendation.GlobalDecision, "CRASH_WARNING", StringComparison.OrdinalIgnoreCase);

            // 防線 1：過熱高檔天花板限制
            if (isOverheat)
            {
                if (targetHolding > 50d)
                {
                    targetHolding = Math.Max(50d, normalizedHolding);
                }
                output.Reasons.Add($"⚠️ 短線過熱，啟動天花板限制。目標倉位卡定於：{targetHolding:F0}%。");
            }

            // 防線 2：【終極優化】多頭排列月線鎖倉機制（徹底消滅回撤 30% 尖刺）
            // 如果股價穩守月線之上，且原本抱有核心部位(>=30%)，當今日分數跳水意圖減碼時：
            // 直接強行「鎖倉觀望 (HOLD)」，不允許程式進行任何減碼賣出動作，直到跌破月線為止！
            if (targetHolding < normalizedHolding && ma20.HasValue && currentPrice > ma20.Value && normalizedHolding >= 30d)
            {
                targetHolding = normalizedHolding; // 強行等於昨日持股，不准賣！
                output.Reasons.Add("🛡️ 多頭鎖倉守護：股價仍穩守 MA20 月線之上，無視今日減碼訊號，強制抱緊鎖倉。");
            }
            else if (targetHolding < 30d && ma20.HasValue && currentPrice > ma20.Value && normalizedHolding > 0d)
            {
                // 保底底倉防護
                targetHolding = Math.Max(targetHolding, 30d);
            }

            var diff = targetHolding - normalizedHolding;

            // 判斷交易動作與防震盪緩衝（Deadzone）
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
            // 輸出封裝與 UI 資料綁定
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
            var match = Regex.Match(text, "[+-]?(\\d+(\\.\\d+)?)");
            if (!match.Success) return 0d;
            return double.TryParse(match.Value, out double value) ? Math.Abs(value) : 0d;
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
            if (action == "BUY_LINEAR") return $"多頭升溫，分數 {score}，平滑加碼至 {executedHolding:F0}%。";
            if (action == "REDUCE_LINEAR") return $"指標轉弱，分數 {score}，平滑減碼至 {executedHolding:F0}%。";
            return $"維持多頭鎖倉排列，變動未達閾值，部位穩定抱緊於 {executedHolding:F0}%。";
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
}
