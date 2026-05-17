using Newtonsoft.Json.Linq;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class MarginDataFetcher
    {
        private static readonly HttpClient _httpClient;

        static MarginDataFetcher()
        {
            var handler = new HttpClientHandler { UseCookies = true };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        public async Task<List<TwseMarginRecord>> FetchAsync(DateTime date, IProgress<string> progress = null)
        {
            var records = new List<TwseMarginRecord>();

            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                progress?.Report($"⚠️ {date:yyyyMMdd} 為假日，跳過");
                return records;
            }

            var twseRecords = await GetTwseMarginDataAsync(date);

            records.AddRange(twseRecords);

            if (records.Count == 0)
            {
                progress?.Report($"⚠️ {date:yyyyMMdd} 融資融券無任何資料");
                return records;
            }

            progress?.Report($"✅ {date:yyyyMMdd} 融資融券下載完成");
            return records;
        }

        private async Task<List<TwseMarginRecord>> GetTwseMarginDataAsync(DateTime date)
        {
            var url = $"https://www.twse.com.tw/exchangeReport/TWTA1U?response=json&date={date:yyyyMMdd}";
            try
            {
                var resp = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(resp);

                if (json["stat"]?.ToString() != "OK")
                {
                    return new List<TwseMarginRecord>();
                }

                var data = json["data"];
                if (data == null) return new List<TwseMarginRecord>();

                var records = new List<TwseMarginRecord>();

                foreach (var item in data)
                {
                    var record = new TwseMarginRecord
                    {
                        TradeDate = date.Date,
                        Market = "上市",
                        Symbol = item[0]?.ToString().Trim(),
                        Name = item[1]?.ToString().Trim(),
                        MarginPurchaseSales = ParseLong(item[2]?.ToString()),
                        MarginSales = ParseLong(item[3]?.ToString()),
                        MarginRedemption = ParseLong(item[4]?.ToString()),
                        MarginBalance = ParseLong(item[5]?.ToString()),
                        ShortCovering = ParseLong(item[8]?.ToString()),
                        ShortSales = ParseLong(item[9]?.ToString()),
                        ShortRedemption = ParseLong(item[10]?.ToString()),
                        ShortBalance = ParseLong(item[11]?.ToString())
                    };

                    if (!string.IsNullOrWhiteSpace(record.Symbol))
                    {
                        records.Add(record);
                    }
                }

                return records;
            }
            catch
            {
                return new List<TwseMarginRecord>();
            }
        }

        private long ParseLong(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            val = val.Replace(",", "");
            if (long.TryParse(val, out var result))
                return result;
            return 0;
        }
    }
}
