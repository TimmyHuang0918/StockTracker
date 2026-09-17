using System;
using System.Collections.Generic;
using System.Linq;

namespace StockTracker.Models
{
    /// <summary>Transparent, non-executing next-session price scenario.</summary>
    public sealed class TradePlanProposal
    {
        public string Strategy { get; set; }
        public bool IsAvailable { get; set; }
        public string Status { get; set; }
        public string CancelCondition { get; set; }
        public string StructureText { get; set; }
        public decimal EntryLower { get; set; }
        public decimal EntryUpper { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TargetOne { get; set; }
        public decimal TargetTwo { get; set; }
        public PriceStructureZone Support { get; set; }
        public PriceStructureZone BreakoutResistance { get; set; }
        public PriceStructureZone TargetOneResistance { get; set; }
        public PriceStructureZone TargetTwoResistance { get; set; }
    }

    public static class TradePlanCalculator
    {
        public const string Pullback = "拉回承接";
        public const string Breakout = "突破確認";
        public const string Manual = "手動規劃";

        public static TradePlanProposal Create(PriceStructureAnalysis structure, decimal currentPrice, string strategy)
        {
            var result = new TradePlanProposal { Strategy = strategy ?? Pullback };
            if (result.Strategy == Manual)
            {
                result.Status = "手動規劃：不自動填入價格，請依自己的交易條件輸入。";
                result.CancelCondition = "尚未設定情境條件；請自行確認支撐、壓力與可承受風險。";
                return result;
            }
            if (structure == null || !structure.HasSufficientData || currentPrice <= 0m || structure.Atr14 <= 0m)
            {
                result.Status = "日 K 結構或波動資料不足，暫不產生價格計畫。";
                result.CancelCondition = "資料不足時不建立自動計畫；可改為手動規劃。";
                return result;
            }

            var tick = Services.PriceStructureAnalyzer.GetPriceTick(currentPrice);
            var buffer = Math.Max(tick * 2m, structure.Atr14 * 0.20m);
            var deadBand = Math.Max(tick * 2m, structure.Atr14 * 0.15m);
            var supports = (structure.Supports ?? new List<PriceStructureZone>())
                .Where(z => z != null && z.High < currentPrice - deadBand).OrderByDescending(z => z.High).ToList();
            var resistances = (structure.Resistances ?? new List<PriceStructureZone>())
                .Where(z => z != null && z.Low > currentPrice + deadBand).OrderBy(z => z.Low).ToList();

            if (Math.Abs(currentPrice - structure.LatestPrice) > Math.Max(structure.Atr14 * 2m, structure.LatestPrice * 0.06m))
            {
                result.Status = "即時價格已明顯偏離日 K 參考價，請重新確認盤中結構後再建立計畫。";
                result.CancelCondition = "報價偏離日 K 基準過大，不沿用昨日結構直接計算。";
                return result;
            }

            if (result.Strategy == Pullback)
                return CreatePullback(result, currentPrice, structure.Atr14, buffer, supports, resistances);
            return CreateBreakout(result, currentPrice, structure.Atr14, buffer, tick, supports, resistances);
        }

        private static TradePlanProposal CreatePullback(TradePlanProposal result, decimal price, decimal atr, decimal buffer, IList<PriceStructureZone> supports, IList<PriceStructureZone> resistances)
        {
            var support = supports.FirstOrDefault();
            var firstTarget = resistances.FirstOrDefault();
            if (support == null || firstTarget == null)
            {
                result.Status = "找不到現價下方有效支撐或上方有效壓力，拉回計畫不成立。";
                result.CancelCondition = "結構不完整時不承接。";
                return result;
            }
            if (price - support.High > atr * 2m)
            {
                result.Status = "支撐距離現價超過 2 ATR，不是適合隔日等待的拉回區。";
                result.CancelCondition = "未回到有效支撐區前不買進。";
                return result;
            }

            result.Support = support;
            result.TargetOneResistance = firstTarget;
            result.TargetTwoResistance = resistances.Skip(1).FirstOrDefault();
            result.EntryLower = Services.PriceStructureAnalyzer.RoundToTick(support.Low + buffer, true);
            result.EntryUpper = Services.PriceStructureAnalyzer.RoundToTick(support.High, false);
            result.StopLoss = Services.PriceStructureAnalyzer.RoundToTick(support.Low - buffer, false);
            result.TargetOne = BeforeZone(firstTarget, buffer);
            result.TargetTwo = BeforeZone(result.TargetTwoResistance, buffer);
            result.CancelCondition = "價格進入支撐區後，須止穩並重新站回區間中線或短線反彈高點；若跌破結構失效價，不承接。";
            return Validate(result);
        }

