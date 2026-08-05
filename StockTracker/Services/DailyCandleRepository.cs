using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public sealed class DailyCandleRepository
    {
        private readonly string _dbPath;
        private string ConnectionString { get { return "Data Source=" + _dbPath + ";Version=3;"; } }

        public DailyCandleRepository(string dbPath)
        {
            _dbPath = dbPath;
            var directory = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS BrokerCandles (
                        Symbol TEXT NOT NULL, BarTime TEXT NOT NULL, Open TEXT NOT NULL, High TEXT NOT NULL,
                        Low TEXT NOT NULL, Close TEXT NOT NULL, Volume INTEGER NOT NULL, PRIMARY KEY(Symbol, BarTime));";
                    command.ExecuteNonQuery();
                }
            }
        }

        public Task UpsertAsync(string symbol, IEnumerable<CandleData> candles)
        {
            return Task.Run(() =>
            {
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"INSERT OR REPLACE INTO BrokerCandles
                            (Symbol, BarTime, Open, High, Low, Close, Volume)
                            VALUES (@symbol, @date, @open, @high, @low, @close, @volume);";
                        foreach (var candle in candles ?? Enumerable.Empty<CandleData>())
                        {
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@symbol", symbol);
                            command.Parameters.AddWithValue("@date", candle.Time.ToString("o", CultureInfo.InvariantCulture));
                            command.Parameters.AddWithValue("@open", candle.Open.ToString(CultureInfo.InvariantCulture));
                            command.Parameters.AddWithValue("@high", candle.High.ToString(CultureInfo.InvariantCulture));
                            command.Parameters.AddWithValue("@low", candle.Low.ToString(CultureInfo.InvariantCulture));
                            command.Parameters.AddWithValue("@close", candle.Close.ToString(CultureInfo.InvariantCulture));
                            command.Parameters.AddWithValue("@volume", candle.Volume);
                            command.ExecuteNonQuery();
                        }
                        transaction.Commit();
                    }
                }
            });
        }

        public Task<IReadOnlyList<CandleData>> LoadAsync(string symbol)
        {
            return Task.Run<IReadOnlyList<CandleData>>(() =>
            {
                var results = new List<CandleData>();
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT BarTime, Open, High, Low, Close, Volume FROM BrokerCandles WHERE Symbol=@symbol AND BarTime LIKE '%T00:00:%' ORDER BY BarTime";
                        command.Parameters.AddWithValue("@symbol", symbol);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read()) results.Add(new CandleData
                            {
                                Time = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                                Open = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture), High = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                                Low = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture), Close = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture), Volume = reader.GetInt64(5)
                            });
                        }
                    }
                }
                return results;
            });
        }
    }
}
