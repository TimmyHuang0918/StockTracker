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
    /// <summary>Reads the official listed-market credit-trading summary (MI_MARGN).</summary>
    public sealed class TwseMarketCreditService
    {
        private static readonly HttpClient HttpClient;

        static TwseMarketCreditService()
        {
            HttpClient = new HttpClient(new HttpClientHandler { UseCookies = true });
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        public async Task<IReadOnlyDictionary<DateTime, MarketCreditDailyTotal>> GetByDatesAsync(IEnumerable<DateTime> dates)
        {
            var result = new Dictionary<DateTime, MarketCreditDailyTotal>();
            foreach (var date in (dates ?? Enumerable.Empty<DateTime>())
                .Select(x => x.Date)
                .Distinct()
                .OrderBy(x => x))
            {
                var total = await GetAsync(date);
                if (total != null)
                    result[date] = total;
            }

            return result;
        }

        private static async Task<MarketCreditDailyTotal> GetAsync(DateTime date)
        {
            var url = $"https://www.twse.com.tw/rwd/zh/marginTrading/MI_MARGN?response=json&date={date:yyyyMMdd}&selectType=ALL";
            try
            {
                var json = JObject.Parse(await HttpClient.GetStringAsync(url));
                if (!string.Equals(json["stat"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                    return null;

                var summary = (json["tables"] as JArray)
                    ?.OfType<JObject>()
                    .FirstOrDefault(x => (x["title"]?.ToString() ?? string.Empty).Contains("信用交易統計"));
                var rows = summary?["data"] as JArray;
                if (rows == null)
                    return null;

                long marginPrevious = 0;
                long marginCurrent = 0;
                long shortPrevious = 0;
                long shortCurrent = 0;

                foreach (var row in rows.OfType<JArray>())
                {
                    var item = row.ElementAtOrDefault(0)?.ToString() ?? string.Empty;
                    if (item.StartsWith("融資金額", StringComparison.Ordinal))
                    {
                        marginPrevious = ParseLong(row.ElementAtOrDefault(4)?.ToString());
                        marginCurrent = ParseLong(row.ElementAtOrDefault(5)?.ToString());
                    }
                    else if (item.StartsWith("融券", StringComparison.Ordinal))
                    {
                        shortPrevious = ParseLong(row.ElementAtOrDefault(4)?.ToString());
                        shortCurrent = ParseLong(row.ElementAtOrDefault(5)?.ToString());
                    }
                }

                if (marginCurrent == 0 && shortCurrent == 0)
                    return null;

                return new MarketCreditDailyTotal
                {
                    TradeDate = date.Date,
                    MarginAmountThousand = marginCurrent,
                    MarginAmountChangeThousand = marginCurrent - marginPrevious,
                    ShortBalanceLots = shortCurrent,
                    ShortBalanceChangeLots = shortCurrent - shortPrevious
                };
            }
            catch
            {
                return null;
            }
        }

        private static long ParseLong(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var normalized = value.Replace(",", string.Empty).Trim();
            return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }
    }
}
