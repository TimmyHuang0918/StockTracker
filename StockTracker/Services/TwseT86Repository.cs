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
    /// 負責將三大法人資料存入 SQLite，並提供查詢。
    /// </summary>
    public class TwseT86Repository
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Version=3;";

        public TwseT86Repository(string dbPath)
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
                        CREATE TABLE IF NOT EXISTS T86 (
                            TradeDate       TEXT    NOT NULL,
                            Market          TEXT    NOT NULL DEFAULT '',
                            Symbol          TEXT    NOT NULL,
                            Name            TEXT    NOT NULL DEFAULT '',
                            ForeignBuy      INTEGER NOT NULL DEFAULT 0,
                            ForeignSell     INTEGER NOT NULL DEFAULT 0,
                            ForeignNet      INTEGER NOT NULL DEFAULT 0,
                            InvTrustBuy     INTEGER NOT NULL DEFAULT 0,
                            InvTrustSell    INTEGER NOT NULL DEFAULT 0,
                            InvTrustNet     INTEGER NOT NULL DEFAULT 0,
                            DealerNet       INTEGER NOT NULL DEFAULT 0,
                            DealerSelfBuy   INTEGER NOT NULL DEFAULT 0,
                            DealerSelfSell  INTEGER NOT NULL DEFAULT 0,
                            DealerSelfNet   INTEGER NOT NULL DEFAULT 0,
                            DealerHedgeBuy  INTEGER NOT NULL DEFAULT 0,
                            DealerHedgeSell INTEGER NOT NULL DEFAULT 0,
                            DealerHedgeNet  INTEGER NOT NULL DEFAULT 0,
                            ThreeMajorNet   INTEGER NOT NULL DEFAULT 0,
                            PRIMARY KEY (TradeDate, Symbol)
                        );
                        CREATE INDEX IF NOT EXISTS idx_t86_date   ON T86 (TradeDate);
                        CREATE INDEX IF NOT EXISTS idx_t86_symbol ON T86 (Symbol);";
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
                    cmd.CommandText = "SELECT DISTINCT TradeDate FROM T86";
                    using (var rdr = cmd.ExecuteReader())
                        while (rdr.Read())
                            if (DateTime.TryParse(rdr.GetString(0), out var d))
                                result.Add(d.Date);
                }
            }
            return result;
        }

        public IReadOnlyList<DateTime> GetLatestTradeDates(int count)
        {
            var result = new List<DateTime>();
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT TradeDate FROM T86 ORDER BY TradeDate DESC LIMIT @count";
                    cmd.Parameters.AddWithValue("@count", Math.Max(1, count));
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            DateTime tradeDate;
                            if (DateTime.TryParse(rdr.GetString(0), out tradeDate))
                            {
                                result.Add(tradeDate.Date);
                            }
                        }
                    }
                }
            }

            return result;
        }

        // ── 批次寫入（UPSERT）────────────────────────────────────────────────
        public Task UpsertAsync(IEnumerable<TwseT86Record> records)
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
                                INSERT OR REPLACE INTO T86
                                  (TradeDate,Market,Symbol,Name,
                                   ForeignBuy,ForeignSell,ForeignNet,
                                   InvTrustBuy,InvTrustSell,InvTrustNet,
                                   DealerNet,DealerSelfBuy,DealerSelfSell,DealerSelfNet,
                                   DealerHedgeBuy,DealerHedgeSell,DealerHedgeNet,
                                   ThreeMajorNet)
                                VALUES
                                  (@td,@mkt,@sym,@nm,
                                   @fb,@fs,@fn,
                                   @ib,@is,@in,
                                   @dn,@dsb,@dss,@dsn,
                                   @dhb,@dhs,@dhn,
                                   @tmn)";
                            foreach (var r in records)
                            {
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@td", r.TradeDate.ToString("yyyy-MM-dd"));
                                cmd.Parameters.AddWithValue("@mkt", r.Market ?? string.Empty);
                                cmd.Parameters.AddWithValue("@sym", r.Symbol ?? string.Empty);
                                cmd.Parameters.AddWithValue("@nm", r.Name ?? string.Empty);
                                cmd.Parameters.AddWithValue("@fb", r.ForeignBuy);
                                cmd.Parameters.AddWithValue("@fs", r.ForeignSell);
                                cmd.Parameters.AddWithValue("@fn", r.ForeignNet);
                                cmd.Parameters.AddWithValue("@ib", r.InvestmentTrustBuy);
                                cmd.Parameters.AddWithValue("@is", r.InvestmentTrustSell);
                                cmd.Parameters.AddWithValue("@in", r.InvestmentTrustNet);
                                cmd.Parameters.AddWithValue("@dn", r.DealerNet);
                                cmd.Parameters.AddWithValue("@dsb", r.DealerSelfBuy);
                                cmd.Parameters.AddWithValue("@dss", r.DealerSelfSell);
                                cmd.Parameters.AddWithValue("@dsn", r.DealerSelfNet);
                                cmd.Parameters.AddWithValue("@dhb", r.DealerHedgeBuy);
                                cmd.Parameters.AddWithValue("@dhs", r.DealerHedgeSell);
                                cmd.Parameters.AddWithValue("@dhn", r.DealerHedgeNet);
                                cmd.Parameters.AddWithValue("@tmn", r.ThreeMajorNet);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            });
        }

        // ── 查詢全部並組成 TwseT86History 清單 ──────────────────────────────
        public Task<IReadOnlyList<TwseT86History>> LoadAllHistoriesAsync(
            IProgress<(int current, int total)> progress = null)
        {
            return Task.Run<IReadOnlyList<TwseT86History>>(() =>
            {
                var records = new List<TwseT86Record>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    // 先取總行數以顯示進度
                    int total = 0;
                    using (var cntCmd = conn.CreateCommand())
                    {
                        cntCmd.CommandText = "SELECT COUNT(*) FROM T86";
                        total = Convert.ToInt32(cntCmd.ExecuteScalar());
                    }
                    progress?.Report((0, total));

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate,Market,Symbol,Name,
       ForeignBuy,ForeignSell,ForeignNet,
       InvTrustBuy,InvTrustSell,InvTrustNet,
       DealerNet,DealerSelfBuy,DealerSelfSell,DealerSelfNet,
       DealerHedgeBuy,DealerHedgeSell,DealerHedgeNet,
       ThreeMajorNet
FROM T86
ORDER BY Symbol, TradeDate";
                        using (var rdr = cmd.ExecuteReader())
                        {
                            int read = 0;
                            while (rdr.Read())
                            {
                                records.Add(new TwseT86Record
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    ForeignBuy = rdr.GetInt64(4),
                                    ForeignSell = rdr.GetInt64(5),
                                    ForeignNet = rdr.GetInt64(6),
                                    InvestmentTrustBuy = rdr.GetInt64(7),
                                    InvestmentTrustSell = rdr.GetInt64(8),
                                    InvestmentTrustNet = rdr.GetInt64(9),
                                    DealerNet = rdr.GetInt64(10),
                                    DealerSelfBuy = rdr.GetInt64(11),
                                    DealerSelfSell = rdr.GetInt64(12),
                                    DealerSelfNet = rdr.GetInt64(13),
                                    DealerHedgeBuy = rdr.GetInt64(14),
                                    DealerHedgeSell = rdr.GetInt64(15),
                                    DealerHedgeNet = rdr.GetInt64(16),
                                    ThreeMajorNet = rdr.GetInt64(17)
                                });
                                read++;
                                if (read % 500 == 0)
                                    progress?.Report((read, total));
                            }
                            progress?.Report((total, total));
                        }
                    }
                }

                var histories = records
                    .GroupBy(x => x.Symbol)
                    .Select(g => new TwseT86History
                    {
                        Symbol = g.Key,
                        Name = g.Select(x => x.Name)
                                  .LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                        RecordsByDate = g
                            .OrderBy(x => x.TradeDate)
                            .GroupBy(x => x.TradeDate.Date)
                            .ToDictionary(x => x.Key, x => x.Last())
                    })
                    .OrderBy(x => x.Symbol)
                    .ToList();

                return histories;
            });
        }

        public Task<IReadOnlyList<TwseT86History>> LoadHistoriesBySymbolsAsync(IEnumerable<string> symbols)
        {
            return Task.Run<IReadOnlyList<TwseT86History>>(() =>
            {
                var symbolList = (symbols ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (symbolList.Count == 0)
                {
                    return new List<TwseT86History>();
                }

                var records = new List<TwseT86Record>();
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
       ForeignBuy,ForeignSell,ForeignNet,
       InvTrustBuy,InvTrustSell,InvTrustNet,
       DealerNet,DealerSelfBuy,DealerSelfSell,DealerSelfNet,
       DealerHedgeBuy,DealerHedgeSell,DealerHedgeNet,
       ThreeMajorNet
FROM T86
WHERE Symbol IN ({string.Join(",", parameterNames)})
ORDER BY Symbol, TradeDate";

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                records.Add(new TwseT86Record
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    ForeignBuy = rdr.GetInt64(4),
                                    ForeignSell = rdr.GetInt64(5),
                                    ForeignNet = rdr.GetInt64(6),
                                    InvestmentTrustBuy = rdr.GetInt64(7),
                                    InvestmentTrustSell = rdr.GetInt64(8),
                                    InvestmentTrustNet = rdr.GetInt64(9),
                                    DealerNet = rdr.GetInt64(10),
                                    DealerSelfBuy = rdr.GetInt64(11),
                                    DealerSelfSell = rdr.GetInt64(12),
                                    DealerSelfNet = rdr.GetInt64(13),
                                    DealerHedgeBuy = rdr.GetInt64(14),
                                    DealerHedgeSell = rdr.GetInt64(15),
                                    DealerHedgeNet = rdr.GetInt64(16),
                                    ThreeMajorNet = rdr.GetInt64(17)
                                });
                            }
                        }
                    }
                }

                return records
                    .GroupBy(x => x.Symbol)
                    .Select(g => new TwseT86History
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

        public Task<IReadOnlyList<TwseT86History>> LoadHistoriesBySymbolsAsync(IEnumerable<string> symbols, DateTime? startDate)
        {
            return Task.Run<IReadOnlyList<TwseT86History>>(() =>
            {
                var symbolList = (symbols ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (symbolList.Count == 0)
                {
                    return new List<TwseT86History>();
                }

                var records = new List<TwseT86Record>();
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

                        var dateFilter = string.Empty;
                        if (startDate.HasValue)
                        {
                            dateFilter = " AND TradeDate >= @startDate";
                            cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd"));
                        }

                        cmd.CommandText = $@"
SELECT TradeDate,Market,Symbol,Name,
       ForeignBuy,ForeignSell,ForeignNet,
       InvTrustBuy,InvTrustSell,InvTrustNet,
       DealerNet,DealerSelfBuy,DealerSelfSell,DealerSelfNet,
       DealerHedgeBuy,DealerHedgeSell,DealerHedgeNet,
       ThreeMajorNet
FROM T86
WHERE Symbol IN ({string.Join(",", parameterNames)}){dateFilter}
ORDER BY Symbol, TradeDate";

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                records.Add(new TwseT86Record
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    ForeignBuy = rdr.GetInt64(4),
                                    ForeignSell = rdr.GetInt64(5),
                                    ForeignNet = rdr.GetInt64(6),
                                    InvestmentTrustBuy = rdr.GetInt64(7),
                                    InvestmentTrustSell = rdr.GetInt64(8),
                                    InvestmentTrustNet = rdr.GetInt64(9),
                                    DealerNet = rdr.GetInt64(10),
                                    DealerSelfBuy = rdr.GetInt64(11),
                                    DealerSelfSell = rdr.GetInt64(12),
                                    DealerSelfNet = rdr.GetInt64(13),
                                    DealerHedgeBuy = rdr.GetInt64(14),
                                    DealerHedgeSell = rdr.GetInt64(15),
                                    DealerHedgeNet = rdr.GetInt64(16),
                                    ThreeMajorNet = rdr.GetInt64(17)
                                });
                            }
                        }
                    }
                }

                return records
                    .GroupBy(x => x.Symbol)
                    .Select(g => new TwseT86History
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

        // ── 只針對指定日期查 ──────────────────────────────────────────────────
        public Task<IReadOnlyList<TwseT86Record>> LoadByDateAsync(DateTime date)
        {
            return Task.Run<IReadOnlyList<TwseT86Record>>(() =>
            {
                var records = new List<TwseT86Record>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate,Market,Symbol,Name,
       ForeignBuy,ForeignSell,ForeignNet,
       InvTrustBuy,InvTrustSell,InvTrustNet,
       DealerNet,DealerSelfBuy,DealerSelfSell,DealerSelfNet,
       DealerHedgeBuy,DealerHedgeSell,DealerHedgeNet,
       ThreeMajorNet
FROM T86
WHERE TradeDate = @td";
                        cmd.Parameters.AddWithValue("@td", date.ToString("yyyy-MM-dd"));
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                                records.Add(new TwseT86Record
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Market = rdr.GetString(1),
                                    Symbol = rdr.GetString(2),
                                    Name = rdr.GetString(3),
                                    ForeignBuy = rdr.GetInt64(4),
                                    ForeignSell = rdr.GetInt64(5),
                                    ForeignNet = rdr.GetInt64(6),
                                    InvestmentTrustBuy = rdr.GetInt64(7),
                                    InvestmentTrustSell = rdr.GetInt64(8),
                                    InvestmentTrustNet = rdr.GetInt64(9),
                                    DealerNet = rdr.GetInt64(10),
                                    DealerSelfBuy = rdr.GetInt64(11),
                                    DealerSelfSell = rdr.GetInt64(12),
                                    DealerSelfNet = rdr.GetInt64(13),
                                    DealerHedgeBuy = rdr.GetInt64(14),
                                    DealerHedgeSell = rdr.GetInt64(15),
                                    DealerHedgeNet = rdr.GetInt64(16),
                                    ThreeMajorNet = rdr.GetInt64(17)
                                });
                    }
                }
                return records;
            });
        }
    }
}