        private static TradePlanProposal CreateBreakout(TradePlanProposal result, decimal price, decimal atr, decimal buffer, decimal tick, IList<PriceStructureZone> supports, IList<PriceStructureZone> resistances)
        {
            var breakout = resistances.FirstOrDefault();
            if (breakout == null)
            {
                result.Status = "找不到現價上方有效壓力區，突破計畫不成立。";
                result.CancelCondition = "沒有明確突破門檻時不追價。";
                return result;
            }
            result.BreakoutResistance = breakout;
            result.Support = supports.FirstOrDefault();
            result.EntryLower = Services.PriceStructureAnalyzer.RoundToTick(breakout.High + Math.Max(tick, atr * 0.10m), true);
            result.EntryUpper = Services.PriceStructureAnalyzer.RoundToTick(result.EntryLower + Math.Max(atr * 0.25m, tick * 2m), true);
            if (price > result.EntryUpper + atr * 0.25m)
            {
                result.Status = "現價已跳過突破可進區，隔日計畫改為不追價。";
                result.CancelCondition = "開盤或盤中跳空超過可進區，不追價。";
                return result;
            }

            result.StopLoss = Services.PriceStructureAnalyzer.RoundToTick(breakout.Low - buffer, false);
            var targets = resistances.Where(z => z.Low > result.EntryUpper + buffer).ToList();
            result.TargetOneResistance = targets.ElementAtOrDefault(0);
            result.TargetTwoResistance = targets.ElementAtOrDefault(1);
            result.TargetOne = BeforeZone(result.TargetOneResistance, buffer);
            result.TargetTwo = BeforeZone(result.TargetTwoResistance, buffer);
            result.CancelCondition = "僅在突破壓力區上緣後進入可進區時觀察；若收盤回到壓力區內或跳空過遠，取消計畫。";
            return Validate(result);
        }

        private static TradePlanProposal Validate(TradePlanProposal result)
        {
            if (result.EntryLower <= 0m || result.EntryUpper < result.EntryLower || result.StopLoss <= 0m || result.StopLoss >= result.EntryLower)
            {
                result.Status = "結構區過於狹窄或停損不合理，不建立自動計畫。";
                return result;
            }
            var risk = result.EntryLower - result.StopLoss;
            if (risk / result.EntryLower > 0.06m)
            {
                result.Status = "結構失效距離超過 6%，等待更好的位置，不硬縮停損。";
                return result;
            }
            if (result.TargetOne <= result.EntryUpper || (result.TargetOne - result.EntryLower) / risk < 1.5m)
            {
                result.Status = "第一個有效壓力的空間不足 1.5R，不建議進場。";
                return result;
            }

            result.IsAvailable = true;
            if (result.TargetTwo <= result.TargetOne)
                result.Status = "可觀察：第一個壓力區可作分段減碼；第二個目標結構不足，剩餘部位不預設價格。";
            else
                result.Status = "可觀察：價格、失效價與目標皆由有效結構區推導，仍須由你確認盤中條件。";
            result.StructureText = BuildStructureText(result);
            return result;
        }

        private static decimal BeforeZone(PriceStructureZone zone, decimal buffer)
        {
            return zone == null ? 0m : Services.PriceStructureAnalyzer.RoundToTick(zone.Low - buffer, false);
        }

        private static string BuildStructureText(TradePlanProposal plan)
        {
            var parts = new List<string>();
            if (plan.Support != null) parts.Add("支撐：" + Format(plan.Support));
            if (plan.BreakoutResistance != null) parts.Add("突破壓力：" + Format(plan.BreakoutResistance));
            if (plan.TargetOneResistance != null) parts.Add("目標一壓力：" + Format(plan.TargetOneResistance));
            if (plan.TargetTwoResistance != null) parts.Add("目標二壓力：" + Format(plan.TargetTwoResistance));
            return string.Join("；", parts);
        }

        public static string Format(PriceStructureZone zone)
        {
            return zone == null ? "—" : zone.Low.ToString("F2") + " ～ " + zone.High.ToString("F2") + "（強度 " + zone.Strength + "／觸及 " + zone.Touches + " 次）";
        }
    }
}
