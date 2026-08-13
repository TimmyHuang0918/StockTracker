using Newtonsoft.Json;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StockTracker.Services
{
    /// <summary>Editable stock-to-group catalog, populated from Capital API symbols.</summary>
    public sealed class StockGroupCatalog
    {
        private readonly string _filePath;
        private readonly Dictionary<string, StockGroupEntry> _entries;

        public StockGroupCatalog(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "stock-groups.json");
            _entries = Load(_filePath);
        }

        public string FilePath => _filePath;

        public StockGroupEntry Find(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            _entries.TryGetValue(symbol.Trim(), out var entry);
            return entry;
        }

        public IReadOnlyList<string> GetGroups(string symbol)
        {
            var entry = Find(symbol);
            if (entry == null || !entry.Enabled) return Array.Empty<string>();
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Industry)) result.Add(entry.Industry);
            if (entry.Themes != null) result.AddRange(entry.Themes.Where(x => !string.IsNullOrWhiteSpace(x)));
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<string> GetCoreGroups(string symbol)
        {
            var entry = Find(symbol);
            if (entry == null || !entry.Enabled) return Array.Empty<string>();
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Industry)) result.Add(entry.Industry);
            if (entry.CoreThemes != null) result.AddRange(entry.CoreThemes.Where(x => !string.IsNullOrWhiteSpace(x)));
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public int SynchronizeDiscoveredStocks(IEnumerable<KeyValuePair<string, string>> stocks)
        {
            var added = 0;
            foreach (var stock in stocks ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                var symbol = stock.Key?.Trim();
                if (string.IsNullOrWhiteSpace(symbol) || _entries.ContainsKey(symbol)) continue;
                _entries[symbol] = new StockGroupEntry
                {
                    Symbol = symbol,
                    Name = stock.Value?.Trim() ?? string.Empty,
                    Industry = "待分類",
                    Themes = new List<string>(),
                    CoreThemes = new List<string>(),
                    Enabled = true,
                    Source = "needs_review"
                };
                added++;
            }
            if (added > 0) Save();
            return added;
        }

        private static Dictionary<string, StockGroupEntry> Load(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, StockGroupEntry>(StringComparer.OrdinalIgnoreCase);
            var entries = JsonConvert.DeserializeObject<List<StockGroupEntry>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<StockGroupEntry>();
            return entries.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol))
                .GroupBy(x => x.Symbol.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            var json = JsonConvert.SerializeObject(_entries.Values.OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase), Formatting.Indented);
            File.WriteAllText(_filePath, json, new UTF8Encoding(false));
        }
    }
}
