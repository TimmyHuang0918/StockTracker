using Newtonsoft.Json.Linq;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class DailyPriceFetcher
    {
        private static readonly HttpClient _httpClient;

        static DailyPriceFetcher()
        {
            var handler = new HttpClientHandler { UseCookies = true };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        public async Task<List<DailyCloseRecord>> FetchAsync(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                return new List<DailyCloseRecord>();
            }

            var records = new List<DailyCloseRecord>();
            records.AddRange(await GetTwseDailyCloseAsync(date));
            records.AddRange(await GetTpexDailyCloseAsync(date));
            return records;
        }

        /// <summary>
        /// 取得交易所最近已公布的全市場正式日線。這是群益歷史日 K 尚未更新時的
        /// 備援資料，僅供補入缺少的最後一個完整交易日，不取代既有群益 K 線。
        /// </summary>
        public async Task<OfficialDailyCandleSnapshot> FetchLatestCompleteCandlesAsync(DateTime referenceDate)
        {
            var latest = new OfficialDailyCandleSnapshot();
            var startDate = referenceDate.Date;

            // 當日資料可能尚未發布，從掃描日向前找最近可用的交易日；
            // 不以單純日曆日期判定，避免週末、休市及跨午夜時誤補資料。
            for (var offset = 0; offset < 10; offset++)
            {
                var date = startDate.AddDays(-offset);
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                var responses = await Task.WhenAll(
                    GetTwseDailyCandlesAsync(date),
                    GetTpexDailyCandlesAsync(date));

                var all = new Dictionary<string, CandleData>(StringComparer.OrdinalIgnoreCase);
                foreach (var candle in responses[0]) all[candle.Key] = candle.Value;
                foreach (var candle in responses[1]) all[candle.Key] = candle.Value;

                // 一個市場的完整官方日行情遠大於 100 檔。低於此數量通常表示
                // 資料尚未發布、格式異常或被暫時擋下，不能當成正式交易日。
                if (all.Count < 100)
                {
                    continue;
                }

                latest.TradeDate = date;
                latest.CandlesBySymbol = all;
                latest.TwseCount = responses[0].Count;
                latest.TpexCount = responses[1].Count;
                latest.Status = $"TWSE {latest.TwseCount:N0} 檔、TPEX {latest.TpexCount:N0} 檔";
                return latest;
            }

            latest.Status = "官方盤後日行情尚未公布或暫時無法取得";
            return latest;
        }

        private async Task<List<DailyCloseRecord>> GetTwseDailyCloseAsync(DateTime date)
        {
            var url = $"https://www.twse.com.tw/exchangeReport/MI_INDEX?response=json&date={date:yyyyMMdd}&type=ALLBUT0999";
            try
            {
                var resp = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(resp);
                if (json["stat"]?.ToString() != "OK")
                {
                    return new List<DailyCloseRecord>();
                }

                var tables = json["tables"] as JArray;
                if (tables == null)
                {
                    return new List<DailyCloseRecord>();
                }

                foreach (var table in tables)
                {
                    var fields = table["fields"] as JArray;
                    var data = table["data"] as JArray;
                    if (fields == null || data == null)
                    {
                        continue;
                    }

                    var symbolIndex = FindFieldIndex(fields, "證券代號");
                    var nameIndex = FindFieldIndex(fields, "證券名稱");
                    var closeIndex = FindFieldIndex(fields, "收盤價");
                    if (symbolIndex < 0 || closeIndex < 0)
                    {
                        continue;
                    }

                    var records = new List<DailyCloseRecord>();
                    foreach (var row in data)
                    {
                        var columns = row as JArray;
                        if (columns == null || columns.Count <= closeIndex)
                        {
                            continue;
                        }

                        var symbol = columns[symbolIndex]?.ToString().Trim();
                        var name = nameIndex >= 0 && columns.Count > nameIndex ? columns[nameIndex]?.ToString().Trim() : string.Empty;
                        var close = ParseClose(columns[closeIndex]?.ToString());
                        if (string.IsNullOrWhiteSpace(symbol) || close <= 0d)
                        {
                            continue;
                        }

                        records.Add(new DailyCloseRecord
                        {
                            TradeDate = date.Date,
                            Symbol = symbol,
                            Name = name,
                            Close = close
                        });
                    }

                    if (records.Count > 0)
                    {
                        return records;
                    }
                }
            }
            catch
            {
            }

            return new List<DailyCloseRecord>();
        }

        private async Task<Dictionary<string, CandleData>> GetTwseDailyCandlesAsync(DateTime date)
        {
            var result = new Dictionary<string, CandleData>(StringComparer.OrdinalIgnoreCase);
            var url = $"https://www.twse.com.tw/exchangeReport/MI_INDEX?response=json&date={date:yyyyMMdd}&type=ALLBUT0999";

            try
            {
                var json = JObject.Parse(await _httpClient.GetStringAsync(url));
                if (!string.Equals(json["stat"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                foreach (var table in (json["tables"] as JArray) ?? new JArray())
                {
                    var fields = table["fields"] as JArray;
                    var data = table["data"] as JArray;
                    if (fields == null || data == null) continue;

                    var symbolIndex = FindFieldIndex(fields, "證券代號");
                    var openIndex = FindFieldIndex(fields, "開盤價");
                    var highIndex = FindFieldIndex(fields, "最高價");
                    var lowIndex = FindFieldIndex(fields, "最低價");
                    var closeIndex = FindFieldIndex(fields, "收盤價");
                    var volumeIndex = FindFieldIndex(fields, "成交股數");
                    if (new[] { symbolIndex, openIndex, highIndex, lowIndex, closeIndex, volumeIndex }.Any(x => x < 0)) continue;

                    foreach (var row in data.OfType<JArray>())
                    {
                        var symbol = ReadColumn(row, symbolIndex);
                        decimal open;
                        decimal high;
                        decimal low;
                        decimal close;
                        long volume;
                        if (string.IsNullOrWhiteSpace(symbol) ||
                            !TryParseDecimal(ReadColumn(row, openIndex), out open) ||
                            !TryParseDecimal(ReadColumn(row, highIndex), out high) ||
                            !TryParseDecimal(ReadColumn(row, lowIndex), out low) ||
                            !TryParseDecimal(ReadColumn(row, closeIndex), out close) ||
                            !TryParseVolumeLots(ReadColumn(row, volumeIndex), "成交股數", out volume) ||
                            open <= 0 || high <= 0 || low <= 0 || close <= 0)
                        {
                            continue;
                        }

                        result[symbol] = new CandleData { Time = date, Open = open, High = high, Low = low, Close = close, Volume = volume };
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private async Task<List<DailyCloseRecord>> GetTpexDailyCloseAsync(DateTime date)
        {
            var url = $"https://www.tpex.org.tw/www/zh-tw/afterTrading/otc?date={date:yyyy}%2F{date:MM}%2F{date:dd}&type=AL&id=&response=csv&order=0&sort=asc";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://www.tpex.org.tw/");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var rawBytes = await response.Content.ReadAsByteArrayAsync();
                var text = Encoding.GetEncoding(950).GetString(rawBytes);
                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var records = new List<DailyCloseRecord>();

                foreach (var line in lines)
                {
                    var columns = ParseCsvLine(line);
                    if (columns.Count < 3)
                    {
                        continue;
                    }

                    var symbol = columns[0].Trim().Trim('=', '"');
                    var name = columns[1].Trim().Trim('"');
                    var close = ParseClose(columns[2]);

                    if (string.Equals(symbol, "代號", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(symbol) || close <= 0d)
                    {
                        continue;
                    }

                    if (!char.IsDigit(symbol[0]))
                    {
                        continue;
                    }

                    records.Add(new DailyCloseRecord
                    {
                        TradeDate = date.Date,
                        Symbol = symbol,
                        Name = name,
                        Close = close
                    });
                }

                return records;
            }
            catch
            {
                return new List<DailyCloseRecord>();
            }
        }

        private async Task<Dictionary<string, CandleData>> GetTpexDailyCandlesAsync(DateTime date)
        {
            var result = new Dictionary<string, CandleData>(StringComparer.OrdinalIgnoreCase);
            var url = $"https://www.tpex.org.tw/www/zh-tw/afterTrading/otc?date={date:yyyy}%2F{date:MM}%2F{date:dd}&type=AL&id=&response=csv&order=0&sort=asc";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://www.tpex.org.tw/");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var text = Encoding.GetEncoding(950).GetString(await response.Content.ReadAsByteArrayAsync());
                var rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ParseCsvLine)
                    .ToList();
                var headerIndex = rows.FindIndex(row => FindColumnIndex(row, "代號", "證券代號") >= 0 && FindColumnIndex(row, "收盤價", "收盤") >= 0);
                if (headerIndex < 0) return result;

                var header = rows[headerIndex];
                var symbolIndex = FindColumnIndex(header, "代號", "證券代號");
                var openIndex = FindColumnIndex(header, "開盤價", "開盤");
                var highIndex = FindColumnIndex(header, "最高價", "最高");
                var lowIndex = FindColumnIndex(header, "最低價", "最低");
                var closeIndex = FindColumnIndex(header, "收盤價", "收盤");
                var volumeIndex = FindColumnIndex(header, "成交股數", "成交千股", "成交張數");
                if (new[] { symbolIndex, openIndex, highIndex, lowIndex, closeIndex, volumeIndex }.Any(x => x < 0)) return result;

                for (var i = headerIndex + 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var symbol = ReadColumn(row, symbolIndex).Trim('=', '"');
                    decimal open;
                    decimal high;
                    decimal low;
                    decimal close;
                    long volume;
                    if (string.IsNullOrWhiteSpace(symbol) || !char.IsDigit(symbol[0]) ||
                        !TryParseDecimal(ReadColumn(row, openIndex), out open) ||
                        !TryParseDecimal(ReadColumn(row, highIndex), out high) ||
                        !TryParseDecimal(ReadColumn(row, lowIndex), out low) ||
                        !TryParseDecimal(ReadColumn(row, closeIndex), out close) ||
                        !TryParseVolumeLots(ReadColumn(row, volumeIndex), header[volumeIndex], out volume) ||
                        open <= 0 || high <= 0 || low <= 0 || close <= 0)
                    {
                        continue;
                    }

                    result[symbol] = new CandleData { Time = date, Open = open, High = high, Low = low, Close = close, Volume = volume };
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = string.Empty;
            var inQuotes = false;

            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    values.Add(current.Trim());
                    current = string.Empty;
                }
                else
                {
                    current += ch;
                }
            }

            values.Add(current.Trim());
            return values;
        }

        private static int FindFieldIndex(JArray fields, string fieldName)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i]?.ToString(), fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindColumnIndex(IReadOnlyList<string> columns, params string[] candidates)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                var value = (columns[i] ?? string.Empty).Trim().Trim('"');
                if (candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))) return i;
            }

            return -1;
        }

        private static string ReadColumn(IList<JToken> columns, int index)
        {
            return index >= 0 && index < columns.Count ? columns[index]?.ToString().Trim() ?? string.Empty : string.Empty;
        }

        private static string ReadColumn(IReadOnlyList<string> columns, int index)
        {
            return index >= 0 && index < columns.Count ? columns[index]?.Trim() ?? string.Empty : string.Empty;
        }

        private static bool TryParseDecimal(string value, out decimal result)
        {
            result = 0m;
            value = (value ?? string.Empty).Replace(",", string.Empty).Trim();
            return value != "--" && value != "---" && value != "----" &&
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseVolumeLots(string value, string header, out long lots)
        {
            lots = 0;
            value = (value ?? string.Empty).Replace(",", string.Empty).Trim();
            long raw;
            if (!long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out raw) || raw < 0) return false;

            header = header ?? string.Empty;
            if (header.Contains("成交股數"))
            {
                lots = raw / 1000;
                return true;
            }

            if (header.Contains("成交千股") || header.Contains("成交張數"))
            {
                lots = raw;
                return true;
            }

            return false;
        }

        private static double ParseClose(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0d;
            }

            value = value.Replace(",", string.Empty).Trim();
            if (value == "--" || value == "---" || value == "----")
            {
                return 0d;
            }

            double close;
            return double.TryParse(value, out close) ? close : 0d;
        }
    }

    public sealed class OfficialDailyCandleSnapshot
    {
        public DateTime TradeDate { get; set; }
        public Dictionary<string, CandleData> CandlesBySymbol { get; set; } = new Dictionary<string, CandleData>(StringComparer.OrdinalIgnoreCase);
        public int TwseCount { get; set; }
        public int TpexCount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
