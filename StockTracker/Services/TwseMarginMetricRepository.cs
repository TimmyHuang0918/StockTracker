using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class TwseMarginMetricRepository
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Version=3;";

        public TwseMarginMetricRepository(string dbPath)
        {
            _dbPath = dbPath;
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS MarginMetric (
                            TradeDate                 TEXT    NOT NULL,
                            Symbol                    TEXT    NOT NULL,
                            Name                      TEXT    NOT NULL DEFAULT '',
                            Market                    TEXT    NOT NULL DEFAULT '',
                            MarginPurchaseSales       INTEGER NOT NULL DEFAULT 0,
                            MarginBalance             INTEGER NOT NULL DEFAULT 0,
                            Close                     REAL    NOT NULL DEFAULT 0,
                            TotalLoan                 REAL    NOT NULL DEFAULT 0,
                            MarginMaintenanceRatio    REAL    NOT NULL DEFAULT 0,
                            MarginAverageCost         REAL    NOT NULL DEFAULT 0,
                            PRIMARY KEY (TradeDate, Symbol)
                        );
                        CREATE INDEX IF NOT EXISTS idx_marginmetric_date   ON MarginMetric (TradeDate);
                        CREATE INDEX IF NOT EXISTS idx_marginmetric_symbol ON MarginMetric (Symbol);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public Task ReplaceAllAsync(IEnumerable<TwseMarginMetricHistory> histories)
        {
            return Task.Run(() =>
            {
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var deleteCmd = conn.CreateCommand())
                        {
                            deleteCmd.Transaction = tx;
                            deleteCmd.CommandText = "DELETE FROM MarginMetric";
                            deleteCmd.ExecuteNonQuery();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                                INSERT INTO MarginMetric
                                  (TradeDate, Symbol, Name, Market, MarginPurchaseSales, MarginBalance, Close, TotalLoan, MarginMaintenanceRatio, MarginAverageCost)
                                VALUES
                                  (@td, @sym, @nm, @mkt, @mps, @mb, @close, @loan, @ratio, @cost)";

                            foreach (var history in histories ?? Enumerable.Empty<TwseMarginMetricHistory>())
                            {
                                foreach (var metric in history?.RecordsByDate.Values ?? Enumerable.Empty<TwseMarginMetricResult>())
                                {
                                    var record = metric.Record;
                                    if (record == null || string.IsNullOrWhiteSpace(record.Symbol))
                                    {
                                        continue;
                                    }

                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@td", record.TradeDate.ToString("yyyy-MM-dd"));
                                    cmd.Parameters.AddWithValue("@sym", record.Symbol ?? string.Empty);
                                    cmd.Parameters.AddWithValue("@nm", record.Name ?? string.Empty);
                                    cmd.Parameters.AddWithValue("@mkt", record.Market ?? string.Empty);
                                    cmd.Parameters.AddWithValue("@mps", record.MarginPurchaseSales);
                                    cmd.Parameters.AddWithValue("@mb", record.MarginBalance);
                                    cmd.Parameters.AddWithValue("@close", metric.Close);
                                    cmd.Parameters.AddWithValue("@loan", metric.TotalLoan);
                                    cmd.Parameters.AddWithValue("@ratio", metric.MarginMaintenanceRatio);
                                    cmd.Parameters.AddWithValue("@cost", metric.MarginAverageCost);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        tx.Commit();
                    }
                }
            });
        }

        public Task<IReadOnlyList<TwseMarginMetricHistory>> LoadAllHistoriesAsync(IProgress<(int current, int total)> progress = null)
        {
            return Task.Run<IReadOnlyList<TwseMarginMetricHistory>>(() =>
            {
                var metrics = new List<TwseMarginMetricResult>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    var total = 0;
                    using (var countCmd = conn.CreateCommand())
                    {
                        countCmd.CommandText = "SELECT COUNT(*) FROM MarginMetric";
                        total = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    progress?.Report((0, total));

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate, Symbol, Name, Market, MarginPurchaseSales, MarginBalance, Close, TotalLoan, MarginMaintenanceRatio, MarginAverageCost
FROM MarginMetric
ORDER BY Symbol, TradeDate";
                        using (var rdr = cmd.ExecuteReader())
                        {
                            var read = 0;
                            while (rdr.Read())
                            {
                                metrics.Add(new TwseMarginMetricResult
                                {
                                    Record = new TwseMarginRecord
                                    {
                                        TradeDate = DateTime.Parse(rdr.GetString(0)),
                                        Symbol = rdr.GetString(1),
                                        Name = rdr.GetString(2),
                                        Market = rdr.GetString(3),
                                        MarginPurchaseSales = rdr.GetInt64(4),
                                        MarginBalance = rdr.GetInt64(5)
                                    },
                                    Close = Convert.ToDouble(rdr.GetValue(6)),
                                    TotalLoan = Convert.ToDouble(rdr.GetValue(7)),
                                    MarginMaintenanceRatio = Convert.ToDouble(rdr.GetValue(8)),
                                    MarginAverageCost = Convert.ToDouble(rdr.GetValue(9))
                                });
                                read++;
                                if (read % 500 == 0)
                                {
                                    progress?.Report((read, total));
                                }
                            }
                        }
                    }
                }

                progress?.Report((metrics.Count, metrics.Count));

                return metrics
                    .GroupBy(x => x.Record.Symbol)
                    .Select(g => new TwseMarginMetricHistory
                    {
                        Symbol = g.Key,
                        Name = g.Select(x => x.Record.Name).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                        RecordsByDate = g.OrderBy(x => x.Record.TradeDate)
                            .GroupBy(x => x.Record.TradeDate.Date)
                            .ToDictionary(x => x.Key, x => x.Last())
                    })
                    .OrderBy(x => x.Symbol)
                    .ToList();
            });
        }
    }
}
