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
            if (!string.IsNullOrWhiteSpace(entry.Industry)) result.Add(NormalizeGroupName(entry.Industry));
            if (entry.Themes != null) result.AddRange(entry.Themes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeGroupName));
            return result.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<string> GetCoreGroups(string symbol)
        {
            var entry = Find(symbol);
            if (entry == null || !entry.Enabled) return Array.Empty<string>();
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Industry)) result.Add(NormalizeGroupName(entry.Industry));
            if (entry.CoreThemes != null) result.AddRange(entry.CoreThemes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeGroupName));
            return result.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Returns the curated themes used for group momentum.  Official industries stay
        /// available through GetGroups, but do not compete with investable themes in the
        /// group leaderboard.  Detailed PCB labels roll up to the PCB parent theme.
        /// </summary>
        public IReadOnlyList<string> GetCoreThemeGroups(string symbol)
        {
            var entry = Find(symbol);
            if (entry == null || !entry.Enabled) return Array.Empty<string>();
            return (entry.CoreThemes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeCoreThemeName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<string> GetGroupNames()
        {
            return _entries.Values
                .Where(x => x != null && x.Enabled)
                .SelectMany(x => GetGroups(x.Symbol))
                .Where(x => !string.IsNullOrWhiteSpace(x) &&
                    !string.Equals(x, "待分類", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCulture)
                .ToList();
        }

        public IReadOnlyList<StockGroupEntry> GetEntriesForGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return Array.Empty<StockGroupEntry>();
            var group = NormalizeGroupName(groupName);
            return _entries.Values
                .Where(x => x != null && x.Enabled && GetGroups(x.Symbol)
                    .Any(value => string.Equals(value, group, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool AddStockToGroup(string groupName, string symbol, string name)
        {
            if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(symbol)) return false;
            var group = NormalizeGroupName(groupName);
            if (string.Equals(group, "待分類", StringComparison.OrdinalIgnoreCase)) return false;

            var normalizedSymbol = symbol.Trim();
            if (!_entries.TryGetValue(normalizedSymbol, out var entry))
            {
                entry = new StockGroupEntry
                {
                    Symbol = normalizedSymbol,
                    Name = name?.Trim() ?? string.Empty,
                    Industry = "待分類",
                    Themes = new List<string>(),
                    CoreThemes = new List<string>(),
                    Enabled = true,
                    Source = "local_editor"
                };
                _entries[normalizedSymbol] = entry;
            }

            if (!string.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
            entry.Enabled = true;
            entry.Themes = entry.Themes ?? new List<string>();
            entry.CoreThemes = entry.CoreThemes ?? new List<string>();
            if (!entry.Themes.Any(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase))) entry.Themes.Add(group);
            if (!entry.CoreThemes.Any(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase))) entry.CoreThemes.Add(group);
            Save();
            return true;
        }

        public bool RemoveStockFromGroup(string groupName, string symbol)
        {
            if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(symbol) ||
                !_entries.TryGetValue(symbol.Trim(), out var entry)) return false;

            var group = groupName.Trim();
            var changed = false;
            if (string.Equals(NormalizeGroupName(entry.Industry), group, StringComparison.OrdinalIgnoreCase))
            {
                entry.Industry = "待分類";
                changed = true;
            }
            changed |= (entry.Themes?.RemoveAll(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            changed |= (entry.CoreThemes?.RemoveAll(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            if (changed) Save();
            return changed;
        }

        public bool DeleteGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName) || string.Equals(groupName.Trim(), "待分類", StringComparison.OrdinalIgnoreCase)) return false;
            var changed = false;
            foreach (var entry in _entries.Values)
            {
                changed |= RemoveGroupFromEntry(entry, groupName.Trim());
            }
            if (changed) Save();
            return changed;
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

        private static bool RemoveGroupFromEntry(StockGroupEntry entry, string groupName)
        {
            var changed = false;
            var group = NormalizeGroupName(groupName);
            if (string.Equals(NormalizeGroupName(entry.Industry), group, StringComparison.OrdinalIgnoreCase))
            {
                entry.Industry = "待分類";
                changed = true;
            }
            changed |= (entry.Themes?.RemoveAll(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            changed |= (entry.CoreThemes?.RemoveAll(x => string.Equals(NormalizeGroupName(x), group, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            return changed;
        }

        private static string NormalizeGroupName(string groupName)
        {
            var group = groupName?.Trim() ?? string.Empty;
            if (group == "防禦型") return string.Empty;
            if (group == "IC載板") return string.Empty;
            if (group.Contains("記憶體")) return "記憶體";
            switch (group)
            {
                case "綠能": return "綠能環保";
                case "金融": return "金融保險業";
                case "公用事業": return "油電燃氣業";
                case "網通": return "通信網路業";
                case "AI伺服器／整機": return "AI伺服器";
                case "半導體業": return "半導體";
                case "生技醫療業": return "生技醫療";
                case "光電業": return "光電";
                case "其他電子業": return "其他電子";
                case "航運業": return "航運";
                case "資訊服務業": return "資訊服務";
                case "電子通路業": return "電子通路";
                case "電子零組件業": return "電子零組件";
                case "水泥": return "水泥工業";
                case "汽車": return "汽車／電動車";
                case "電腦及週邊設備業": return "電腦及週邊";
                case "PCB／HDI": return "PCB";
                default: return group;
            }
        }

        private static string NormalizeCoreThemeName(string groupName)
        {
            var group = NormalizeGroupName(groupName);
            return group.StartsWith("PCB", StringComparison.OrdinalIgnoreCase) ? "PCB" : group;
        }
    }
}
