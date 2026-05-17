using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class DailyPriceRepository
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Version=3;";

        public DailyPriceRepository(string dbPath)
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
                        CREATE TABLE IF NOT EXISTS DailyPrice (
                            TradeDate    TEXT    NOT NULL,
                            Symbol       TEXT    NOT NULL,
                            Name         TEXT    NOT NULL DEFAULT '',
                            Close        REAL    NOT NULL DEFAULT 0,
                            PRIMARY KEY (TradeDate, Symbol)
                        );
                        CREATE INDEX IF NOT EXISTS idx_dailyprice_date   ON DailyPrice (TradeDate);
                        CREATE INDEX IF NOT EXISTS idx_dailyprice_symbol ON DailyPrice (Symbol);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public HashSet<DateTime> GetExistingDates()
        {
            var result = new HashSet<DateTime>();
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT TradeDate FROM DailyPrice";
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            DateTime date;
                            if (DateTime.TryParse(rdr.GetString(0), out date))
                            {
                                result.Add(date.Date);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public Task UpsertAsync(IEnumerable<DailyCloseRecord> records)
        {
            return Task.Run(() =>
            {
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO DailyPrice
                              (TradeDate, Symbol, Name, Close)
                            VALUES
                              (@td, @sym, @nm, @close)";

                        foreach (var record in records ?? Enumerable.Empty<DailyCloseRecord>())
                        {
                            if (record == null || string.IsNullOrWhiteSpace(record.Symbol))
                            {
                                continue;
                            }

                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@td", record.TradeDate.ToString("yyyy-MM-dd"));
                            cmd.Parameters.AddWithValue("@sym", record.Symbol ?? string.Empty);
                            cmd.Parameters.AddWithValue("@nm", record.Name ?? string.Empty);
                            cmd.Parameters.AddWithValue("@close", record.Close);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }
            });
        }

        public Task<IReadOnlyList<DailyCloseHistory>> LoadAllHistoriesAsync(IProgress<(int current, int total)> progress = null)
        {
            return Task.Run<IReadOnlyList<DailyCloseHistory>>(() =>
            {
                var records = new List<DailyCloseRecord>();
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();
                    var total = 0;
                    using (var countCmd = conn.CreateCommand())
                    {
                        countCmd.CommandText = "SELECT COUNT(*) FROM DailyPrice";
                        total = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    progress?.Report((0, total));

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TradeDate, Symbol, Name, Close
FROM DailyPrice
ORDER BY Symbol, TradeDate";
                        using (var rdr = cmd.ExecuteReader())
                        {
                            var read = 0;
                            while (rdr.Read())
                            {
                                records.Add(new DailyCloseRecord
                                {
                                    TradeDate = DateTime.Parse(rdr.GetString(0)),
                                    Symbol = rdr.GetString(1),
                                    Name = rdr.GetString(2),
                                    Close = Convert.ToDouble(rdr.GetValue(3))
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

                progress?.Report((records.Count, records.Count));

                return records
                    .GroupBy(x => x.Symbol)
                    .Select(g => new DailyCloseHistory
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
    }
}
