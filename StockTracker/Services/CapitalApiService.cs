using SKCOMLib;
using StockManager.Services;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace StockTracker.Services
{
    public class CapitalApiService
    {
        private readonly HashSet<string> _subscribedSymbols = new HashSet<string>();
        private readonly SKAPI _api = App.Api;
        private bool _isSkEventsRegistered;
        private bool _isSkQuoteConnectionReady;
        private TaskCompletionSource<bool> _quoteConnectionReadyTcs;

        public event Action<string, CandleData> KLineDataReceived;
	public event Action<string, SKSTOCKLONG> InstantDataRecevied;
	public event Action<string, CandleData> InstantCandleReceived;

        public bool IsLoggedIn { get; private set; }

        public CapitalApiService()
        {

        }

        public async Task<bool> LoginAsync(string account, string password)
        {
            if (IsLoggedIn)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            RegisterSkEventsIfNeeded();
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
            return IsLoggedIn;
        }

        public async Task SubscribeAsync(string symbol)
        {
            var normalized = symbol?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
	    }
	    foreach(var eachStock in symbol.Split(','))
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
		if(code == 0)
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
