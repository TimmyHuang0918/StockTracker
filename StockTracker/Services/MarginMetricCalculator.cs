using StockTracker.Models;
using System;
using System.Collections.Generic;

namespace StockTracker.Services
{
    public class MarginMetricCalculator
    {
        private const double SharesPerLot = 1000d;
        private const double TwseMarginRatio = 0.6d;
        private const double TpexMarginRatio = 0.5d;

        public List<TwseMarginMetricResult> CalculateMarginMetrics(List<TwseMarginRecord> records, Dictionary<DateTime, double> priceDict)
        {
            var results = new List<TwseMarginMetricResult>();
            if (records == null || records.Count == 0)
            {
                return results;
            }

            var normalizedPriceDict = NormalizePriceDictionary(priceDict);
            results.Capacity = records.Count;

            double previousTotalLoan = 0d;
            double previousAverageCost = 0d;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null)
                {
                    continue;
                }

                var close = GetClosePrice(normalizedPriceDict, record.TradeDate);
                var marginRatio = ResolveMarginRatio(record.Market);
                var balanceShares = Math.Max(0L, record.MarginBalance);
                var totalLoan = 0d;
                var maintenanceRatio = 0d;
                var averageCost = 0d;

                if (i == 0)
                {
                    if (close > 0d && balanceShares > 0)
                    {
                        totalLoan = balanceShares * SharesPerLot * close * marginRatio;
                        averageCost = close;
                        maintenanceRatio = marginRatio > 0d ? (1d / marginRatio) * 100d : 0d;
                    }
                }
                else
                {
                    var deltaShares = record.MarginPurchaseSales;
                    if (deltaShares > 0)
                    {
                        var addedLoan = deltaShares * SharesPerLot * close * marginRatio;
                        totalLoan = previousTotalLoan + Math.Max(0d, addedLoan);
                    }
                    else
                    {
                        var reducedLoan = Math.Abs(deltaShares) * SharesPerLot * previousAverageCost * marginRatio;
                        totalLoan = Math.Max(0d, previousTotalLoan - Math.Max(0d, reducedLoan));
                    }

                    if (balanceShares <= 0)
                    {
                        totalLoan = 0d;
                    }

                    if (totalLoan > 0d && balanceShares > 0 && marginRatio > 0d)
                    {
                        var marketValue = balanceShares * SharesPerLot * close;
                        maintenanceRatio = marketValue > 0d ? (marketValue / totalLoan) * 100d : 0d;
                        averageCost = totalLoan / (balanceShares * SharesPerLot * marginRatio);
                    }
                }

                results.Add(new TwseMarginMetricResult
                {
                    Record = record,
                    Close = close,
                    TotalLoan = totalLoan,
                    MarginMaintenanceRatio = maintenanceRatio,
                    MarginAverageCost = averageCost
                });

                previousTotalLoan = totalLoan;
                previousAverageCost = averageCost;
            }

            return results;
        }

        private static Dictionary<DateTime, double> NormalizePriceDictionary(Dictionary<DateTime, double> priceDict)
        {
            var normalized = new Dictionary<DateTime, double>();
            if (priceDict == null)
            {
                return normalized;
            }

            foreach (var pair in priceDict)
            {
                normalized[pair.Key.Date] = pair.Value > 0d ? pair.Value : 0d;
            }

            return normalized;
        }

        private static double GetClosePrice(Dictionary<DateTime, double> priceDict, DateTime tradeDate)
        {
            if (priceDict == null)
            {
                return 0d;
            }

            double close;
            return priceDict.TryGetValue(tradeDate.Date, out close) && close > 0d ? close : 0d;
        }

        private static double ResolveMarginRatio(string market)
        {
            if (string.IsNullOrWhiteSpace(market))
            {
                return TwseMarginRatio;
            }

            if (market.IndexOf("上櫃", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(market, "TPEX", StringComparison.OrdinalIgnoreCase))
            {
                return TpexMarginRatio;
            }

            return TwseMarginRatio;
        }
    }
}
