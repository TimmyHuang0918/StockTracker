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
    /// <summary>
    /// Reads TDCC's weekly stock ownership distribution in one batch.  Holding
    /// tiers 12 to 15 represent 400 lots and above; tier 15 is 1,000 lots and above.
    /// </summary>
    public sealed class TdccLargeHolderService
    {
        private const string SourceUrl = "https://openapi.tdcc.com.tw/v1/opendata/1-5";
        private static readonly HttpClient HttpClient;

        static TdccLargeHolderService()
        {
            HttpClient = new HttpClient(new HttpClientHandler { UseCookies = true });
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        public async Task<TdccLargeHolderDataset> GetLatestAsync()
        {
            try
            {
                var rows = JArray.Parse(await HttpClient.GetStringAsync(SourceUrl));
                var grouped = new Dictionary<string, TdccLargeHolderSnapshot>(StringComparer.OrdinalIgnoreCase);
                DateTime dataDate = DateTime.MinValue;

                foreach (var row in rows.OfType<JObject>())
                {
                    var symbol = (row["證券代號"]?.ToString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(symbol))
                        continue;

                    var tier = ParseInt(row["持股分級"]?.ToString());
                    if (tier < 12 || tier > 15)
                    {
                        if (dataDate == DateTime.MinValue)
                            dataDate = ParseDataDate(row);
                        continue;
                    }

                    if (!grouped.TryGetValue(symbol, out var snapshot))
                    {
                        snapshot = new TdccLargeHolderSnapshot { Symbol = symbol };
                        grouped[symbol] = snapshot;
                    }

                    var ratio = ParseDecimal(row["占集保庫存數比例%"]?.ToString());
                    snapshot.Holding400PlusRatio += ratio;
                    if (tier == 15)
                        snapshot.Holding1000PlusRatio += ratio;

                    if (dataDate == DateTime.MinValue)
                        dataDate = ParseDataDate(row);
                }

                if (dataDate == DateTime.MinValue || grouped.Count == 0)
                    return null;

                return new TdccLargeHolderDataset
                {
                    DataDate = dataDate,
                    SnapshotsBySymbol = grouped
                };
            }
            catch
            {
                return null;
            }
        }

        private static DateTime ParseDataDate(JObject row)
        {
            var value = row.Properties()
                .FirstOrDefault(x => x.Name.TrimStart('\uFEFF').Contains("資料日期"))
                ?.Value?.ToString();
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.Date
                : DateTime.MinValue;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private static decimal ParseDecimal(string value)
        {
            return decimal.TryParse((value ?? string.Empty).Replace(",", string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0m;
        }
    }
}
