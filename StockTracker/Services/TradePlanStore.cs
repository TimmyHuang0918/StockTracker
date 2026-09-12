using Newtonsoft.Json;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StockTracker.Services
{
    /// <summary>Stores manual trade-plan notes locally. No plan is sent to a broker.</summary>
    public static class TradePlanStore
    {
        private const string FileName = "TradePlans.json";

        public static void Save(TradePlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.Symbol))
            {
                return;
            }

            var plans = Load().Where(item => !string.Equals(item.Symbol, plan.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            plans.Add(plan);

            var directory = AppDataPathService.GetT86HistoryDirectory();
            var path = Path.Combine(directory, FileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(plans, Formatting.Indented));
        }

        public static IReadOnlyList<TradePlan> Load()
        {
            try
            {
                var path = Path.Combine(AppDataPathService.GetT86HistoryDirectory(), FileName);
                if (!File.Exists(path))
                {
                    return Array.Empty<TradePlan>();
                }

                return JsonConvert.DeserializeObject<List<TradePlan>>(File.ReadAllText(path)) ?? new List<TradePlan>();
            }
            catch
            {
                return Array.Empty<TradePlan>();
            }
        }
    }
}
