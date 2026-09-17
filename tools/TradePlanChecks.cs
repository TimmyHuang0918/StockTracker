using StockTracker.Models;
using System;
using System.Collections.Generic;

public static class TradePlanChecks
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    public static int Main()
    {
        try
        {
            var structure = new PriceStructureAnalysis
            {
                HasSufficientData = true,
                LatestPrice = 100m,
                Atr14 = 5m,
                Supports = new List<PriceStructureZone> { Zone(94m, 95m), Zone(88m, 89m) },
                Resistances = new List<PriceStructureZone> { Zone(105m, 106m), Zone(115m, 116m), Zone(125m, 126m) }
            };

            var pullback = TradePlanCalculator.Create(structure, 100m, TradePlanCalculator.Pullback);
            Check(pullback.IsAvailable, "pullback valid structure");
            Check(pullback.EntryLower == 95m && pullback.EntryUpper == 95m && pullback.StopLoss == 93m, "pullback entry and invalidation use support zone plus ATR buffer");
            Check(pullback.TargetOne == 104m && pullback.TargetTwo == 114m, "pullback targets use real upper resistance zones");
            Check(pullback.CancelCondition.Contains("止穩"), "pullback has confirmation condition");

            var breakout = TradePlanCalculator.Create(structure, 100m, TradePlanCalculator.Breakout);
            Check(breakout.IsAvailable, "breakout valid structure");
            Check(breakout.EntryLower == 106.5m && breakout.EntryUpper == 108m && breakout.StopLoss == 104m, "breakout trigger and invalidation use pressure zone");
            Check(breakout.TargetOne == 114m && breakout.TargetTwo == 124m, "breakout targets exclude breakout pressure itself");

            var overlapping = new PriceStructureAnalysis
            {
                HasSufficientData = true, LatestPrice = 100m, Atr14 = 5m,
                Supports = new List<PriceStructureZone> { Zone(99m, 101m) },
                Resistances = new List<PriceStructureZone> { Zone(99m, 101m) }
            };
            Check(!TradePlanCalculator.Create(overlapping, 100m, TradePlanCalculator.Pullback).IsAvailable, "overlapping current range is not a support entry");
            Check(!TradePlanCalculator.Create(structure, 120m, TradePlanCalculator.Pullback).IsAvailable, "large reference-price drift blocks stale daily plan");
            Check(!TradePlanCalculator.Create(structure, 100m, TradePlanCalculator.Manual).IsAvailable, "manual mode never invents trade prices");

            var insufficient = new PriceStructureAnalysis { HasSufficientData = true, LatestPrice = 100m, Atr14 = 5m, Supports = new List<PriceStructureZone> { Zone(95m, 96m) }, Resistances = new List<PriceStructureZone> { Zone(100.5m, 101m) } };
            Check(!TradePlanCalculator.Create(insufficient, 100m, TradePlanCalculator.Pullback).IsAvailable, "insufficient reward space is rejected");
            Console.WriteLine("PASS total " + passed);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static PriceStructureZone Zone(decimal low, decimal high) { return new PriceStructureZone { Low = low, High = high, Strength = 70, Touches = 3, Basis = "test" }; }
}
