using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    /// <summary>讀取期交所公布的臺指選擇權 Put/Call 比率。</summary>
    public sealed class TaifexPutCallRatioService
    {
        private const string SourceUrl = "https://www.taifex.com.tw/cht/3/pcRatio";

        public async Task<IReadOnlyList<PutCallRatioRecord>> GetRecentAsync(int dayCount = 5)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StockTracker");
                    var html = await client.GetStringAsync(SourceUrl);
                    var records = new List<PutCallRatioRecord>();
                    var rows = Regex.Matches(html, @"<tr>\s*<td[^>]*>\s*(?<date>\d{4}/\d{1,2}/\d{1,2})\s*</td>\s*<td[^>]*>[\s\S]*?</td>\s*<td[^>]*>[\s\S]*?</td>\s*<td[^>]*>\s*(?<volume>[\d.]+)\s*</td>\s*<td[^>]*>[\s\S]*?</td>\s*<td[^>]*>[\s\S]*?</td>\s*<td[^>]*>\s*(?<oi>[\d.]+)\s*</td>", RegexOptions.IgnoreCase);
                    foreach (Match row in rows)
                    {
                        DateTime date;
                        decimal volume;
                        decimal openInterest;
                        if (!DateTime.TryParseExact(row.Groups["date"].Value, "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
                            !decimal.TryParse(row.Groups["volume"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out volume) ||
                            !decimal.TryParse(row.Groups["oi"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out openInterest))
                            continue;
                        records.Add(new PutCallRatioRecord { TradeDate = date, VolumeRatioPercent = volume, OpenInterestRatioPercent = openInterest });
                    }
                    return records.OrderByDescending(x => x.TradeDate).Take(Math.Max(1, dayCount)).OrderBy(x => x.TradeDate).ToList();
                }
            }
            catch
            {
                return Array.Empty<PutCallRatioRecord>();
            }
        }
    }
}
