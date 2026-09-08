using Mscc.GenerativeAI.Types;
using SKCOMLib;
using StockManager.Services;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Navigation;

namespace StockTracker.Services
{
    public class CapitalApiService
    {
        private readonly HashSet<string> _subscribedSymbols = new HashSet<string>();
        private SKAPI _api = App.Api;
        private bool _isSkEventsRegistered;
        private bool _isSkQuoteConnectionReady;
        private TaskCompletionSource<bool> _quoteConnectionReadyTcs;

        private string Account = string.Empty;
        private string Password = string.Empty;

        public event Action<string, CandleData> KLineDataReceived;
        public event Action<string, SKSTOCKLONG> InstantDataRecevied;
        public event Action<string, CandleData> InstantCandleReceived;

        public Tuple<int, string>  ServiceStatus
        {
            get
            {
                int nCode = _api.SKQuoteLib_IsConnected();
                string msg;
                switch (nCode)
                {
                    case 0:
                        msg = "Disconnected";
                        break;
                    case 1:
                        msg = "Is Connected";
                        break;
                    case 2:
                        msg = "Downloading";
                        break;
                    default:
                        msg = "Error";
                        break;
                }
                return new Tuple<int, string>(nCode, msg);
            }
        }

        public bool IsLoggedIn { get; private set; }

        public CapitalApiService()
        {

        }

        public async Task<bool> LoginAsync(string account, string password)
        {
            IsLoggedIn = false;

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            RegisterSkEventsIfNeeded();

            _isSkQuoteConnectionReady = false;
            _quoteConnectionReadyTcs = null;

            var resultCode = _api.SKCenterLib_Login(account.Trim(), password);
            if (resultCode != 0)
            {
                return false;
            }

            var enterMonitorCode = _api.SKQuoteLib_EnterMonitorLONG();
            if (enterMonitorCode != 0)
            {
                return false;
            }

            IsLoggedIn = await WaitForQuoteConnectionReadyAsync(15000);
            Account = account;
            Password = password;
            return IsLoggedIn;
        }

        public async Task ReLogin()
        {
            await LoginAsync(Account, Password);
        }

        public async Task SubscribeAsync(string symbol)
        {
            var normalized = symbol?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }
            foreach (var eachStock in symbol.Split(','))
            {
                _subscribedSymbols.Add(eachStock);
            }
            _api.SKQuoteLib_RequestStocks(1, GetSubscribedSymbolsAsCsv());
            await Task.CompletedTask;
        }

        public async Task UnsubscribeAsync(string symbol)
        {
            var normalized = symbol?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            _api.SKQuoteLib_CancelRequestStocks(normalized);
            _subscribedSymbols.Remove(normalized);
            await Task.CompletedTask;
        }

        public int RequestKLineByDate(string symbol, short kLineType, short outType, short tradeSession, string startDate, string endDate, short minuteNumber)
        {
            return _api.SKQuoteLib_RequestKLineAMByDate(symbol, kLineType, outType, tradeSession, startDate, endDate, minuteNumber);
        }

        public string GetReturnCodeMessage(int code)
        {
            return _api.SKCenterLib_GetReturnCodeMessage(code);
        }

        public SKSTOCKLONG GetRelativeStockMessage(string symbol)
        {
            var pSKStock = new SKSTOCKLONG();
            int nCode = _api.SKQuoteLib_GetStockByNoLONG(symbol, ref pSKStock);
            if (nCode == 0)
            {
                return pSKStock;
            }
            return pSKStock;
        }

        /// <summary>
        /// 以和主畫面訂閱股票相同的群益即時報價通道，取得一批股票的最新日內快照。
        /// 此方法使用第二個報價頁面，且不會取消使用者在主畫面（第一頁）的訂閱。
        /// </summary>
        public async Task<Dictionary<string, CandleData>> GetInstantQuoteSnapshotsAsync(
            IEnumerable<string> symbols,
            Action<int, int> progressCallback = null)
        {
            var snapshots = new Dictionary<string, CandleData>(StringComparer.OrdinalIgnoreCase);
            if (!IsLoggedIn || symbols == null)
            {
                return snapshots;
            }

            var pendingSymbols = new List<string>();
            var knownSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in symbols)
            {
                var symbol = item == null ? string.Empty : item.Trim();
                if (string.IsNullOrWhiteSpace(symbol) || !knownSymbols.Add(symbol))
                {
                    continue;
                }

                // 主畫面已經訂閱的股票不需要再申請臨時報價，直接使用同一個即時快取。
                if (_subscribedSymbols.Contains(symbol))
                {
                    var existingQuote = GetRelativeStockMessage(symbol);
                    CandleData existingCandle;
                    if (TryBuildScanInstantCandle(existingQuote, out existingCandle))
                    {
                        snapshots[symbol] = existingCandle;
                    }
                    continue;
                }

                pendingSymbols.Add(symbol);
            }

