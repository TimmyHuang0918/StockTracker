using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    /// <summary>讀取上市與上櫃三大法人的官方買賣金額彙總資料。</summary>
    public sealed class MarketInstitutionalAmountService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<IReadOnlyDictionary<DateTime, MarketInstitutionalAmount>> GetByDatesAsync(IEnumerable<DateTime> tradeDates)
        {
            var result = new Dictionary<DateTime, MarketInstitutionalAmount>();
            foreach (var tradeDate in (tradeDates ?? Enumerable.Empty<DateTime>()).Select(x => x.Date).Distinct().OrderBy(x => x))
            {
                var twse = await GetTwseAsync(tradeDate);
                var tpex = await GetTpexAsync(tradeDate);
                if (twse == null || tpex == null)
                    continue;

                result[tradeDate] = new MarketInstitutionalAmount
                {
                    TradeDate = tradeDate,
                    ForeignNet = twse.ForeignNet + tpex.ForeignNet,
                    TrustNet = twse.TrustNet + tpex.TrustNet,
                    DealerNet = twse.DealerNet + tpex.DealerNet
                };
            }

            return result;
        }

        private static async Task<MarketInstitutionalAmount> GetTwseAsync(DateTime tradeDate)
        {
            try
            {
                var url = $"https://www.twse.com.tw/rwd/zh/fund/BFI82U?response=json&dayDate={tradeDate:yyyyMMdd}&type=day";
                var json = JObject.Parse(await HttpClient.GetStringAsync(url));
                if (!string.Equals(json["stat"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                    return null;

                var rows = json["data"] as JArray;
                if (rows == null)
                    return null;

                var result = new MarketInstitutionalAmount { TradeDate = tradeDate };
                foreach (var row in rows.OfType<JArray>())
                {
                    var name = row.ElementAtOrDefault(0)?.ToString().Trim() ?? string.Empty;
                    var net = ParseAmount(row.ElementAtOrDefault(3)?.ToString());
                    if (name.StartsWith("外資及陸資(不含外資自營商)", StringComparison.Ordinal))
                        result.ForeignNet = net;
                    else if (string.Equals(name, "投信", StringComparison.Ordinal))
                        result.TrustNet = net;
                    else if (name.StartsWith("自營商(自行買賣)", StringComparison.Ordinal) || name.StartsWith("自營商(避險)", StringComparison.Ordinal))
                        result.DealerNet += net;
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<MarketInstitutionalAmount> GetTpexAsync(DateTime tradeDate)
        {
            try
            {
                var url = $"https://www.tpex.org.tw/www/zh-tw/insti/summary?type=Daily&prod=1&date={tradeDate:yyyyMMdd}&response=json";
                var json = JObject.Parse(await HttpClient.GetStringAsync(url));
                if (!string.Equals(json["stat"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    return null;

                var rows = json["tables"]?.FirstOrDefault()?["data"] as JArray;
                if (rows == null)
                    return null;

                var result = new MarketInstitutionalAmount { TradeDate = tradeDate };
                foreach (var row in rows.OfType<JArray>())
                {
                    var name = row.ElementAtOrDefault(0)?.ToString().Trim() ?? string.Empty;
                    var net = ParseAmount(row.ElementAtOrDefault(3)?.ToString());
                    if (name.StartsWith("外資及陸資(不含自營商)", StringComparison.Ordinal))
                        result.ForeignNet = net;
                    else if (string.Equals(name, "投信", StringComparison.Ordinal))
                        result.TrustNet = net;
                    else if (string.Equals(name, "自營商合計", StringComparison.Ordinal))
                        result.DealerNet = net;
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static decimal ParseAmount(string raw)
        {
            decimal value;
            return decimal.TryParse((raw ?? string.Empty).Replace(",", string.Empty), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
                ? value
                : 0m;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StockTracker");
            return client;
        }
    }

    public sealed class MarketInstitutionalAmount
    {
        public DateTime TradeDate { get; set; }
        public decimal ForeignNet { get; set; }
        public decimal TrustNet { get; set; }
        public decimal DealerNet { get; set; }
        public decimal ThreeMajorNet => ForeignNet + TrustNet + DealerNet;
    }
}
