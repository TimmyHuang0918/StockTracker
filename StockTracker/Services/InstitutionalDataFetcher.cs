using Newtonsoft.Json.Linq;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class InstitutionalDataFetcher
    {
        private static readonly HttpClient _httpClient;

        static InstitutionalDataFetcher()
        {
            var handler = new HttpClientHandler { UseCookies = true };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        public async Task<(List<TwseT86Record> T86Records, List<TwseMarginRecord> MarginRecords)> FetchAsync(DateTime date, IProgress<string> progress = null)
        {
            var t86Records = new List<TwseT86Record>();
            var marginRecords = new List<TwseMarginRecord>();

            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                progress?.Report($"⚠️ {date:yyyyMMdd} 為假日，跳過");
                return (t86Records, marginRecords);
            }

            var twseRecords = await GetTwseInstitutionalDataAsync(date);
            var tpexRecords = await GetTpexInstitutionalDataAsync(date);

            t86Records.AddRange(twseRecords);
            t86Records.AddRange(tpexRecords);

            var marginTwse = await GetTwseMarginDataAsync(date);
            marginRecords.AddRange(marginTwse);

            if (t86Records.Count == 0 && marginRecords.Count == 0)
            {
                progress?.Report($"⚠️ {date:yyyyMMdd} 無任何資料");
                return (t86Records, marginRecords);
            }

            progress?.Report($"✅ {date:yyyyMMdd} 下載完成");
            return (t86Records, marginRecords);
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
                        MarginBalance = ParseLongWrapper(item[6]?.ToString()),
                        MarginPurchaseSales = ParseLongWrapper(item[6]?.ToString()) - ParseLongWrapper(item[2]?.ToString()),
                        MarginRedemption = ParseLongWrapper(item[4]?.ToString()),
                        ShortCovering = ParseLongWrapper(item[8]?.ToString()),
                        ShortSales = ParseLongWrapper(item[9]?.ToString()),
                        ShortRedemption = ParseLongWrapper(item[10]?.ToString()),
                        ShortBalance = ParseLongWrapper(item[11]?.ToString())
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

        private long ParseLongWrapper(string val)
        {
            return ParseLong(val);
        }

        private async Task<List<TwseT86Record>> GetTwseInstitutionalDataAsync(DateTime date)
        {
            var url = $"https://www.twse.com.tw/fund/T86?response=csv&date={date:yyyyMMdd}&selectType=ALL";
            try
            {
                var resp = await _httpClient.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                var rawBytes = await resp.Content.ReadAsByteArrayAsync();

                // TWSE returns Big5
                var enc = Encoding.GetEncoding("big5");
                var text = enc.GetString(rawBytes);

                return ParseTwseCsv(text, date);
            }
            catch
            {
                return new List<TwseT86Record>();
            }
        }

        // Parse TWSE CSV text into standardised TWSE_columns, omitting header
        private List<TwseT86Record> ParseTwseCsv(string csvText, DateTime date)
        {
            var lines = csvText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("證券代號"))
                {
                    headerIdx = i;
                    break;
                }
            }

            if (headerIdx == -1) return new List<TwseT86Record>();

            var records = new List<TwseT86Record>();
            var headerCols = lines[headerIdx].Split(',').Select(c => c.Trim('"', ' ', '=')).ToList();

            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line).Select(c => c.Replace(",", "")).ToList();
                if (cols.Count < headerCols.Count) continue;

                var record = new TwseT86Record
                {
                    TradeDate = date.Date,
                    Market = "上市"
                };

                for (int j = 0; j < headerCols.Count; j++)
                {
                    var colName = headerCols[j];
                    string val = cols[j];
                    if (string.IsNullOrWhiteSpace(val)) val = "0";

                    if (colName == "證券代號") record.Symbol = val.Replace("=", "").Trim('"');
                    else if (colName == "證券名稱") record.Name = val.Trim('"');
                    else if (colName == "外陸資買進股數(不含外資自營商)") record.ForeignBuy = ParseLong(val);
                    else if (colName == "外陸資賣出股數(不含外資自營商)") record.ForeignSell = ParseLong(val);
                    else if (colName == "外陸資買賣超股數(不含外資自營商)") record.ForeignNet = ParseLong(val);
                    else if (colName == "外資自營商買進股數") { /* 暫不處理 */ }
                    else if (colName == "外資自營商賣出股數") { /* 暫不處理 */ }
                    else if (colName == "外資自營商買賣超股數") { /* 暫不處理 */ }
                    else if (colName == "投信買進股數") record.InvestmentTrustBuy = ParseLong(val);
                    else if (colName == "投信賣出股數") record.InvestmentTrustSell = ParseLong(val);
                    else if (colName == "投信買賣超股數") record.InvestmentTrustNet = ParseLong(val);
                    else if (colName == "自營商買進股數") { /* maybe Map later if needed */ }
                    else if (colName == "自營商賣出股數") { /* maybe Map later if needed */ }
                    else if (colName == "自營商買賣超股數") record.DealerNet = ParseLong(val);
                    else if (colName == "自營商買進股數(自行買賣)") record.DealerSelfBuy = ParseLong(val);
                    else if (colName == "自營商賣出股數(自行買賣)") record.DealerSelfSell = ParseLong(val);
                    else if (colName == "自營商買賣超股數(自行買賣)") record.DealerSelfNet = ParseLong(val);
                    else if (colName == "自營商買進股數(避險)") record.DealerHedgeBuy = ParseLong(val);
                    else if (colName == "自營商賣出股數(避險)") record.DealerHedgeSell = ParseLong(val);
                    else if (colName == "自營商買賣超股數(避險)") record.DealerHedgeNet = ParseLong(val);
                    else if (colName == "三大法人買賣超股數" || colName == "三大法人買賣超股數合計") record.ThreeMajorNet = ParseLong(val);
                }

                if (!string.IsNullOrWhiteSpace(record.Symbol) && char.IsDigit(record.Symbol.FirstOrDefault()))
                {
                    records.Add(record);
                }
            }

            return records;
        }

        private async Task<List<TwseT86Record>> GetTpexInstitutionalDataAsync(DateTime date)
        {
            var url = $"https://www.tpex.org.tw/www/zh-tw/insti/dailyTrade?type=Daily&sect=AL&date={date:yyyy/MM/dd}&id=&response=csv";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://www.tpex.org.tw/");

            try
            {
                var resp = await _httpClient.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                var rawBytes = await resp.Content.ReadAsByteArrayAsync();

                // TPEx uses ms950/cp950 which big5 will decode
                var enc = Encoding.GetEncoding("big5");
                var text = enc.GetString(rawBytes);

                return ParseTpexCsv(text, date);
            }
            catch
            {
                return new List<TwseT86Record>();
            }
        }

        // Parse TPEx CSV text into standardised TWSE_columns, omitting header
        private List<TwseT86Record> ParseTpexCsv(string csvText, DateTime date)
        {
            var lines = csvText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("代號"))
                {
                    headerIdx = i;
                    break;
                }
            }

            if (headerIdx == -1) return new List<TwseT86Record>();

            var records = new List<TwseT86Record>();
            var headerCols = lines[headerIdx].Split(',').Select(c => c.Trim('"', ' ')).ToList();

            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line).Select(c => c.Replace(",", "")).ToList();
                if (cols.Count < headerCols.Count) continue;

                var record = new TwseT86Record
                {
                    TradeDate = date.Date,
                    Market = "上櫃"
                };

                for (int j = 0; j < headerCols.Count; j++)
                {
                    var colName = headerCols[j];
                    string val = cols[j].Trim('=', '"');
                    if (string.IsNullOrWhiteSpace(val)) val = "0";

                    if (colName == "代號") record.Symbol = val.Replace("=", "").Trim('"');
                    else if (colName == "名稱") record.Name = val.Trim('"');
                    else if (colName == "外資及陸資(不含外資自營商)-買進股數") record.ForeignBuy = ParseLong(val);
                    else if (colName == "外資及陸資(不含外資自營商)-賣出股數") record.ForeignSell = ParseLong(val);
                    else if (colName == "外資及陸資(不含外資自營商)-買賣超股數") record.ForeignNet = ParseLong(val);
                    else if (colName == "外資自營商-買進股數") { }
                    else if (colName == "外資自營商-賣出股數") { }
                    else if (colName == "外資自營商-買賣超股數") { }
                    else if (colName == "投信-買進股數") record.InvestmentTrustBuy = ParseLong(val);
                    else if (colName == "投信-賣出股數") record.InvestmentTrustSell = ParseLong(val);
                    else if (colName == "投信-買賣超股數") record.InvestmentTrustNet = ParseLong(val);
                    else if (colName == "自營商-買進股數") { }
                    else if (colName == "自營商-賣出股數") { }
                    else if (colName == "自營商-買賣超股數") record.DealerNet = ParseLong(val);
                    else if (colName == "自營商(自行買賣)-買進股數") record.DealerSelfBuy = ParseLong(val);
                    else if (colName == "自營商(自行買賣)-賣出股數") record.DealerSelfSell = ParseLong(val);
                    else if (colName == "自營商(自行買賣)-買賣超股數") record.DealerSelfNet = ParseLong(val);
                    else if (colName == "自營商(避險)-買進股數") record.DealerHedgeBuy = ParseLong(val);
                    else if (colName == "自營商(避險)-賣出股數") record.DealerHedgeSell = ParseLong(val);
                    else if (colName == "自營商(避險)-買賣超股數") record.DealerHedgeNet = ParseLong(val);
                    else if (colName == "三大法人買賣超股數合計") record.ThreeMajorNet = ParseLong(val);
                }

                if (!string.IsNullOrWhiteSpace(record.Symbol) && char.IsDigit(record.Symbol.FirstOrDefault()))
                {
                    records.Add(record);
                }
            }

            return records;
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

        private long ParseLong(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;
            var normalized = input.Trim().Trim('"').Replace(",", "");
            if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dval))
                return (long)dval;
            return 0;
        }
    }
}
