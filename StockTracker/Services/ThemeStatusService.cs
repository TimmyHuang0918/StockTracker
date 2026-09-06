using Newtonsoft.Json;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StockTracker.Services
{
    /// <summary>Builds daily theme state exclusively from metrics supplied by the user's market API.</summary>
    public sealed class ThemeStatusService
    {
        private readonly StockGroupCatalog _catalog;
        private readonly string _filePath;

        public ThemeStatusService(StockGroupCatalog catalog, string filePath = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "theme-status.json");
        }

        public IReadOnlyList<ThemeStatus> BuildAndSave(IEnumerable<ThemeStockMetric> metrics, DateTime asOfDate)
        {
            var grouped = new Dictionary<string, List<ThemeStockMetric>>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in metrics ?? Enumerable.Empty<ThemeStockMetric>())
            {
                foreach (var group in _catalog.GetCoreThemeGroups(metric.Symbol)
                    .Where(x => !string.Equals(x, "待分類", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!grouped.TryGetValue(group, out var members))
                    {
                        members = new List<ThemeStockMetric>();
                        grouped[group] = members;
                    }
                    members.Add(metric);
                }
            }

            var statuses = grouped.Where(pair => pair.Value.Count >= 3)
                .Select(pair => BuildStatus(pair.Key, pair.Value, asOfDate))
                .OrderByDescending(x => x.AverageChange1D)
                .ThenByDescending(x => x.AdvanceRatioPercent)
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(statuses, Formatting.Indented), new UTF8Encoding(false));
            return statuses;
        }

        private static ThemeStatus BuildStatus(string theme, List<ThemeStockMetric> stocks, DateTime asOfDate)
        {
            var count = stocks.Count;
            var up = stocks.Count(x => x.Change1D > 0m);
            var down = stocks.Count(x => x.Change1D < 0m);
            var avg1 = count == 0 ? 0m : stocks.Average(x => x.Change1D);
            var avg5 = count == 0 ? 0m : stocks.Average(x => x.Change5D);
            var volume = count == 0 ? 0m : stocks.Average(x => x.VolumeRatio20D);
            var advanceRatio = up + down == 0 ? 0m : up * 100m / (up + down);

            return new ThemeStatus
            {
                Theme = theme,
                AsOfDate = asOfDate.Date,
                StockCount = count,
                AdvancingCount = up,
                DecliningCount = down,
                AdvanceRatioPercent = advanceRatio,
                AverageChange1D = avg1,
                AverageChange5D = avg5,
                AverageVolumeRatio20D = volume,
                MarketStatus = ResolveStatus(avg1, avg5, advanceRatio, volume),
                Source = "user_api_market_data",
                LeadingSymbols = stocks.OrderByDescending(x => x.Change1D).Take(3).Select(x => x.Symbol).ToList(),
                WeakSymbols = stocks.OrderBy(x => x.Change1D).Take(3).Select(x => x.Symbol).ToList()
            };
        }

        private static string ResolveStatus(decimal change1D, decimal change5D, decimal advanceRatio, decimal volumeRatio)
        {
            if (change1D > 0m && change5D > 0m && advanceRatio >= 60m && volumeRatio >= 1.10m) return "升溫";
            if (change5D > 0m && advanceRatio >= 55m) return "延續";
            if (change1D < 0m && change5D < 0m && advanceRatio <= 40m) return "轉弱";
            if (change5D < 0m || advanceRatio < 45m) return "降溫";
            return "整理";
        }
    }
}
