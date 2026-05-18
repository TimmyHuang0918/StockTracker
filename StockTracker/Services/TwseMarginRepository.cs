using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    /// <summary>
    /// 負責將融資融券資料存入 SQLite，並提供查詢。
    /// </summary>
    public class TwseMarginRepository
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Version=3;";

        public TwseMarginRepository(string dbPath)
        {
            _dbPath = dbPath;
            EnsureDatabase();
        }

        // ── Schema ──────────────────────────────────────────────────────────
        private void EnsureDatabase()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Margin (
                            TradeDate        TEXT    NOT NULL,
                            Market           TEXT    NOT NULL DEFAULT '',
                            Symbol           TEXT    NOT NULL,
                            Name             TEXT    NOT NULL DEFAULT '',
                            MarginPurchaseSales   INTEGER NOT NULL DEFAULT 0,
                            MarginSales      INTEGER NOT NULL DEFAULT 0,
                            MarginRedemption INTEGER NOT NULL DEFAULT 0,
                            MarginBalance    INTEGER NOT NULL DEFAULT 0,
                            ShortCovering    INTEGER NOT NULL DEFAULT 0,
                            ShortSales       INTEGER NOT NULL DEFAULT 0,
                            ShortRedemption  INTEGER NOT NULL DEFAULT 0,
                            ShortBalance     INTEGER NOT NULL DEFAULT 0,
                            PRIMARY KEY (TradeDate, Symbol)
                        );
                        CREATE INDEX IF NOT EXISTS idx_margin_date   ON Margin (TradeDate);
                        CREATE INDEX IF NOT EXISTS idx_margin_symbol ON Margin (Symbol);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ── 已存在的日期集合 ─────────────────────────────────────────────────
        public HashSet<DateTime> GetExistingDates()
        {
            var result = new HashSet<DateTime>();
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT TradeDate FROM Margin";
                    using (var rdr = cmd.ExecuteReader())
                        while (rdr.Read())
                            if (DateTime.TryParse(rdr.GetString(0), out var d))
                                result.Add(d.Date);
                }
            }
            return result;
        }

        // ── 批次寫入（UPSERT）────────────────────────────────────────────────
        public Task UpsertAsync(IEnumerable<TwseMarginRecord> records)
        {
            return Task.Run(() =>
            {
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                                INSERT OR REPLACE INTO Margin
                                  (TradeDate,Market,Symbol,Name,
                                   MarginPurchaseSales,MarginSales,MarginRedemption,MarginBalance,
                                   ShortCovering,ShortSales,ShortRedemption,ShortBalance)
                                VALUES
                                  (@td,@mkt,@sym,@nm,
                                   @mp,@ms,@mr,@mb,
                                   @sc,@ss,@sr,@sb)";
                            foreach (var r in records)
                            {
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@td", r.TradeDate.ToString("yyyy-MM-dd"));
                                cmd.Parameters.AddWithValue("@mkt", r.Market ?? string.Empty);
                                cmd.Parameters.AddWithValue("@sym", r.Symbol ?? string.Empty);
                                cmd.Parameters.AddWithValue("@nm", r.Name ?? string.Empty);
                                cmd.Parameters.AddWithValue("@mp", r.MarginPurchaseSales);
                                cmd.Parameters.AddWithValue("@ms", r.MarginSales);
                                cmd.Parameters.AddWithValue("@mr", r.MarginRedemption);
                                cmd.Parameters.AddWithValue("@mb", r.MarginBalance);
                                cmd.Parameters.AddWithValue("@sc", r.ShortCovering);
                                cmd.Parameters.AddWithValue("@ss", r.ShortSales);
                                cmd.Parameters.AddWithValue("@sr", r.ShortRedemption);
                                cmd.Parameters.AddWithValue("@sb", r.ShortBalance);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            });
        }

        // ── 查詢全部並組成 TwseMarginHistory 清單 ──────────────────────────────
        public Task<IReadOnlyList<TwseMarginHistory>> LoadAllHistoriesAsync(
            IProgress<(int current, int total)> progress = null)
        {
            return Task.Run<IReadOnlyList<TwseMarginHistory>>(() =>
            {
                var records = new List<TwseMarginRecord>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    // 先取總行數以顯示進度
                    int total = 0;
                    using (var cntCmd = conn.CreateCommand())
                    {
                        cntCmd.CommandText = "SELECT COUNT(*) FROM Margin";
                        total = Convert.ToInt32(cntCmd.ExecuteScalar());
                    }
                    progress?.Report((0, total));

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate,Market,Symbol,Name,
       MarginPurchaseSales,MarginSales,MarginRedemption,MarginBalance,
       ShortCovering,ShortSales,ShortRedemption,ShortBalance
FROM Margin
ORDER BY Symbol, TradeDate";
                        using (var rdr = cmd.ExecuteReader())
                        {
                            int read = 0;
                            while (rdr.Read())
                            {
                                records.Add(new TwseMarginRecord
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    MarginPurchaseSales = rdr.GetInt64(4),
                                    MarginSales = rdr.GetInt64(5),
                                    MarginRedemption = rdr.GetInt64(6),
                                    MarginBalance = rdr.GetInt64(7),
                                    ShortCovering = rdr.GetInt64(8),
                                    ShortSales = rdr.GetInt64(9),
                                    ShortRedemption = rdr.GetInt64(10),
                                    ShortBalance = rdr.GetInt64(11)
                                });
                                read++;
                                if (read % 500 == 0)
                                    progress?.Report((read, total));
                            }
                            progress?.Report((total, total));
                        }
                    }
                }

                var dict = new Dictionary<string, TwseMarginHistory>();
                foreach (var r in records)
                {
                    if (!dict.TryGetValue(r.Symbol, out var hist))
                    {
                        hist = new TwseMarginHistory
                        {
                            Symbol = r.Symbol,
                            Name = r.Name
                        };
                        dict[r.Symbol] = hist;
                    }
                    hist.RecordsByDate[r.TradeDate] = r;
                }

                return dict.Values.ToList();
            });
        }

        public Task<IReadOnlyList<TwseMarginHistory>> LoadHistoriesBySymbolsAsync(IEnumerable<string> symbols)
        {
            return Task.Run<IReadOnlyList<TwseMarginHistory>>(() =>
            {
                var symbolList = (symbols ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (symbolList.Count == 0)
                {
                    return new List<TwseMarginHistory>();
                }

                var records = new List<TwseMarginRecord>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var parameterNames = new List<string>();
                        for (var i = 0; i < symbolList.Count; i++)
                        {
                            var parameterName = "@sym" + i;
                            parameterNames.Add(parameterName);
                            cmd.Parameters.AddWithValue(parameterName, symbolList[i]);
                        }

                        cmd.CommandText = $@"
SELECT TradeDate,Market,Symbol,Name,
       MarginPurchaseSales,MarginSales,MarginRedemption,MarginBalance,
       ShortCovering,ShortSales,ShortRedemption,ShortBalance
FROM Margin
WHERE Symbol IN ({string.Join(",", parameterNames)})
ORDER BY Symbol, TradeDate";

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                records.Add(new TwseMarginRecord
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    MarginPurchaseSales = rdr.GetInt64(4),
                                    MarginSales = rdr.GetInt64(5),
                                    MarginRedemption = rdr.GetInt64(6),
                                    MarginBalance = rdr.GetInt64(7),
                                    ShortCovering = rdr.GetInt64(8),
                                    ShortSales = rdr.GetInt64(9),
                                    ShortRedemption = rdr.GetInt64(10),
                                    ShortBalance = rdr.GetInt64(11)
                                });
                            }
                        }
                    }
                }

                return records
                    .GroupBy(x => x.Symbol)
                    .Select(g => new TwseMarginHistory
                    {
                        Symbol = g.Key,
                        Name = g.Select(x => x.Name).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                        RecordsByDate = g.OrderBy(x => x.TradeDate)
                            .GroupBy(x => x.TradeDate.Date)
                            .ToDictionary(x => x.Key, x => x.Last())
                    })
                    .OrderBy(x => x.Symbol)
                    .ToList();
            });
        }

        public Task<IReadOnlyList<TwseMarginRecord>> LoadByDateAsync(DateTime date)
        {
            return Task.Run<IReadOnlyList<TwseMarginRecord>>(() =>
            {
                var records = new List<TwseMarginRecord>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate,Market,Symbol,Name,
       MarginPurchaseSales,MarginSales,MarginRedemption,MarginBalance,
       ShortCovering,ShortSales,ShortRedemption,ShortBalance
FROM Margin
WHERE TradeDate = @td
ORDER BY Symbol";
                        cmd.Parameters.AddWithValue("@td", date.ToString("yyyy-MM-dd"));
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                records.Add(new TwseMarginRecord
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    MarginPurchaseSales = rdr.GetInt64(4),
                                    MarginSales = rdr.GetInt64(5),
                                    MarginRedemption = rdr.GetInt64(6),
                                    MarginBalance = rdr.GetInt64(7),
                                    ShortCovering = rdr.GetInt64(8),
                                    ShortSales = rdr.GetInt64(9),
                                    ShortRedemption = rdr.GetInt64(10),
                                    ShortBalance = rdr.GetInt64(11)
                                });
                            }
                        }
                    }
                }

                return records;
            });
        }
    }
}