            const int batchSize = 50;
            const short scanQuotePageNo = 2;
            var completed = snapshots.Count;
            var total = knownSymbols.Count;
            progressCallback?.Invoke(completed, total);

            for (var offset = 0; offset < pendingSymbols.Count; offset += batchSize)
            {
                var batch = pendingSymbols.Skip(offset).Take(batchSize).ToList();
                var requested = new HashSet<string>(batch, StringComparer.OrdinalIgnoreCase);
                var received = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Action<string, SKSTOCKLONG> onQuoteReceived = null;

                onQuoteReceived = (reportedSymbol, quote) =>
                {
                    var matchedSymbol = FindRequestedSymbol(quote.bstrStockNo, requested);
                    if (matchedSymbol == null)
                    {
                        return;
                    }

                    CandleData candle;
                    if (!TryBuildScanInstantCandle(quote, out candle))
                    {
                        return;
                    }

                    lock (snapshots)
                    {
                        snapshots[matchedSymbol] = candle;
                        received.Add(matchedSymbol);
                    }
                };

                InstantDataRecevied += onQuoteReceived;
                try
                {
                    var requestCode = _api.SKQuoteLib_RequestStocks(scanQuotePageNo, string.Join(",", batch));
                    if (requestCode == 0)
                    {
                        var startedAt = DateTime.UtcNow;
                        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < 1500)
                        {
                            lock (snapshots)
                            {
                                if (received.Count == batch.Count)
                                {
                                    break;
                                }
                        }
                            await Task.Delay(40);
                        }
                    }

                    // 先在臨時報價請求仍有效時讀取群益快取，避免取消請求後快取被清理。
                    FillSnapshotGaps(batch, snapshots);
                }
                finally
                {
                    InstantDataRecevied -= onQuoteReceived;
                    _api.SKQuoteLib_CancelRequestStocks(string.Join(",", batch));
                }

