using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class TwseT86Record
    {
        public DateTime TradeDate { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public long ForeignNet { get; set; }
        public long InvestmentTrustNet { get; set; }
        public long DealerNet { get; set; }
        public long ThreeMajorNet { get; set; }
    }

    public class TwseT86CsvClient
    {
        private readonly HttpClient _httpClient;

        public TwseT86CsvClient(HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<IReadOnlyList<TwseT86Record>> DownloadAndParseAsync(DateTime tradeDate, string selectType = "ALL")
        {
	    var url = $"https://www.twse.com.tw/fund/T86?response=csv&date={tradeDate:yyyyMMdd}&selectType={selectType}";
	    var csv = await _httpClient.GetStringAsync(url);
            return ParseCsv(csv, tradeDate);
        }

        public static IReadOnlyList<TwseT86Record> ParseCsv(string csv, DateTime tradeDate)
        {
            var result = new List<TwseT86Record>();
            if (string.IsNullOrWhiteSpace(csv))
            {
                return result;
            }

            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("=") || line.StartsWith("\"證券代號\"") || line.StartsWith("\"說明\""))
                {
                    continue;
                }

                var cols = ParseCsvLine(line);
                if (cols.Count < 19)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cols[0]) || !char.IsDigit(cols[0].FirstOrDefault()))
                {
                    continue;
                }

                result.Add(new TwseT86Record
                {
                    TradeDate = tradeDate.Date,
                    Symbol = cols[0],
                    Name = cols[1],
                    ForeignNet = ParseLong(cols[4]),
                    InvestmentTrustNet = ParseLong(cols[10]),
                    DealerNet = ParseLong(cols[11]),
                    ThreeMajorNet = ParseLong(cols[18])
                });
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
                    continue;
                }

                current += ch;
            }

            values.Add(current.Trim());
            return values;
        }

        private static long ParseLong(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var normalized = text.Replace(",", string.Empty).Trim();
            long value;
            if (long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return 0;
        }
    }
}
