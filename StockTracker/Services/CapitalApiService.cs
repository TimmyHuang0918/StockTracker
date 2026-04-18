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

            _subscribedSymbols.Add(normalized);
	    _api.SKQuoteLib_RequestStocks(1, normalized);

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
	    var close = skStock.nClose / 100;
	    var open = skStock.nOpen / 100;
	    var high = skStock.nHigh / 100;
	    var low = skStock.nLow / 100;
	    var volume = skStock.nYQty;
	    var time = skStock.nTradingDay.ToString() + (skStock.nDealTime / 100).ToString();
	    var date = DateTime.TryParseExact(time, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime);

	    candle = new CandleData
	    {
		Time = parsedTime,
		Open = open,
		High = high,
		Low = low,
		Close = close,
		Volume = volume
	    };

	    return true;
	}
    }
}