                lock (snapshots)
                {
                    completed = snapshots.Count;
                }
                progressCallback?.Invoke(completed, total);
            }

            return snapshots;
        }

        private void FillSnapshotGaps(IEnumerable<string> symbols, Dictionary<string, CandleData> snapshots)
        {
            // 群益有時會更新內部報價快取，但不對每一檔觸發通知事件。
            // 事件只作為快速路徑，缺漏的股票改從同一個群益快取逐檔讀回，
            // 避免把「沒有事件」誤判成「沒有即時資料」。
            foreach (var symbol in symbols)
            {
                lock (snapshots)
                {
                    if (snapshots.ContainsKey(symbol))
                    {
                        continue;
                    }
                }

                var cachedQuote = GetRelativeStockMessage(symbol);
                CandleData cachedCandle;
                if (TryBuildScanInstantCandle(cachedQuote, out cachedCandle))
                {
                    lock (snapshots)
                    {
                        snapshots[symbol] = cachedCandle;
                    }
                }
            }
        }

        private void RegisterSkEventsIfNeeded()
        {
            if (_isSkEventsRegistered)
            {
                return;
            }

            _api.OnReplyMessage += OnAnnouncement;
            void OnAnnouncement(string strUserID, string bstrMessage, out short nConfirmCode)
            {
                nConfirmCode = -1;
            }

            _api.OnConnection += (nKind, code) =>
            {
                if (nKind == 3003)
                {
                    _isSkQuoteConnectionReady = true;
                    _quoteConnectionReadyTcs?.TrySetResult(true);
                }
            };

            _api.OnNotifyKLineData += (stockNo, raw) =>
            {
                if (TryParseKLineData(raw, out var candle))
                {
                    KLineDataReceived?.Invoke(stockNo, candle);
                }
            };

            _api.OnNotifyQuoteLONG += (stockNo, nIndex) =>
            {
                var skStock = new SKSTOCKLONG();
                var code = _api.SKQuoteLib_GetStockByIndexLONG(stockNo, nIndex, ref skStock);
                if (code == 0)
                {
                    InstantDataRecevied?.Invoke(skStock.bstrStockNo, skStock);
                    if (TryBuildInstantCandle(skStock, out var candle))
                    {
                        InstantCandleReceived?.Invoke(skStock.bstrStockNo, candle);
                    }
                }
            };

            _isSkEventsRegistered = true;
        }

        private string GetSubscribedSymbolsAsCsv()
        {
            return string.Join(",", _subscribedSymbols);
        }

        private static string FindRequestedSymbol(string reportedSymbol, IEnumerable<string> requestedSymbols)
        {
            if (string.IsNullOrWhiteSpace(reportedSymbol))
            {
                return null;
            }

            foreach (var requested in requestedSymbols)
            {
                if (string.Equals(reportedSymbol, requested, StringComparison.OrdinalIgnoreCase) ||
                    reportedSymbol.EndsWith(requested, StringComparison.OrdinalIgnoreCase))
                {
                    return requested;
                }
            }

            return null;
        }

        private async Task<bool> WaitForQuoteConnectionReadyAsync(int timeoutMs)
        {
            if (_isSkQuoteConnectionReady)
            {
                return true;
            }

            _quoteConnectionReadyTcs = new TaskCompletionSource<bool>();
            var completedTask = await Task.WhenAny(_quoteConnectionReadyTcs.Task, Task.Delay(timeoutMs));
            return completedTask == _quoteConnectionReadyTcs.Task && _quoteConnectionReadyTcs.Task.Result;
        }

        private static bool TryParseKLineData(string raw, out CandleData candle)
        {
            candle = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var parts = raw.Split(',');
            if (parts.Length < 6)
            {
                return false;
            }

            string timeText;
            int valueStartIndex;
            if (parts.Length >= 7)
            {
                timeText = $"{parts[0].Trim()} {parts[1].Trim()}";
                valueStartIndex = 2;
            }
            else
            {
                timeText = parts[0].Trim();
                valueStartIndex = 1;
            }

            if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return false;
            }

            if (!decimal.TryParse(parts[valueStartIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out var open) ||
                !decimal.TryParse(parts[valueStartIndex + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var high) ||
                !decimal.TryParse(parts[valueStartIndex + 2], NumberStyles.Any, CultureInfo.InvariantCulture, out var low) ||
                !decimal.TryParse(parts[valueStartIndex + 3], NumberStyles.Any, CultureInfo.InvariantCulture, out var close) ||
                !long.TryParse(parts[valueStartIndex + 4], NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            {
                return false;
            }

            candle = new CandleData
            {
                Time = time,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            };

            return true;
        }

        private static bool TryBuildInstantCandle(SKSTOCKLONG skStock, out CandleData candle)
        {
            candle = null;
            var close = NormalizePrice(skStock.nClose);
            var open = NormalizePrice(skStock.nOpen);
            var high = NormalizePrice(skStock.nHigh);
            var low = NormalizePrice(skStock.nLow);

            if (close <= 0)
            {
                close = open > 0 ? open : (high > 0 ? high : low);
            }

            if (open <= 0)
            {
                open = close;
            }

            if (high <= 0)
            {
                high = Math.Max(open, close);
            }

            if (low <= 0)
            {
                low = Math.Min(open, close);
            }

            if (close <= 0 || !TryParseDealTime(skStock.nTradingDay, skStock.nDealTime, out var parsedTime))
            {
                return false;
            }

            candle = new CandleData
            {
                Time = parsedTime,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = skStock.nYQty
            };

            return true;
        }

        private static bool TryBuildScanInstantCandle(SKSTOCKLONG skStock, out CandleData candle)
        {
            if (TryBuildInstantCandle(skStock, out candle))
            {
                return true;
            }

            // 報價快取可能只有價格，未帶完整成交日期時間；掃描日期由掃描端
            // 以本次請求日期統一標記，價格仍然完全來自群益快取。
            var close = NormalizePrice(skStock.nClose);
            if (close <= 0)
            {
                candle = null;
                return false;
            }

            var open = NormalizePrice(skStock.nOpen);
            var high = NormalizePrice(skStock.nHigh);
            var low = NormalizePrice(skStock.nLow);
            if (open <= 0) open = close;
            if (high <= 0) high = Math.Max(open, close);
            if (low <= 0) low = Math.Min(open, close);

            candle = new CandleData
            {
                Time = DateTime.Today,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = skStock.nYQty
            };
            return true;
        }

        private static decimal NormalizePrice(int rawPrice)
        {
            return rawPrice / 100m;
        }

        private static bool TryParseDealTime(int tradingDay, int dealTime, out DateTime time)
        {
            time = default(DateTime);
            if (tradingDay <= 0)
            {
                return false;
            }

            var dayText = tradingDay.ToString("D8");
            var dealTimeText = Math.Abs(dealTime).ToString();

            if (string.IsNullOrWhiteSpace(dealTimeText))
            {
                return false;
            }

            if (dealTimeText.Length > 6)
            {
                dealTimeText = dealTimeText.Substring(0, 6);
            }

            dealTimeText = dealTimeText.PadLeft(6, '0');
            var fullTimeText = dayText + dealTimeText;
            if (DateTime.TryParseExact(fullTimeText, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return true;
            }

            var minuteTimeText = dayText + dealTimeText.Substring(0, 4);
            return DateTime.TryParseExact(minuteTimeText, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }
    }
}
