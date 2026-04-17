using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class FakeCapitalApiService
    {
        private readonly Dictionary<string, decimal> _latestPrices = new Dictionary<string, decimal>();
        private readonly HashSet<string> _subscribedSymbols = new HashSet<string>();
        private readonly Random _random = new Random();
        private readonly HttpClient _httpClient = new HttpClient();

        private readonly bool _useMockApi;
        private readonly string _loginUrl;
        private readonly string _subscribeUrl;
        private readonly string _quoteUrl;

        public bool IsLoggedIn { get; private set; }

        public FakeCapitalApiService()
        {
            _useMockApi = !string.Equals(Environment.GetEnvironmentVariable("CAPITAL_USE_REAL_API"), "true", StringComparison.OrdinalIgnoreCase);
            _loginUrl = Environment.GetEnvironmentVariable("CAPITAL_LOGIN_URL") ?? "https://api.example.com/capital/login";
            _subscribeUrl = Environment.GetEnvironmentVariable("CAPITAL_SUBSCRIBE_URL") ?? "https://api.example.com/capital/subscribe";
            _quoteUrl = Environment.GetEnvironmentVariable("CAPITAL_QUOTE_URL") ?? "https://api.example.com/capital/quote";
        }

        public async Task<bool> LoginAsync(string account, string password)
        {
            if (!_useMockApi)
            {
                try
                {
                    var content = new StringContent($"{{\"account\":\"{account}\",\"password\":\"{password}\"}}", Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(_loginUrl, content);
                    IsLoggedIn = response.IsSuccessStatusCode;
                    if (IsLoggedIn)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            await Task.Delay(800);
            IsLoggedIn = !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password);
            return IsLoggedIn;
        }

        public async Task SubscribeAsync(string symbol)
        {
            _subscribedSymbols.Add(symbol);

            if (!_useMockApi)
            {
                try
                {
                    var content = new StringContent($"{{\"symbol\":\"{symbol}\"}}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(_subscribeUrl, content);
                }
                catch
                {
                }
            }

            await Task.Delay(100);
            if (!_latestPrices.ContainsKey(symbol))
            {
                _latestPrices[symbol] = _random.Next(30, 600);
            }
        }

        public async Task<IReadOnlyList<StockModel>> GetRealtimeSnapshotAsync()
        {
            if (!_useMockApi)
            {
                try
                {
                    var snapshots = new List<StockModel>();
                    foreach (var symbol in _subscribedSymbols)
                    {
                        var response = await _httpClient.GetAsync($"{_quoteUrl}?symbol={symbol}");
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var payload = await response.Content.ReadAsStringAsync();
                        if (decimal.TryParse(payload, out var price))
                        {
                            snapshots.Add(new StockModel
                            {
                                Symbol = symbol,
                                Name = $"{symbol} Corp",
                                Price = price,
                                Time = DateTime.Now,
                                Volume = _random.Next(500, 12000)
                            });
                        }
                    }

                    if (snapshots.Count > 0)
                    {
                        return snapshots;
                    }
                }
                catch
                {
                }
            }

            return BuildMockSnapshots();
        }

        private IReadOnlyList<StockModel> BuildMockSnapshots()
        {
            var snapshots = new List<StockModel>();

            foreach (var symbol in _latestPrices.Keys.ToList())
            {
                var previous = _latestPrices[symbol];
                var movement = (decimal)(_random.NextDouble() - 0.5) * 2.2m;
                var current = Math.Max(1m, previous + movement);
                _latestPrices[symbol] = current;

                snapshots.Add(new StockModel
                {
                    Symbol = symbol,
                    Name = $"{symbol} Corp",
                    Price = Math.Round(current, 2),
                    Time = DateTime.Now,
                    Volume = _random.Next(500, 12000)
                });
            }

            return snapshots;
        }
    }
}
