using Newtonsoft.Json.Linq;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public sealed class MarketTradingOverviewService
    {
        private static readonly HttpClient Client = CreateClient();
        public static DateTime TaipeiNow => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time");

        public async Task<MarketTradingOverview> GetRecentAsync(DateTime taipeiNow)
        {
            var markets = await Task.WhenAll(LoadMarketAsync("TWSE", taipeiNow), LoadMarketAsync("TPEX", taipeiNow)).ConfigureAwait(false);
            return new MarketTradingOverview { RetrievedAt = taipeiNow, Markets = markets.ToList() };
        }

        private async Task<MarketTradingSeries> LoadMarketAsync(string code, DateTime now)
        {
            var result = new MarketTradingSeries
            {
                Code = code,
                Name = code == "TWSE" ? "上市 · 加權指數" : "上櫃 · 櫃買指數",
                SourceUrl = code == "TWSE" ? "https://www.twse.com.tw/zh/trading/historical/fmtqik.html" : "https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_index/st41_result.php?l=zh-tw&o=json",
                Scope = code == "TWSE" ? "證交所市場成交資訊（依官方報表範圍）" : "上櫃股票：等價、零股、盤後定價交易；不含鉅額。"
            };
            var records = new List<MarketTradingDay>();
            var missingMonths = new HashSet<DateTime>();
            var month = new DateTime(now.Year, now.Month, 1);
            for (var i = 0; i < 3; i++)
            {
                var requested = month.AddMonths(-i);
                var url = code == "TWSE"
                    ? $"https://www.twse.com.tw/rwd/zh/afterTrading/FMTQIK?date={requested:yyyyMMdd}&response=json"
                    : $"https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_index/st41_result.php?l=zh-tw&d={requested.Year - 1911}/{requested.Month:00}&o=json";
                try
                {
                    var json = JObject.Parse(await Client.GetStringAsync(url).ConfigureAwait(false));
                    var parsed = ParseMonth(json, code, requested);
                    if (parsed.Count == 0) missingMonths.Add(requested);
                    records.AddRange(parsed);
                    if (code == "TWSE" && i == 0 && json["notes"] is JArray notes)
                        result.Scope = string.Join(" ", notes.Select(x => System.Text.RegularExpressions.Regex.Replace(x.ToString(), "<[^>]+>", "")));
                }
                catch (Exception ex)
                {
                    missingMonths.Add(requested);
                    System.Diagnostics.Debug.WriteLine($"Market turnover {code} {requested:yyyyMM}: {ex.Message}");
                }
            }
            result.Days = CalculateRecent(records, now, missingMonths);
            result.Status = result.Latest == null ? "來源暫無可用資料，請稍後重新掃描。"
                : result.Latest.TradeDate < now.Date ? $"最新可用資料：{result.Latest.DateText}；當日資料尚未公布或非交易日。"
                : result.Latest.SessionText;
            return result;
        }

        // Read fields by name and reject unknown units instead of silently scaling them.
        public static List<MarketTradingDay> ParseMonth(JObject json, string code, DateTime requestedMonth)
        {
            var output = new List<MarketTradingDay>();
            if (!string.Equals((string)json["stat"], "ok", StringComparison.OrdinalIgnoreCase)) return output;
            var table = code == "TWSE" ? json : (json["tables"] as JArray)?.OfType<JObject>().FirstOrDefault(t => ((string)t["title"] ?? "").Contains("日成交量值指數"));
            if (!(table?["fields"] is JArray fields) || !(table["data"] is JArray rows)) return output;
            var names = fields.Select(f => f.ToString().Replace(" ", "")).ToList();
            var dateColumn = names.IndexOf("日期");
            var volumeColumn = names.FindIndex(f => f == "成交股數" || f == "成交張數");
            var amountColumn = names.FindIndex(f => f == "成交金額" || f == "金額（仟元）" || f == "金額(仟元)");
            var indexColumn = names.FindIndex(f => f == "發行量加權股價指數" || f == "櫃買指數");
            var changeColumn = names.FindIndex(f => f == "漲跌點數" || f == "漲/跌");
            if (new[] { dateColumn, volumeColumn, amountColumn, indexColumn, changeColumn }.Any(i => i < 0)) return output;
            foreach (var row in rows.OfType<JArray>())
            {
                DateTime date;
                decimal volume, amount, close, change;
                if (!TryDate((string)row.ElementAtOrDefault(dateColumn), out date) || date.Year != requestedMonth.Year || date.Month != requestedMonth.Month ||
                    !TryNumber(row.ElementAtOrDefault(volumeColumn), out volume) || !TryNumber(row.ElementAtOrDefault(amountColumn), out amount) ||
                    !TryNumber(row.ElementAtOrDefault(indexColumn), out close) || !TryNumber(row.ElementAtOrDefault(changeColumn), out change) ||
                    volume < 0 || amount < 0 || close <= 0) continue;
                output.Add(new MarketTradingDay { TradeDate = date, IndexClose = close, IndexChange = change,
                    VolumeShares = volume * (names[volumeColumn] == "成交張數" ? 1000m : 1m),
                    TurnoverNtd = amount * (names[amountColumn].Contains("仟元") ? 1000m : 1m) });
            }
            return output;
        }

        public static List<MarketTradingDay> CalculateRecent(IEnumerable<MarketTradingDay> source, DateTime now, ISet<DateTime> missingMonths = null)
        {
            var rows = source.Where(x => x.TradeDate <= now.Date).GroupBy(x => x.TradeDate).Select(g => g.Last()).OrderBy(x => x.TradeDate).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                // Both daily reports may still be revised after the close. Treat today's data as provisional until 18:00.
                row.IsComplete = row.TradeDate < now.Date || now.TimeOfDay >= TimeSpan.FromHours(18);
                row.SessionText = row.IsComplete ? "盤後資料" : now.TimeOfDay < new TimeSpan(13, 30, 0) ? $"盤中累計 · 擷取 {now:HH:mm}" : $"盤後更新中 · 擷取 {now:HH:mm}";
                row.TurnoverChangePercent = null;
                row.TurnoverRatio20 = null;
                if (!row.IsComplete) continue;
                if (i > 0 && rows[i - 1].TurnoverNtd > 0 && !HasMissingMonth(rows[i - 1].TradeDate, row.TradeDate, missingMonths))
                    row.TurnoverChangePercent = (row.TurnoverNtd / rows[i - 1].TurnoverNtd - 1m) * 100m;
                // Exclude this day from its baseline; require all 20 preceding sessions.
                if (i >= 20 && !HasMissingMonth(rows[i - 20].TradeDate, row.TradeDate, missingMonths))
                {
                    var average = rows.Skip(i - 20).Take(20).Average(x => x.TurnoverNtd);
                    if (average > 0) row.TurnoverRatio20 = row.TurnoverNtd / average;
                }
            }
            return rows.OrderByDescending(x => x.TradeDate).Take(5).ToList();
        }

        private static bool HasMissingMonth(DateTime from, DateTime to, ISet<DateTime> missing) => missing != null && missing.Any(x => x >= new DateTime(from.Year, from.Month, 1) && x <= to);
        private static bool TryNumber(JToken token, out decimal value) => decimal.TryParse((token?.ToString() ?? "").Replace(",", "").Replace("−", "-").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        private static bool TryDate(string raw, out DateTime date)
        {
            date = DateTime.MinValue;
            var parts = (raw ?? "").Split('/');
            int year, month, day;
            if (parts.Length != 3 || !int.TryParse(parts[0], out year) || !int.TryParse(parts[1], out month) || !int.TryParse(parts[2], out day)) return false;
            if (year < 1911) year += 1911;
            return DateTime.TryParseExact($"{year:0000}/{month:00}/{day:00}", "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StockTracker");
            return client;
        }
    }
}
