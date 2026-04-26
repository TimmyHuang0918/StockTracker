using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class TwseT86Record
    {
	public string Market { get; set; } // 新增市場欄位
	/// <summary>
	/// 交易日期
	/// </summary>
	public DateTime TradeDate { get; set; }

	/// <summary>
	/// 證券代號
	/// </summary>
	public string Symbol { get; set; }

	/// <summary>
	/// 證券名稱
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// 外陸資買進股數 (不含外資自營商)
	/// </summary>
	public long ForeignBuy { get; set; }

	/// <summary>
	/// 外陸資賣出股數 (不含外資自營商)
	/// </summary>
	public long ForeignSell { get; set; }

	/// <summary>
	/// 外陸資買賣超股數 (不含外資自營商)
	/// </summary>
	public long ForeignNet { get; set; }

	/// <summary>
	/// 投信買進股數
	/// </summary>
	public long InvestmentTrustBuy { get; set; }

	/// <summary>
	/// 投信賣出股數
	/// </summary>
	public long InvestmentTrustSell { get; set; }

	/// <summary>
	/// 投信買賣超股數
	/// </summary>
	public long InvestmentTrustNet { get; set; }

	/// <summary>
	/// 自營商買賣超股數 (合計：自行買賣 + 避險)
	/// </summary>
	public long DealerNet { get; set; }

	/// <summary>
	/// 自營商買進股數 (自行買賣)
	/// </summary>
	public long DealerSelfBuy { get; set; }

	/// <summary>
	/// 自營商賣出股數 (自行買賣)
	/// </summary>
	public long DealerSelfSell { get; set; }

	/// <summary>
	/// 自營商買賣超股數 (自行買賣)
	/// </summary>
	public long DealerSelfNet { get; set; }

	/// <summary>
	/// 自營商買進股數 (避險)
	/// </summary>
	public long DealerHedgeBuy { get; set; }

	/// <summary>
	/// 自營商賣出股數 (避險)
	/// </summary>
	public long DealerHedgeSell { get; set; }

	/// <summary>
	/// 自營商買賣超股數 (避險)
	/// </summary>
	public long DealerHedgeNet { get; set; }

	/// <summary>
	/// 三大法人買賣超股數 (外資 + 投信 + 自營商合計)
	/// </summary>
	public long ThreeMajorNet { get; set; }
    }

    public class TwseT86History
    {
	public string Symbol { get; set; }
	public string Name { get; set; }
	public Dictionary<DateTime, TwseT86Record> RecordsByDate { get; set; } = new Dictionary<DateTime, TwseT86Record>();
    }

    public class TwseT86CsvClient
    {
        public TwseT86CsvClient()
        {

        }

	public async Task<IReadOnlyList<TwseT86History>> ParseAsync(string folderPath, DateTime tradeDate)
	{
	    var result = new List<TwseT86Record>();
	    if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
	    {
		return new List<TwseT86History>();
	    }

	    var files = Directory.GetFiles(folderPath, "T86_ALL_*.csv", SearchOption.TopDirectoryOnly)
		.OrderBy(x => x)
		.ToList();

	    foreach (var file in files)
	    {
		var fileName = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
		var dateText = fileName.StartsWith("T86_ALL_") ? fileName.Substring("T86_ALL_".Length) : string.Empty;
		DateTime fileDate;
		if (!DateTime.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fileDate))
		{
		    continue;
		}

		if (fileDate.Date > tradeDate.Date)
		{
		    continue;
		}

		result.AddRange(ParseCsvFile(file, fileDate));
	    }

	    var histories = result
		.GroupBy(x => x.Symbol)
		.Select(g => new TwseT86History
		{
		    Symbol = g.Key,
		    Name = g.Select(x => x.Name).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
		    RecordsByDate = g
			.OrderBy(x => x.TradeDate)
			.GroupBy(x => x.TradeDate.Date)
			.ToDictionary(x => x.Key, x => x.Last())
		})
		.OrderBy(x => x.Symbol)
		.ToList();

	    await Task.CompletedTask;
	    return histories;
	}

	public static IReadOnlyList<TwseT86Record> ParseCsvFile(string csvFile, DateTime tradeDate)
	{
	    string csv;
	    using (var sr = new StreamReader(csvFile, System.Text.Encoding.UTF8))
	    {
		csv = sr.ReadToEnd();
	    }
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
		if (cols.Count < 23) // 至少要有 23 欄
		{
		    continue;
		}
		cols[2] = cols[2].Replace("=", "");

		// 證券代號在第 3 欄 (cols[2])
		if (string.IsNullOrWhiteSpace(cols[2]) || !char.IsDigit(cols[2].FirstOrDefault()))
		{
		    continue;
		}

		result.Add(new TwseT86Record
		{
		    TradeDate = tradeDate.Date,
		    Market = cols[1], // 直接取 CSV 的市場欄位
		    Symbol = cols[2],
		    Name = cols[3],

		    ForeignBuy = ParseLong(cols[4]),
		    ForeignSell = ParseLong(cols[5]),
		    ForeignNet = ParseLong(cols[6]),

		    InvestmentTrustBuy = ParseLong(cols[10]),
		    InvestmentTrustSell = ParseLong(cols[11]),
		    InvestmentTrustNet = ParseLong(cols[12]),

		    DealerNet = ParseLong(cols[15]),

		    DealerSelfBuy = ParseLong(cols[16]),
		    DealerSelfSell = ParseLong(cols[17]),
		    DealerSelfNet = ParseLong(cols[18]),

		    DealerHedgeBuy = ParseLong(cols[19]),
		    DealerHedgeSell = ParseLong(cols[20]),
		    DealerHedgeNet = ParseLong(cols[21]),

		    ThreeMajorNet = ParseLong(cols[22])
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
	private static long ParseLong(string input)
	{
	    if (string.IsNullOrWhiteSpace(input))
		return 0;

	    // 去掉引號、逗號、空白
	    var normalized = input.Trim().Trim('"').Replace(",", "");

	    // 如果是 "30466564.0" 這種格式，先嘗試轉 double 再轉 long
	    if (normalized.Contains("."))
	    {
		if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var dval))
		{
		    return (long)dval;
		}
	    }

	    // 一般整數格式
	    if (long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
	    {
		return value;
	    }

	    return 0;
	}

    }
}
