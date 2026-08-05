using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public sealed class PortfolioRepository
    {
        private readonly string _dbPath;
        private string ConnectionString { get { return "Data Source=" + _dbPath + ";Version=3;"; } }

        public PortfolioRepository(string dbPath)
        {
            _dbPath = dbPath;
            var directory = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS PortfolioTransactions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT NOT NULL, Name TEXT, TradeTime TEXT NOT NULL,
                        Side INTEGER NOT NULL, Quantity TEXT NOT NULL, Price TEXT NOT NULL,
                        Fee TEXT NOT NULL, Note TEXT);";
                    command.ExecuteNonQuery();
                }
            }
        }

        public Task<long> AddAsync(TradeTransaction transaction)
        {
            return Task.Run(() =>
            {
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"INSERT INTO PortfolioTransactions
                            (Symbol, Name, TradeTime, Side, Quantity, Price, Fee, Note)
                            VALUES (@symbol, @name, @time, @side, @quantity, @price, @fee, @note);
                            SELECT last_insert_rowid();";
                        command.Parameters.AddWithValue("@symbol", transaction.Symbol);
                        command.Parameters.AddWithValue("@name", transaction.Name ?? string.Empty);
                        command.Parameters.AddWithValue("@time", transaction.TradeTime.ToString("o", CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@side", (int)transaction.Side);
                        command.Parameters.AddWithValue("@quantity", transaction.Quantity.ToString(CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@price", transaction.Price.ToString(CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@fee", transaction.Fee.ToString(CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@note", transaction.Note ?? string.Empty);
                        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }
                }
            });
        }

        public Task<IReadOnlyList<TradeTransaction>> LoadBySymbolAsync(string symbol)
        {
            return Task.Run<IReadOnlyList<TradeTransaction>>(() =>
            {
                var results = new List<TradeTransaction>();
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Id, Symbol, Name, TradeTime, Side, Quantity, Price, Fee, Note FROM PortfolioTransactions WHERE Symbol = @symbol ORDER BY TradeTime, Id";
                        command.Parameters.AddWithValue("@symbol", symbol);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                results.Add(new TradeTransaction
                                {
                                    Id = reader.GetInt64(0), Symbol = reader.GetString(1), Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                    TradeTime = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                    Side = (TradeSide)reader.GetInt32(4), Quantity = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                                    Price = decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture), Fee = decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                                    Note = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                                });
                            }
                        }
                    }
                }
                return results;
            });
        }
    }
}
