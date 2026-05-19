using Newtonsoft.Json.Linq;
using StockTracker.Models;
using System;
using System.Collections.Generic;
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
}
